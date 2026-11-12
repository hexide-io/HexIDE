using HexIDE.Runtime.Serialization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;
using HexIDE.Bookmarks;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.ViewModels;

public class ServerApplyEditTests : IAsyncDisposable
{
    private ILspClient client = Substitute.For<ILspClient>();
    private readonly IProjectService projects = Substitute.For<IProjectService>();
    private readonly ILocalizationService localization = Substitute.For<ILocalizationService>();
    private readonly IStatusBarService status = Substitute.For<IStatusBarService>();
    private readonly IDocumentDockService dock = Substitute.For<IDocumentDockService>();
    private readonly List<BaseEditorWindowViewModel> windows = [];
    private readonly EditorService editors;
    private BaseEditorWindowViewModel? active;
    private readonly ModuleDefinition first;
    private readonly ModuleDefinition second;

    public ServerApplyEditTests()
    {
        var project = TestHelpers.CreateProject();
        first = TestHelpers.CreateModule(project, "Module1");
        second = TestHelpers.CreateModule(project, "Module2");
        foreach (var module in new[] { first, second })
        {
            module.AbsolutePath = Path.GetFullPath(module.Name + ".bas");
            module.UpdateCode("Public Sub Greet()\r\n    Greet\r\nEnd Sub\r\n");
            project.AddModule(module);
        }
        var manager = Substitute.For<IProjectManager>();
        manager.LoadedProjects.Returns([project]);
        dock.OpenDocuments.Returns(windows);
        dock.When(d => d.OpenDocument(Arg.Any<BaseEditorWindowViewModel>(), Arg.Any<bool>()))
            .Do(c =>
            {
                var editor = c.Arg<BaseEditorWindowViewModel>();
                windows.Add(editor);
                if (c.Arg<bool>()) active = editor;
            });
        dock.TryActivate(Arg.Any<Func<CodeEditorViewModel, bool>>(), Arg.Any<bool>())
            .Returns(c =>
            {
                var found = windows.OfType<CodeEditorViewModel>().FirstOrDefault(c.Arg<Func<CodeEditorViewModel, bool>>());
                if (found is null) return false;
                if (c.Arg<bool>()) active = found;
                return true;
            });
        editors = new EditorService(dock, NewEditor, () => throw new InvalidOperationException(),
            () => throw new InvalidOperationException(), () => throw new InvalidOperationException(), () => manager, localization);
        localization.GetString("Str.Lsp.EditNotApplied").Returns("Server edit not applied: {0}");
        localization.GetString("Str.Lsp.EditRequiresUserCommand").Returns("HexIDE applies a server's edits only while a command the user started is running.");
        localization.GetString("Str.CodeEditor.Msg.RenameDocumentRefused").Returns("Rename refused: {0}");
        localization.GetString("Str.CodeEditor.Msg.RenameStaleVersion").Returns("Cannot rename {0}: stale version. Try again.");
        localization.GetString("Str.CodeEditor.Msg.RenameUnsavedChanges").Returns("Cannot rename {0}: unsaved changes. Save and try again.");
        localization.GetString("Str.CodeEditor.Msg.RenamedInModules").Returns("Renamed in {0} modules");
        localization.GetString("Str.CodeEditor.QuickFix.ChangedTarget").Returns("{0} changed since the server read it. Try again.");
    }

    private CodeEditorViewModel NewEditor() => new(
        Substitute.For<IWindowManager>(), editors, projects, Substitute.For<IEventBus>(), client,
        Substitute.For<ISettingsService>(), status, Substitute.For<IBookmarkService>(),
        Substitute.For<HexIDE.Debugging.IBreakpointService>(),
        Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(), Substitute.For<HexIDE.Debugging.IRunScope>(), localization,
            new Vb6ProjectFileFormat(),
            new Vb6ReadOnlyRegionReader());

    private CodeEditorViewModel Open(ModuleDefinition module)
    {
        editors.EditCode(module);
        return windows.OfType<CodeEditorViewModel>().Single(w => w.ModuleDefinition == module);
    }

    private static string UriOf(ModuleDefinition module) => DocumentWireName.For(DocumentIdentity.For(module));

    private static TextEdit Rename(CodeEditorViewModel editor)
    {
        var offset = editor.Document.Text.IndexOf("Greet", StringComparison.Ordinal);
        var location = editor.Document.GetLocation(offset);
        return new(new(new(location.Line - 1, location.Column - 1), new(location.Line - 1, location.Column + 4)), "Welcome");
    }

