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
            () => throw new InvalidOperationException(), () => throw new InvalidOperationException(), () => manager);
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
        Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(), Substitute.For<HexIDE.Debugging.IRunScope>(), localization);

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

    private async Task<VBLspClient> ConnectAsync()
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
            applyEdit: (edit, documentFor, token) => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
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

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement p)
        {
            InitializeParams = p.Clone();
            return JsonDocument.Parse("""{"capabilities":{"textDocumentSync":1,"codeLensProvider":{},"executeCommandProvider":{"commands":["test.edit"]}}}""").RootElement.Clone();
        }
        [JsonRpcMethod("initialized", UseSingleObjectParameterDeserialization = true)]
        public void Initialized(JsonElement p) { }
        [JsonRpcMethod("textDocument/didOpen", UseSingleObjectParameterDeserialization = true)]
        public void Open(JsonElement p) { }
        [JsonRpcMethod("textDocument/didChange", UseSingleObjectParameterDeserialization = true)]
        public void Change(JsonElement p) { }
        [JsonRpcMethod("textDocument/codeLens", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Lenses(JsonElement p) => JsonDocument.Parse("""[{"range":{"start":{"line":1,"character":0},"end":{"line":1,"character":0}},"command":{"title":"Edit","command":"test.edit"}}]""").RootElement.Clone();
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
