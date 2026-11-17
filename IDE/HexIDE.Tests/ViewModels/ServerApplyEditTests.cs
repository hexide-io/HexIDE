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
        localization.GetString("Str.CodeEditor.QuickFix.Refused").Returns("Edit refused: {0}");
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
    private readonly List<EditServer> stubs = [];
    private readonly CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
    private EditServer server = null!;

    // #3520: on CI, now and then a few of these tests find the stand-in's connection already gone, a millisecond or two
    // in, while the test's own token is still live. Once a client's initialize frame arrived with header bytes inside
    // its body. It hasn't reproduced off CI, so the class records what each side saw, and a test that hits it fails
    // saying so: the stand-in's disconnect reason and exception, the last bytes it read, the client's handshake steps
    // and log, and whether the stand-in ever received initialize.
    private readonly long started = System.Diagnostics.Stopwatch.GetTimestamp();
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> standInDrops = new();
    private readonly List<RecordingLogger> clientLogs = [];
    private readonly List<ReadTap> standInReads = [];

    // The stand-in's end of the pair, keeping the last bytes it read, so a frame it couldn't parse can be shown.
    private sealed class ReadTap(Stream inner) : Stream
    {
        private const int Keep = 4096;
        private readonly Queue<byte> recent = new();

        public string Recent()
        {
            byte[] bytes;
            lock (recent) bytes = [.. recent];
            var text = System.Text.Encoding.UTF8.GetString(bytes);
            return text.Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private void Record(ReadOnlySpan<byte> read)
        {
            lock (recent)
                foreach (var b in read)
                {
                    recent.Enqueue(b);
                    if (recent.Count > Keep) recent.Dequeue();
                }
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var n = await inner.ReadAsync(buffer, cancellationToken);
            Record(buffer.Span[..n]);
            return n;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = inner.Read(buffer, offset, count);
            Record(buffer.AsSpan(offset, n));
            return n;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.WriteAsync(buffer, cancellationToken);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.WriteAsync(buffer, offset, count, cancellationToken);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override void Flush() => inner.Flush();
        public override bool CanRead => inner.CanRead;
        public override bool CanWrite => inner.CanWrite;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private string Elapsed() =>
        $"{System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1}ms";

    private sealed class RecordingLogger(ServerApplyEditTests test, int connection) : ILogger<VBLspClient>
    {
        public readonly System.Collections.Concurrent.ConcurrentQueue<string> Lines = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Lines.Enqueue($"{test.Elapsed()} client {connection} {logLevel}: {formatter(state, exception)}"
                + (exception is null ? "" : $" {exception}"));
    }

    private string Diagnosis(string what)
    {
        static string Steps(LanguageConnectionAttempt? attempt) => attempt is null ? "none"
            : $"reached {attempt.ReachedStage}: " + string.Join(", ", attempt.Steps.Select(s =>
                $"{s.Stage} {s.Outcome} at {s.At?.TotalMilliseconds:F1}ms{(s.Detail is null ? "" : $" ({s.Detail})")}"));
        var clients = connections.Select((c, i) =>
            $"client {i + 1}: running={c.IsRunning}, attempt {Steps(c.LastAttempt)}, "
            + $"stand-in received initialize={stubs[i].InitializeParams.ValueKind != JsonValueKind.Undefined}");
        var reads = standInReads.Select((tap, i) => $"stand-in {i + 1} read: <{tap.Recent()}>");
        return $"#3520: {what} at {Elapsed()}. Test token cancelled={timeout.IsCancellationRequested}; "
            + $"thread pool threads={ThreadPool.ThreadCount}, queued={ThreadPool.PendingWorkItemCount}. "
            + $"{string.Join("; ", clients)}. Stand-in drops: [{string.Join(" | ", standInDrops)}]. "
            + $"Client log: [{string.Join(" | ", clientLogs.SelectMany(l => l.Lines))}]. "
            + $"Last bytes each stand-in read: [{string.Join(" || ", reads)}]";
    }

    private async Task<VBLspClient> ConnectAsync(bool withEditor = true)
    {
        var (local, pairEnd) = FullDuplexStream.CreatePair();
        var remote = new ReadTap(pairEnd);
        standInReads.Add(remote);
        var stub = new EditServer();
        var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(remote, remote, new SystemTextJsonFormatter()), stub);
        stub.Rpc = rpc;
        peers.Add(rpc);
        stubs.Add(stub);
        var number = peers.Count;
        rpc.Disconnected += (_, e) => standInDrops.Enqueue($"{Elapsed()} stand-in {number} {e.Reason}: {e.Description}"
            + (e.Exception is null ? "" : $" {e.Exception}"));
        rpc.StartListening();
        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.LeaveServerRunning.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(c => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(local, local, c.Arg<IJsonRpcMessageFormatter>())));
        var log = new RecordingLogger(this, number);
        clientLogs.Add(log);
        var connection = new VBLspClient(transport, log, "vb6",
            applyEdit: !withEditor ? null : (edit, documentFor, token) => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                () => editors.ApplyWorkspaceEdit(edit, documentFor), Avalonia.Threading.DispatcherPriority.Normal, token).GetTask());
        connections.Add(connection);
        var before = Elapsed();
        await connection.StartAsync(timeout.Token);
        if (!connection.IsRunning)
            throw new InvalidOperationException(Diagnosis($"client {number} isn't running after StartAsync (began at {before})"));
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

    private async Task ApplyFromServer(VBLspClient connection, WorkspaceEdit edit, bool applied = true)
    {
        server.DuringCommand = async () => await SendEdit(server, edit);
        await connection.ExecuteCommandAsync("test.edit", cancellationToken: timeout.Token);
        server.EditAnswer.GetProperty("applied").GetBoolean().Should().Be(applied);
        if (applied)
            server.EditAnswer.TryGetProperty("failureReason", out var reason).Should().BeFalse();
        else
            server.EditAnswer.GetProperty("failureReason").GetString().Should().NotBeNullOrWhiteSpace();
    }

    private static TextEdit[] TwoEdits(AvaloniaEdit.Document.TextDocument document)
    {
        return document.Lines.Where(l => document.GetText(l).Contains("Greet", StringComparison.Ordinal))
            .Select(l =>
            {
                var column = document.GetText(l).IndexOf("Greet", StringComparison.Ordinal);
                return new TextEdit(new(new(l.LineNumber - 1, column), new(l.LineNumber - 1, column + 5)), "Welcome");
            }).ToArray();
    }

    private static void AssertUndoRedo(AvaloniaEdit.Document.TextDocument document, string before)
    {
        document.Text.Should().Be(before.Replace("Greet", "Welcome"));
        document.UndoStack.Undo();
        document.Text.Should().Be(before);
        document.UndoStack.CanUndo.Should().BeFalse("the whole server edit is one step");
        document.UndoStack.Redo();
        document.Text.Should().Be(before.Replace("Greet", "Welcome"));
    }

    [AvaloniaFact]
    public async Task ServerEditOfOpenCodeIsModifiedAndOneUndoStepWithRedo()
    {
        var connection = await ConnectAsync();
        var a = Open(first);
        await a.FlushDocumentAsync();
        var before = a.Document.Text;
        a.Document.UndoStack.ClearAll();
        await ApplyFromServer(connection, new(new() { [UriOf(first)] = TwoEdits(a.Document) }));
        a.IsModified.Should().BeTrue();
        AssertUndoRedo(a.Document, before);
        a.IsModified.Should().BeTrue();
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ServerEditOfOpenCarriedFileIsModifiedAndOneUndoStepWithRedo(bool withCode, bool versioned)
    {
        var connection = await ConnectAsync();
        var code = withCode ? Open(first) : null;
        if (code is not null) await code.FlushDocumentAsync();
        var path = Path.Combine(Path.GetTempPath(), $"hexide-4043-{Guid.NewGuid():N}.md");
        File.WriteAllText(path, "Greet\r\nGreet\r\n");
        try
        {
            var definition = new RelatedDocumentDefinition(first.Owner, "Notes.md", path);
            first.Owner.AddRelatedDocument(definition);
            var a = new RelatedDocumentEditorViewModel(client, Substitute.For<IWindowManager>(), localization,
                Substitute.For<ISettingsService>()).Initialize(definition);
            windows.Add(a);
            await a.FlushDocumentAsync();
            var before = a.Document.Text;
            a.Document.UndoStack.ClearAll();
            var targets = new Dictionary<string, TextEdit[]> { [LspDocumentUri.ForFile(path)] = TwoEdits(a.Document) };
            var beforeCode = code?.Document.Text;
            if (code is not null)
            {
                code.Document.UndoStack.ClearAll();
                targets[UriOf(first)] = TwoEdits(code.Document);
            }
            var edit = versioned ? new WorkspaceEdit(DocumentChanges: targets.Select(pair =>
                new TextDocumentEdit(new(pair.Key, 1), pair.Value)).ToArray()) : new WorkspaceEdit(targets);
            await ApplyFromServer(connection, edit);
            if (code is not null) AssertUndoRedo(code.Document, beforeCode!);
            a.IsModified.Should().BeTrue();
            AssertUndoRedo(a.Document, before);
            a.IsModified.Should().BeTrue();
            File.ReadAllText(path).Should().Be(before);
        }
        finally { File.Delete(path); }
    }

    [AvaloniaTheory]
    [InlineData("stale")]
    [InlineData("range")]
    [InlineData("overlap")]
    [InlineData("duplicate")]
    [InlineData("header")]
    [InlineData("closed")]
    public async Task ServerEditValidatesCarriedAndCodeTargetsBeforeChangingEither(string refusal)
    {
        var connection = await ConnectAsync();
        var code = Open(first);
        await code.FlushDocumentAsync();
        var path = Path.Combine(Path.GetTempPath(), $"hexide-4043-{Guid.NewGuid():N}.md");
        File.WriteAllText(path, "Greet\r\nGreet\r\n");
        try
        {
            var definition = new RelatedDocumentDefinition(first.Owner, "Notes.md", path);
            first.Owner.AddRelatedDocument(definition);
            var related = new RelatedDocumentEditorViewModel(client, Substitute.For<IWindowManager>(), localization,
                Substitute.For<ISettingsService>()).Initialize(definition);
            windows.Add(related);
            await related.FlushDocumentAsync();
            if (refusal == "stale") related.Document.Insert(0, "typed");
            if (refusal == "closed")
            {
                windows.Remove(related);
                related.Dispose();
            }
            var codeEdits = refusal == "header" ? new TextEdit[] { new(new(new(0, 0), new(0, 1)), "X") }
                : TwoEdits(code.Document);
            var edits = refusal == "range" ? new TextEdit[] { new(new(new(99, 0), new(99, 0)), "X") }
                : TwoEdits(related.Document);
            if (refusal == "overlap") edits = [edits[0], edits[0]];
            var targets = new List<TextDocumentEdit>
            {
                new(new(UriOf(first), null), codeEdits),
                new(new(LspDocumentUri.ForFile(path), null), edits)
            };
            if (refusal == "duplicate") targets.Add(targets[1]);
            var beforeCode = code.Document.Text;
            var beforeRelated = related.Document.Text;
            code.Document.UndoStack.ClearAll();
            related.Document.UndoStack.ClearAll();
            await ApplyFromServer(connection, new(DocumentChanges: targets.ToArray()), false);
            code.Document.Text.Should().Be(beforeCode);
            related.Document.Text.Should().Be(beforeRelated);
            code.Document.UndoStack.CanUndo.Should().BeFalse();
            related.Document.UndoStack.CanUndo.Should().BeFalse();
            File.ReadAllText(path).Should().Be("Greet\r\nGreet\r\n");
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact]
    public async Task ServerEditOpensClosedCodeUnsavedWithoutWritingDisk()
    {
        var connection = await ConnectAsync();
        Open(first);
        var path = Path.Combine(Path.GetTempPath(), $"hexide-4043-{Guid.NewGuid():N}.bas");
        second.AbsolutePath = path;
        File.WriteAllText(path, "' disk sentinel");
        try
        {
            await ApplyFromServer(connection, new(new() { [UriOf(second)] = [RenameClosed()] }));
            var b = windows.OfType<CodeEditorViewModel>().Single(w => w.ModuleDefinition == second);
            b.Document.Text.Should().Contain("Sub Welcome()");
            b.IsModified.Should().BeTrue();
            File.ReadAllText(path).Should().Be("' disk sentinel");
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact]
    public async Task ServerEditReachingHeaderRefusesTheWholeEditWithWireReason()
    {
        var connection = await ConnectAsync();
        var a = Open(first);
        await a.FlushDocumentAsync();
        var before = a.Document.Text;
        a.Document.UndoStack.ClearAll();
        await ApplyFromServer(connection, new(new() { [UriOf(first)] =
            [..TwoEdits(a.Document), new(new(new(0, 0), new(0, 1)), "X")] }), false);
        a.Document.Text.Should().Be(before);
        a.Document.UndoStack.CanUndo.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task ServerEditOfSeveralDocumentsHasOneIndependentStepEach()
    {
        var connection = await ConnectAsync();
        var a = Open(first);
        var b = Open(second);
        await a.FlushDocumentAsync();
        await b.FlushDocumentAsync();
        var beforeA = a.Document.Text;
        var beforeB = b.Document.Text;
        a.Document.UndoStack.ClearAll();
        b.Document.UndoStack.ClearAll();
        await ApplyFromServer(connection, new(new() { [UriOf(first)] = TwoEdits(a.Document), [UriOf(second)] = TwoEdits(b.Document) }));
        AssertUndoRedo(a.Document, beforeA);
        b.Document.Text.Should().Be(beforeB.Replace("Greet", "Welcome"));
        AssertUndoRedo(b.Document, beforeB);
        a.IsModified.Should().BeTrue();
        b.IsModified.Should().BeTrue();
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ClaimedCommandEditsUseCheckedSnapshotAndCloseWithHandler(bool typeDuringHandler, bool action)
    {
        var connection = await ConnectAsync();
        var a = Open(first);
        await a.FlushDocumentAsync();
        var before = a.Document.Text;
        a.Document.UndoStack.ClearAll();
        var edit = new WorkspaceEdit(new() { [UriOf(first)] = TwoEdits(a.Document) });
        ApplyWorkspaceEditResult? answer = null;
        localization.GetString("Str.Lsp.EditedByCommand").Returns("Edited by {0}: {1}");
        using var reporter = new LanguageServerMessageReporter(connection, status, localization);
        connection.ClaimCommands(new Dictionary<string, HexIDE.Addins.AddinClientCommand>
        {
            ["test.claimed"] = async (_, ct) =>
            {
                if (typeDuringHandler)
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        a.Document.Insert(a.Document.TextLength, "' typed during command\r\n");
                        await a.FlushDocumentAsync();
                    });
                answer = await connection.ApplyClaimedEditAsync(edit, ct);
            },
        });
        if (action)
        {
            server.ActionCommand = "test.claimed";
            var offered = (await connection.RequestCodeActionsAsync(UriOf(first), new(new(1, 0), new(1, 1)), [], timeout.Token)).Single();
            (await a.ApplyCodeActionAsync(offered, timeout.Token)).Should().BeNull();
        }
        else await a.RunCodeLensAsync(new Command("Extract Method", "test.claimed"));
        answer.Should().NotBeNull();
        answer!.Applied.Should().Be(!typeDuringHandler);
        if (typeDuringHandler)
        {
            answer.FailureReason.Should().Contain("changed since the server read it");
            a.Document.Text.Should().Contain("Sub Greet()");
        }
        else
        {
            a.IsModified.Should().BeTrue();
            status.Received().SetTemporaryMessage(action ? "Edited by vb6: Edit" : "Edited by vb6: Extract Method", TimeSpan.FromSeconds(5));
            AssertUndoRedo(a.Document, before);
        }
        var outside = await connection.ApplyClaimedEditAsync(edit, timeout.Token);
        outside.Applied.Should().BeFalse();
        outside.FailureReason.Should().Contain("command the user started");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClaimedCommandFailureShowsOnlyItsMessage(bool lens)
    {
        var connection = await ConnectAsync();
        var a = Open(first);
        connection.ClaimCommands(new Dictionary<string, HexIDE.Addins.AddinClientCommand>
        {
            ["test.claimed"] = (_, _) => throw new InvalidOperationException("add-in message"),
        });
        if (lens)
        {
            await a.RunCodeLensAsync(new Command("Fail", "test.claimed"));
            status.Received().SetTemporaryMessage("add-in message", TimeSpan.FromSeconds(10));
        }
        else
        {
            server.ActionCommand = "test.claimed";
            var action = (await connection.RequestCodeActionsAsync(UriOf(first), new(new(1, 0), new(1, 1)), [], timeout.Token)).Single();
            (await a.ApplyCodeActionAsync(action, timeout.Token)).Should().Be("add-in message");
        }
    }

    private sealed class EditServer
    {
        public JsonRpc Rpc = null!;
        public JsonElement InitializeParams;
        public JsonElement EditAnswer;
        public string ActionCommand = "test.edit";
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
        public JsonElement Actions(JsonElement p) => JsonSerializer.SerializeToElement(new[] { new { title = "Edit", command = ActionCommand } });
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
        // Taken before teardown, which drops every stand-in on purpose. No test here drops one itself, so a drop
        // recorded by now is #3520, whatever the test then failed on.
        var dropped = standInDrops.IsEmpty ? null : Diagnosis("a stand-in server's connection dropped during the test");
        foreach (var window in windows) window.Dispose();
        foreach (var connection in connections) await connection.DisposeAsync();
        foreach (var peer in peers) peer.Dispose();
        timeout.Dispose();
        if (dropped is not null) throw new InvalidOperationException(dropped);
    }
}