    private static TextEdit RenameClosed()
    {
        // A standard module's file begins with Attribute VB_Name, then its code.
        return new(new(new(1, 11), new(1, 16)), "Welcome");
    }

    private readonly List<VBLspClient> connections = [];
    private readonly List<JsonRpc> peers = [];
    private readonly CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
    private EditServer server = null!;

    private async Task<VBLspClient> ConnectAsync(bool withEditor = true)
    {
        var (local, remote) = FullDuplexStream.CreatePair();
        var stub = new EditServer();
        var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(remote, remote, new SystemTextJsonFormatter()), stub);
        stub.Rpc = rpc;
        peers.Add(rpc);
        rpc.StartListening();
        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.LeaveServerRunning.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(c => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(local, local, c.Arg<IJsonRpcMessageFormatter>())));
        var connection = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), "vb6",
            applyEdit: !withEditor ? null : (edit, documentFor, token) => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                () => editors.ApplyWorkspaceEdit(edit, documentFor), Avalonia.Threading.DispatcherPriority.Normal, token).GetTask());
        connections.Add(connection);
        await connection.StartAsync(timeout.Token);
        server = stub;
        client = connection;
        return connection;
    }

    private WorkspaceEdit Edit(CodeEditorViewModel a, CodeEditorViewModel b, bool versioned = true, int version = 1) => versioned
        ? new(DocumentChanges: [new(new(UriOf(first), 1), [Rename(a)]), new(new(UriOf(second), version), [Rename(b)])])
        : new(new() { [UriOf(first)] = [Rename(a)], [UriOf(second)] = [Rename(b)] });

    private async Task<JsonElement> SendEdit(EditServer peer, WorkspaceEdit edit) =>
        await peer.Rpc.InvokeWithParameterObjectAsync<JsonElement>("workspace/applyEdit",
            new ApplyWorkspaceEditParams(edit), timeout.Token);

    [AvaloniaTheory]
    [InlineData("lens")]
    [InlineData("codeAction")]
    [InlineData("executeCommand")]
    [InlineData("unsolicited")]
    public async Task RefusedServerEditShowsItsWireReasonInStatusBar(string trigger)
    {
        var connection = await ConnectAsync();
        using var reporter = new LanguageServerMessageReporter(connection, status, localization);
        var a = Open(first);
        var b = Open(second);
        await a.FlushDocumentAsync();
        await b.FlushDocumentAsync();
        var before = b.Document.Text;
        var edit = Edit(a, b, version: 99);
        server.DuringCommand = async () => await SendEdit(server, edit);
        if (trigger == "lens")
        {
            var lens = (await connection.RequestCodeLensesAsync(UriOf(first), timeout.Token)).Single();
            await a.RunCodeLensAsync(lens.Command!);
        }
        else if (trigger == "codeAction")
        {
            var action = (await connection.RequestCodeActionsAsync(UriOf(first), new(new(1, 0), new(1, 1)), [], timeout.Token)).Single();
            await a.ApplyCodeActionAsync(action, timeout.Token);
        }
        else if (trigger == "executeCommand")
            await connection.ExecuteCommandAsync("test.edit", cancellationToken: timeout.Token);
        else server.EditAnswer = await SendEdit(server, edit);

        server.EditAnswer.GetProperty("applied").GetBoolean().Should().BeFalse();
        var reason = server.EditAnswer.GetProperty("failureReason").GetString()!;
        reason.Should().NotBeNullOrWhiteSpace();
        status.Received().SetTemporaryMessage(Arg.Is<string>(message => message.Contains(reason)), TimeSpan.FromSeconds(10));
        b.Document.Text.Should().Be(before);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClientRefusalIsDisplayedInTheSelectedLanguage(bool duringCommand)
    {
        var french = new LocalizationService();
        french.Apply("fr");
        var connection = await ConnectAsync(withEditor: false);
        using var reporter = new LanguageServerMessageReporter(connection, status, french);
        var edit = new WorkspaceEdit();
        if (duringCommand)
        {
            server.DuringCommand = async () => await SendEdit(server, edit);
            await connection.ExecuteCommandAsync("test.edit", cancellationToken: timeout.Token);
        }
        else server.EditAnswer = await SendEdit(server, edit);

        var expected = duringCommand ? "Aucun éditeur d’espace de travail n’est disponible."
            : "HexIDE applique les modifications d’un serveur uniquement pendant l’exécution d’une commande lancée par l’utilisateur.";
        status.Received().SetTemporaryMessage(Arg.Is<string>(message => message == string.Format(french.GetString("Str.Lsp.EditNotApplied"), expected)), TimeSpan.FromSeconds(10));
        server.EditAnswer.GetProperty("failureReason").GetString().Should().Be(duringCommand
            ? "No workspace editor is available."
            : "HexIDE applies a server's edits only while a command the user started is running.");
    }

    [AvaloniaFact]
    public async Task MissingWorkspaceEditorIsDisplayedInTheSelectedLanguage()
    {
        var french = new LocalizationService();
        french.Apply("fr");
        localization.GetString(Arg.Any<string>()).Returns(c => french.GetString(c.Arg<string>()));
        var connection = await ConnectAsync();
        using var reporter = new LanguageServerMessageReporter(connection, status, french);
        server.DuringCommand = async () => await SendEdit(server, new WorkspaceEdit());
        await connection.ExecuteCommandAsync("test.edit", cancellationToken: timeout.Token);
        status.Received().SetTemporaryMessage(Arg.Is<string>(message => message.Contains("Aucun éditeur d’espace de travail n’est disponible.")), TimeSpan.FromSeconds(10));
    }

    [AvaloniaFact]
    public async Task ChangedTargetRetainsTheSelectedLanguage()
    {
        var french = new LocalizationService();
        french.Apply("fr");
        localization.GetString(Arg.Any<string>()).Returns(c => french.GetString(c.Arg<string>()));
        var connection = await ConnectAsync();
        using var reporter = new LanguageServerMessageReporter(connection, status, french);
        var a = Open(first);
        var b = Open(second);
        await a.FlushDocumentAsync();
        await b.FlushDocumentAsync();
        var edit = Edit(a, b, version: 99);
        server.DuringCommand = async () => await SendEdit(server, edit);
        var lens = (await connection.RequestCodeLensesAsync(UriOf(first), timeout.Token)).Single();
        await a.RunCodeLensAsync(lens.Command!);
        var expected = string.Format(french.GetString("Str.CodeEditor.QuickFix.ChangedTarget"), "Module2");
        expected.Should().NotBe("Module2 changed since the server read it. Try again.");
        server.EditAnswer.GetProperty("failureReason").GetString().Should().Be(expected);
        status.Received().SetTemporaryMessage(Arg.Is<string>(message => message == string.Format(french.GetString("Str.Lsp.EditNotApplied"), expected)), TimeSpan.FromSeconds(10));
    }

    [AvaloniaFact]
    public async Task InitializeDeclaresApplyEditAndDocumentChanges()
    {
        await ConnectAsync();
        var workspace = server.InitializeParams.GetProperty("capabilities").GetProperty("workspace");
        workspace.GetProperty("applyEdit").GetBoolean().Should().BeTrue();
        workspace.GetProperty("workspaceEdit").GetProperty("documentChanges").GetBoolean().Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task LensInSecondWindowKeepsThatWindowActiveAfterEdit()
    {
        var connection = await ConnectAsync();
        var a = Open(first);
        var b = Open(second);
        dock.ActiveTab.Returns(_ => active);
        dock.TryActivateAny(Arg.Any<Func<Dock.Model.Core.IDockable, bool>>()).Returns(c =>
        {
            active = windows.Single(w => c.Arg<Func<Dock.Model.Core.IDockable, bool>>()(w));
            return true;
        });
        await a.FlushDocumentAsync();
        await b.FlushDocumentAsync();
        var edit = Edit(a, b);
        server.DuringCommand = async () => await SendEdit(server, edit);
        var lens = (await connection.RequestCodeLensesAsync(UriOf(second), timeout.Token)).Single();
        await b.RunCodeLensAsync(lens.Command!);
        server.EditAnswer.GetProperty("applied").GetBoolean().Should().BeTrue();
        active.Should().BeSameAs(b);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LensCommandAppliesBothModulesWithIndependentUndo(bool versioned)
    {
        var connection = await ConnectAsync();
        var a = Open(first);
        var b = Open(second);
        await a.FlushDocumentAsync();
        await b.FlushDocumentAsync();
        var beforeA = a.Document.Text;
        var beforeB = b.Document.Text;
        var edit = Edit(a, b, versioned);
        server.DuringCommand = async () => await SendEdit(server, edit);
        var lens = (await connection.RequestCodeLensesAsync(UriOf(first), timeout.Token)).Single();
        await a.RunCodeLensAsync(lens.Command!);
        server.EditAnswer.GetProperty("applied").GetBoolean().Should().BeTrue();
        a.Document.Text.Should().Contain("Sub Welcome()");
        b.Document.Text.Should().Contain("Sub Welcome()");
        a.Document.UndoStack.Undo();
        a.Document.Text.Should().Be(beforeA);
        b.Document.Text.Should().Contain("Sub Welcome()");
        b.Document.UndoStack.Undo();
        b.Document.Text.Should().Be(beforeB);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(false, true)]
    public async Task ConsecutiveUnversionedEditsApplyWithoutSaving(bool documentChanges, bool codeAction = false)
    {
        var connection = await ConnectAsync();
        var a = Open(first);
        var b = Open(second);
        for (var i = 0; i < 2; i++)
        {
            await a.FlushDocumentAsync();
            await b.FlushDocumentAsync();
            var edit = UnversionedEdit(b, documentChanges, i == 0 ? "Welcome" : "Again");
            server.DuringCommand = async () => await SendEdit(server, edit);
            if (codeAction)
            {
                var action = (await connection.RequestCodeActionsAsync(UriOf(first), new(new(1, 0), new(1, 1)), [], timeout.Token)).Single();
                await a.ApplyCodeActionAsync(action, timeout.Token);
            }
            else await connection.ExecuteCommandAsync("test.edit", cancellationToken: timeout.Token);
            server.EditAnswer.GetProperty("applied").GetBoolean().Should().BeTrue();
            b.Document.Text.Should().Contain(i == 0 ? "Sub Welcome()" : "Sub Again()");
            b.IsModified.Should().BeTrue();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnversionedEditRefusesTypingNotYetSentToServer(bool documentChanges)
    {
        var connection = await ConnectAsync();
        var a = Open(first);
        var b = Open(second);
        await a.FlushDocumentAsync();
        await b.FlushDocumentAsync();
        var edit = UnversionedEdit(b, documentChanges, "Welcome");
        server.DuringCommand = async () =>
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                b.Document.Insert(b.Document.TextLength, "' typed during command\r\n"));
            return await SendEdit(server, edit);
        };
        await connection.ExecuteCommandAsync("test.edit", cancellationToken: timeout.Token);
        server.EditAnswer.GetProperty("applied").GetBoolean().Should().BeFalse();
        server.EditAnswer.GetProperty("failureReason").GetString().Should().Be("Module2 changed since the server read it. Try again.");
        b.Document.Text.Should().Contain("Sub Greet()");
        b.Document.Text.Should().Contain("' typed during command");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommandSnapshotPredatesTypingSentWhileEditIsHeld(bool documentChanges)
    {
        var connection = await ConnectAsync();
        var a = Open(first);
        var b = Open(second);
        await a.FlushDocumentAsync();
        await b.FlushDocumentAsync();
        var edit = UnversionedEdit(b, documentChanges, "Welcome");
        server.DuringCommand = async () =>
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                b.Document.Insert(b.Document.TextLength, "' typed during command\r\n");
                await b.FlushDocumentAsync();
            });
            await server.TypedChange.Task.WaitAsync(timeout.Token);
            return await SendEdit(server, edit);
        };
        await connection.ExecuteCommandAsync("test.edit", cancellationToken: timeout.Token);
        server.EditAnswer.GetProperty("applied").GetBoolean().Should().BeFalse();
        server.EditAnswer.GetProperty("failureReason").GetString().Should().Be("Module2 changed since the server read it. Try again.");
        b.Document.Text.Should().Contain("Sub Greet()");
        b.Document.Text.Should().Contain("' typed during command");
    }

    [AvaloniaFact]
    public async Task OverlappingCommandsUseOldestSnapshot()
    {
        var connection = await ConnectAsync();
        var b = Open(second);
        await b.FlushDocumentAsync();
        var edit = UnversionedEdit(b, false, "Welcome");
        server.DuringCommand = async () =>
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                b.Document.Insert(b.Document.TextLength, "' typed during command\r\n");
                await b.FlushDocumentAsync();
            });
            await server.TypedChange.Task.WaitAsync(timeout.Token);
            server.DuringCommand = async () => await SendEdit(server, edit);
            await connection.ExecuteCommandAsync("test.edit", cancellationToken: timeout.Token);
            return server.EditAnswer;
        };
        await connection.ExecuteCommandAsync("test.edit", cancellationToken: timeout.Token);
        server.EditAnswer.GetProperty("applied").GetBoolean().Should().BeFalse();
        server.EditAnswer.GetProperty("failureReason").GetString().Should().Be("Module2 changed since the server read it. Try again.");
        b.Document.Text.Should().Contain("Sub Greet()");
    }

    [AvaloniaFact]
    public async Task UnsolicitedUnversionedEditRefusesDirtyTarget()
    {
        await ConnectAsync();
        var b = Open(second);
        b.Document.Insert(b.Document.TextLength, "' unsaved\r\n");
        await b.FlushDocumentAsync();
        var before = b.Document.Text;
        var answer = await SendEdit(server, UnversionedEdit(b, false, "Welcome"));
        answer.GetProperty("applied").GetBoolean().Should().BeFalse();
        b.Document.Text.Should().Be(before);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task RenameSnapshotPredatesTypingSentWhileRequestIsPending(bool documentChanges, bool undo = false)
    {
        var connection = await ConnectAsync();
        var a = Open(first);
        var b = Open(second);
        await a.FlushDocumentAsync();
        await b.FlushDocumentAsync();
        var before = b.Document.Text;
        var edit = UnversionedEdit(b, documentChanges, "Welcome");
        server.DuringRename = async () =>
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                b.Document.Insert(b.Document.TextLength, "' typed during request\r\n");
                if (undo) b.Document.UndoStack.Undo();
                await b.FlushDocumentAsync();
            });
            return edit;
        };
        var answer = await connection.RequestRenameAsync(UriOf(first), new(1, 11), "Welcome", timeout.Token);
        answer!.DocumentFor!(UriOf(second))!.Text.Should().Be(before);
        a.ApplyRename(answer.Edit!, "", answer.DocumentFor, codeAction: true)
            .Should().Be("Module2 changed since the server read it. Try again.");
        b.Document.Text.Should().Contain("Sub Greet()");
        if (undo) b.Document.Text.Should().Be(before);
        else b.Document.Text.Should().Contain("' typed during request");
    }

    [AvaloniaFact]
    public async Task VersionedRenameStillUsesLatestSentVersion()
    {
        var connection = await ConnectAsync();
        var a = Open(first);
        var b = Open(second);
        await a.FlushDocumentAsync();
        await b.FlushDocumentAsync();
        var edit = new WorkspaceEdit(DocumentChanges: [new(new(UriOf(second), 2), [Rename(b)])]);
        server.DuringRename = async () =>
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                b.Document.Insert(b.Document.TextLength, "' sent during request\r\n");
                await b.FlushDocumentAsync();
            });
            return edit;
        };
        var answer = await connection.RequestRenameAsync(UriOf(first), new(1, 11), "Welcome", timeout.Token);
        a.ApplyRename(answer!.Edit!, "Greet", answer.DocumentFor).Should().BeNull();
        b.Document.Text.Should().Contain("Sub Welcome()");
    }

    private WorkspaceEdit UnversionedEdit(CodeEditorViewModel target, bool documentChanges, string name)
    {
        var line = target.Document.Lines.Single(l => target.Document.GetText(l).StartsWith("Public Sub ", StringComparison.Ordinal));
        TextEdit edit = new(new(new(line.LineNumber - 1, 11), new(line.LineNumber - 1, line.Length - 2)), name);
        return documentChanges ? new(DocumentChanges: [new(new(UriOf(second), null), [edit])])
            : new(new() { [UriOf(second)] = [edit] });
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OutsideACommandRefusesAndChangesNothing(bool afterAnswer)
    {
        var connection = await ConnectAsync();
        var a = Open(first);
        var b = Open(second);
        if (afterAnswer) await connection.ExecuteCommandAsync("test.edit", cancellationToken: timeout.Token);
        var beforeA = a.Document.Text;
        var beforeB = b.Document.Text;
        var answer = await SendEdit(server, Edit(a, b));
        answer.GetProperty("applied").GetBoolean().Should().BeFalse();
        answer.GetProperty("failureReason").GetString().Should().Contain("command the user started");
        a.Document.Text.Should().Be(beforeA);
        b.Document.Text.Should().Be(beforeB);
    }

    [AvaloniaFact]
    public async Task StaleEditDuringLensCommandReturnsAppliersRefusalWithoutPartialWrite()
    {
        var connection = await ConnectAsync();
        var a = Open(first);
        var b = Open(second);
        await a.FlushDocumentAsync();
        await b.FlushDocumentAsync();
        var beforeA = a.Document.Text;
        var beforeB = b.Document.Text;
        var edit = Edit(a, b, version: 99);
        server.DuringCommand = async () => await SendEdit(server, edit);
        var lens = (await connection.RequestCodeLensesAsync(UriOf(first), timeout.Token)).Single();
        await a.RunCodeLensAsync(lens.Command!);
        server.EditAnswer.GetProperty("applied").GetBoolean().Should().BeFalse();
        server.EditAnswer.GetProperty("failureReason").GetString().Should().Be("Module2 changed since the server read it. Try again.");
        a.Document.Text.Should().Be(beforeA);
        b.Document.Text.Should().Be(beforeB);
    }

    [AvaloniaFact]
    public async Task AnotherServersCommandDoesNotAuthorizeEdits()
    {
        var other = await ConnectAsync();
        var otherServer = server;
        var connection = await ConnectAsync();
        var a = Open(first);
        var b = Open(second);
        var before = a.Document.Text;
        var edit = Edit(a, b);
        server.DuringCommand = async () => await SendEdit(otherServer, edit);
        await connection.ExecuteCommandAsync("test.edit", cancellationToken: timeout.Token);
        server.EditAnswer.GetProperty("applied").GetBoolean().Should().BeFalse();
        server.EditAnswer.GetProperty("failureReason").GetString().Should().Contain("command the user started");
        a.Document.Text.Should().Be(before);
    }

    private sealed class EditServer
    {
        public JsonRpc Rpc = null!;
        public JsonElement InitializeParams;
        public JsonElement EditAnswer;
        public Func<Task<JsonElement>>? DuringCommand;
        public Func<Task<WorkspaceEdit>>? DuringRename;
        public readonly TaskCompletionSource TypedChange = new(TaskCreationOptions.RunContinuationsAsynchronously);

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement p)
        {
            InitializeParams = p.Clone();
            return JsonDocument.Parse("""{"capabilities":{"textDocumentSync":1,"renameProvider":true,"codeActionProvider":true,"codeLensProvider":{},"executeCommandProvider":{"commands":["test.edit"]}}}""").RootElement.Clone();
        }
        [JsonRpcMethod("initialized", UseSingleObjectParameterDeserialization = true)]
        public void Initialized(JsonElement p) { }
        [JsonRpcMethod("textDocument/didOpen", UseSingleObjectParameterDeserialization = true)]
        public void Open(JsonElement p) { }
        [JsonRpcMethod("textDocument/didChange", UseSingleObjectParameterDeserialization = true)]
        public void Change(JsonElement p)
        {
            if (p.GetProperty("contentChanges")[0].GetProperty("text").GetString()!.Contains("' typed during command", StringComparison.Ordinal))
                TypedChange.TrySetResult();
        }
        [JsonRpcMethod("textDocument/codeLens", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Lenses(JsonElement p) => JsonDocument.Parse("""[{"range":{"start":{"line":1,"character":0},"end":{"line":1,"character":0}},"command":{"title":"Edit","command":"test.edit"}}]""").RootElement.Clone();
        [JsonRpcMethod("textDocument/codeAction", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Actions(JsonElement p) => JsonDocument.Parse("""[{"title":"Edit","command":"test.edit"}]""").RootElement.Clone();
        [JsonRpcMethod("textDocument/rename", UseSingleObjectParameterDeserialization = true)]
        public Task<WorkspaceEdit> Rename(JsonElement p) => DuringRename!();
        [JsonRpcMethod("workspace/executeCommand", UseSingleObjectParameterDeserialization = true)]
        public async Task<JsonElement?> Execute(JsonElement p)
        {
            if (DuringCommand is { } run) EditAnswer = await run();
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var window in windows) window.Dispose();
        foreach (var connection in connections) await connection.DisposeAsync();
        foreach (var peer in peers) peer.Dispose();
        timeout.Dispose();
    }
}
