using HexIDE.Runtime.Serialization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AvaloniaEdit;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Views;

public class QuickFixTests : IDisposable
{
    private readonly ILspClient client = Substitute.For<ILspClient>();
    private readonly IEditorService editors = Substitute.For<IEditorService>();
    private readonly IWindowManager messages = Substitute.For<IWindowManager>();
    private readonly CodeEditorViewModel vm;
    private readonly CodeEditorView view;
    private readonly Window window;
    private readonly TextEditor editor;
    private readonly MenuItem menu;
    private readonly Diagnostic diagnostic;
    private readonly string uri;
    private readonly string before;
    private readonly TextEdit edit;
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;

    public QuickFixTests()
    {
        client.IsRunning.Returns(true);
        var localization = new LocalizationService();
        localization.Apply("en");
        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);
        vm = new CodeEditorViewModel(messages, editors, Substitute.For<IProjectService>(),
            Substitute.For<IEventBus>(), client, settings, Substitute.For<IStatusBarService>(), new BookmarkService(),
            new BreakpointService(), Substitute.For<IDebugController>(), new RunScope(), localization);
        var module = new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "Module1", ModuleKind.StandardModule);
        module.AbsolutePath = OperatingSystem.IsWindows() ? @"C:\HexIDE-quick-fix\Module1.bas"
            : Path.Combine(Path.GetTempPath(), "HexIDE-quick-fix", "Module1.bas");
        module.UpdateCode("Option Explicit\r\nPublic Sub Greet()\r\n    Greet\r\nEnd Sub\r\n");
        vm.Initialize(module);
        view = new CodeEditorView { DataContext = vm };
        window = new Window { Content = view, Width = 1000, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        editor = view.FindControl<TextEditor>("TextEditor")!;
        menu = view.FindControl<MenuItem>("QuickFixesMenu")!;
        uri = vm.GetDocumentUriPublic();
        var offset = vm.Document.Text.IndexOf("Greet", StringComparison.Ordinal);
        editor.CaretOffset = offset;
        var location = vm.Document.GetLocation(offset);
        var range = new HexIDE.Lsp.Messages.Range(new(location.Line - 1, location.Column - 1), new(location.Line - 1, location.Column + 4));
        diagnostic = new Diagnostic(range, "problem");
        edit = new TextEdit(range, "Welcome");
        client.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(client, new PublishDiagnosticsParams(uri, [diagnostic]));
        Dispatcher.UIThread.RunJobs();
        before = vm.Document.Text;
    }

    private OfferedCodeAction Action(WorkspaceEdit? workspaceEdit = null, Command? command = null,
        string? disabled = null, Func<CancellationToken, Task<OfferedCodeAction>>? resolve = null,
        Func<Command, CancellationToken, Task>? execute = null) =>
        new("server", "Fix it", disabled, workspaceEdit, command, resolve, execute,
            CanExecute: c => c.CommandName is "fix" or "server.fix");
    private WorkspaceEdit Edit() => new(new() { [uri] = [edit, new(new(new(edit.Range.Start.Line + 1, 4), new(edit.Range.Start.Line + 1, 9)), "Welcome")] });
    private void Answers(params OfferedCodeAction[] actions) =>
        client.RequestCodeActionsAsync(uri, Arg.Any<HexIDE.Lsp.Messages.Range>(), Arg.Any<Diagnostic[]>(), Arg.Any<CancellationToken>()).Returns(actions);

    [AvaloniaFact]
    public async Task OpeningTheMenuRequestsOnceAndGroupsAnswersByServer()
    {
        Answers(Action() with { ServerDisplayName = "Same display name" },
            Action(disabled: "Save first") with { Server = "other", ServerDisplayName = "Same display name" });
        editor.ContextFlyout!.ShowAt(editor);
        Dispatcher.UIThread.RunJobs();
        menu.Header.Should().Be("Quick Fi_xes");
        menu.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent));
        for (var i = 0; i < 10 && menu.Items.Count != 2; i++) await Task.Yield();
        await client.Received(1).RequestCodeActionsAsync(uri, diagnostic.Range,
            Arg.Is<Diagnostic[]>(ds => ds.Length == 1 && ds[0] == diagnostic), Arg.Any<CancellationToken>());
        menu.Items.Cast<MenuItem>().Select(item => item.Header).Should().Equal("Same display name", "Same display name");
        ((MenuItem)((MenuItem)menu.Items[1]!).Items[0]!).Header.Should().Be("Fix it — Save first");
        ((MenuItem)((MenuItem)menu.Items[1]!).Items[0]!).IsEnabled.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task PickingAnEditIsOneUndoStep()
    {
        Answers(Action(Edit()));
        await view.LoadQuickFixesAsync();
        var item = (MenuItem)((MenuItem)menu.Items[0]!).Items[0]!;
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        vm.Document.Text.Should().Contain("Sub Welcome()").And.Contain("    Welcome");
        vm.Document.UndoStack.Undo();
        vm.Document.Text.Should().Be(before);
    }

    [AvaloniaFact]
    public async Task NothingIsRequestedUntilAskedAndOffRangeAsksNothing()
    {
        await client.DidNotReceive().RequestCodeActionsAsync(Arg.Any<string>(), Arg.Any<HexIDE.Lsp.Messages.Range>(), Arg.Any<Diagnostic[]>(), Arg.Any<CancellationToken>());
        editor.CaretOffset = vm.Document.TextLength;
        await view.LoadQuickFixesAsync();
        await client.DidNotReceive().RequestCodeActionsAsync(Arg.Any<string>(), Arg.Any<HexIDE.Lsp.Messages.Range>(), Arg.Any<Diagnostic[]>(), Arg.Any<CancellationToken>());
        menu.Items.Cast<MenuItem>().Should().ContainSingle().Which.Header.Should().Be("_No applicable fix was supplied.");
    }

    [AvaloniaFact]
    public async Task EditPrecedesCommandAndLazyResolvePrecedesBoth()
    {
        var order = new List<string>();
        var resolved = Action(Edit(), new("Run", "fix"), execute: (_, _) =>
        {
            vm.Document.Text.Should().Contain("Sub Welcome()");
            order.Add("command"); return Task.CompletedTask;
        });
        var lazy = Action(resolve: _ => { vm.Document.Text.Should().Be(before); order.Add("resolve"); return Task.FromResult(resolved); });
        (await vm.ApplyCodeActionAsync(lazy, ct)).Should().BeNull();
        order.Should().Equal("resolve", "command");
    }

    [AvaloniaFact]
    public async Task DisabledActionsReturnTheirReasonWithoutResolvingOrApplying()
    {
        var resolved = false;
        (await vm.ApplyCodeActionAsync(Action(Edit(), disabled: "Save first", resolve: _ =>
            { resolved = true; return Task.FromResult(Action()); }), ct)).Should().Be("Save first");
        resolved.Should().BeFalse();
        vm.Document.Text.Should().Be(before);
    }

    [AvaloniaFact]
    public async Task AnActionWithNoEditOrCommandExplainsWhyItDidNothing()
    {
        (await vm.ApplyCodeActionAsync(Action(), ct)).Should().Be("No applicable fix was supplied.");
    }

    [AvaloniaFact]
    public async Task ProtectedEditsRefuseTheWholeActionAndNeverRunItsCommand()
    {
        var ran = false;
        var workspaceEdit = new WorkspaceEdit(new() { [uri] = [edit, new(new(new(0, 0), new(0, 1)), "X")] });
        (await vm.ApplyCodeActionAsync(Action(workspaceEdit, new("Run", "fix"), execute: (_, _) =>
            { ran = true; return Task.CompletedTask; }), ct)).Should().StartWith("Cannot apply this fix to ").And.EndWith(": the target or edit ranges are unavailable or protected.");
        vm.Document.Text.Should().Be(before);
        ran.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task AnActionFromAnOlderSnapshotRefusesWithoutChangingTheBuffer()
    {
        vm.Document.Insert(vm.Document.TextLength, "' changed");
        var changed = vm.Document.Text;
        var action = Action(Edit()) with { DocumentFor = _ => new RenameDocumentSnapshot(1, before) };
        (await vm.ApplyCodeActionAsync(action, ct)).Should().StartWith("Cannot apply this fix to ").And.EndWith(": its content changed or has unsaved changes. Save and request fixes again.");
        vm.Document.Text.Should().Be(changed);
    }

    [AvaloniaFact]
    public async Task UnlistedCommandExplainsWhyWithoutSendingIt()
    {
        var ran = false;
        var action = Action(command: new("Run", "unknown.fix"), execute: (_, _) =>
            { ran = true; return Task.CompletedTask; });
        (await vm.ApplyCodeActionAsync(action, ct)).Should().Be("HexIDE cannot run this fix's command: unknown.fix.");
        ran.Should().BeFalse();
    }

    [AvaloniaTheory]
    [InlineData("editor.action.rename")]
    public async Task RenameClientCommandOpensTheExistingRenamePromptAtItsPosition(string command)
    {
        editor.CaretOffset = vm.Document.TextLength;
        var position = edit.Range.Start;
        var alias = OperatingSystem.IsWindows() ? uri.Replace("/C:/", "/c:/", StringComparison.Ordinal)
            : uri.Replace("Module1", "%4Dodule1", StringComparison.Ordinal);
        var args = new[] { System.Text.Json.JsonSerializer.SerializeToElement(alias), System.Text.Json.JsonSerializer.SerializeToElement(new { line = position.Line, character = position.Character }) };
        messages.InputBox(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(Task.FromResult<string?>(null));
        (await vm.ApplyCodeActionAsync(Action(command: new("Rename", command, args)), ct)).Should().BeNull();
        await messages.Received(1).InputBox(Arg.Any<string>(), Arg.Any<string>(), "Greet");
        editor.CaretOffset.Should().Be(vm.Document.GetOffset(position.Line + 1, position.Character + 1));
    }

    [AvaloniaFact]
    public async Task UnclaimedRdcoreRenameDoesNotOpenTheRenamePrompt()
    {
        var offset = editor.CaretOffset;
        var args = new[] { JsonSerializer.SerializeToElement(uri), JsonSerializer.SerializeToElement(edit.Range.Start) };
        (await vm.ApplyCodeActionAsync(Action(command: new("Rename", "rdcore.rename", args)), ct))
            .Should().Be("HexIDE cannot run this fix's command: rdcore.rename.");
        await messages.DidNotReceive().InputBox(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        editor.CaretOffset.Should().Be(offset);
    }

    [AvaloniaFact]
    public async Task RenameCodeActionPreparesBeforeOpeningThePrompt()
    {
        var position = edit.Range.Start;
        var order = new List<string>();
        client.RequestPrepareRenameAsync(uri, position, Arg.Any<CancellationToken>())
            .Returns(_ => { order.Add("prepare"); return Task.FromResult<PrepareRenameResult?>(new(true, Placeholder: "ServerName")); });
        messages.InputBox(Arg.Any<string>(), Arg.Any<string>(), "ServerName")
            .Returns(_ => { order.Add("prompt"); return Task.FromResult<string?>(null); });
        var args = new[] { JsonSerializer.SerializeToElement(uri), JsonSerializer.SerializeToElement(position) };
        (await vm.ApplyCodeActionAsync(Action(command: new("Rename", "editor.action.rename", args)), ct)).Should().BeNull();
        order.Should().Equal("prepare", "prompt");
    }

    [AvaloniaFact]
    public async Task RenameInAnotherModuleWorksBeforeItsViewIsAttached()
    {
        using var target = new CodeEditorViewModel(messages, editors, Substitute.For<IProjectService>(),
            Substitute.For<IEventBus>(), client, vm.Settings, Substitute.For<IStatusBarService>(), new BookmarkService(),
            new BreakpointService(), Substitute.For<IDebugController>(), new RunScope(), new LocalizationService());
        var module = new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "Other"), "Other", ModuleKind.StandardModule);
        module.UpdateCode("Public Sub OtherName()\r\nEnd Sub\r\n");
        target.Initialize(module);
        var offset = target.Document.Text.IndexOf("OtherName", StringComparison.Ordinal);
        var location = target.Document.GetLocation(offset);
        const string otherUri = "file:///other.bas";
        editors.OpenCodeForUri(otherUri).Returns(target);
        var args = new[] { JsonSerializer.SerializeToElement(otherUri), JsonSerializer.SerializeToElement(new { line = location.Line - 1, character = location.Column - 1 }) };
        messages.InputBox(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(Task.FromResult<string?>(null));
        (await vm.ApplyCodeActionAsync(Action(command: new("Rename", "editor.action.rename", args)), ct)).Should().BeNull();
        await messages.Received(1).InputBox(Arg.Any<string>(), Arg.Any<string>(), "OtherName");
        target.CaretOffset.Should().Be(offset);
    }

    [AvaloniaFact]
    public async Task PendingEditReachesTheServerBeforeItsCommand()
    {
        var seen = false;
        client.ChangeDocumentAsync(uri, Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => { seen = call.ArgAt<string>(2).Contains("Sub Welcome()"); return Task.CompletedTask; });
        var action = Action(Edit(), new("Run", "server.fix"), execute: (_, _) =>
        {
            seen.Should().BeTrue();
            return Task.CompletedTask;
        });
        (await vm.ApplyCodeActionAsync(action, ct)).Should().BeNull();
    }

    [AvaloniaFact]
    public async Task EditThenCommandArriveOnTheWireInThatOrder()
    {
        var peer = new OrderingPeer();
        var (local, remote) = FullDuplexStream.CreatePair();
        using var server = new JsonRpc(new LspMessageHandler(remote, remote, new SystemTextJsonFormatter()), peer);
        server.StartListening();
        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IJsonRpcMessageHandler?>(new LspMessageHandler(local, local, call.Arg<IJsonRpcMessageFormatter>())));
        await using var wire = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), "vb6");
        await wire.StartAsync(ct);
        await wire.OpenDocumentAsync(uri, before, ct);
        client.ChangeDocumentAsync(uri, Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => wire.ChangeDocumentAsync(uri, call.ArgAt<int>(1), call.ArgAt<string>(2), ct));
        var action = Action(Edit(), new("Run", "server.fix"), execute: async (command, token) =>
            { await wire.ExecuteCommandAsync(command.CommandName, command.Arguments, token); });
        (await vm.ApplyCodeActionAsync(action, ct)).Should().BeNull();
        peer.Order.Should().Equal("didChange", "executeCommand");
        peer.ServerHadTheEdit.Should().BeTrue();
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RefactorWithoutDiagnosticsRequestsTheSelectionOrCaretAndResolvesOneUndoStep(bool selection)
    {
        var peer = new RefactorPeer { Edit = Edit() };
        var (local, remote) = FullDuplexStream.CreatePair();
        using var server = new JsonRpc(new LspMessageHandler(remote, remote, new SystemTextJsonFormatter()), peer);
        server.StartListening();
        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IJsonRpcMessageHandler?>(new LspMessageHandler(local, local, call.Arg<IJsonRpcMessageFormatter>())));
        await using var wire = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), "vb6");
        await using var registry = new LspClientRegistry([
            new LanguageServerRegistration("scripted", "Scripted server", [".bas"], "vb6", () => wire)],
            Substitute.For<ILogger<LspClientRegistry>>());
        await registry.OpenDocumentAsync(uri, before, ct);
        client.RequestCodeActionsAsync(uri, Arg.Any<HexIDE.Lsp.Messages.Range>(), Arg.Any<Diagnostic[]>(), Arg.Any<string[]?>(), Arg.Any<CancellationToken>())
            .Returns(call => registry.RequestCodeActionsAsync(uri, call.Arg<HexIDE.Lsp.Messages.Range>(), call.Arg<Diagnostic[]>(), call.Arg<string[]?>(), ct));
        client.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(client, new PublishDiagnosticsParams(uri, []));
        Dispatcher.UIThread.RunJobs();
        editor.Select(editor.CaretOffset, selection ? 5 : 0);
        var start = vm.Document.GetLocation(editor.SelectionStart);
        var end = vm.Document.GetLocation(editor.SelectionStart + editor.SelectionLength);
        var refactor = view.FindControl<MenuItem>("RefactorMenu")!;
        editor.ContextFlyout!.ShowAt(editor);
        refactor.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent));
        for (var i = 0; i < 200 && peer.Request is null; i++) await Task.Delay(10, ct);
        for (var i = 0; i < 200 && refactor.Items[0] is MenuItem { IsEnabled: false }; i++) await Task.Delay(10, ct);
        peer.Request.Should().NotBeNull();
        var request = peer.Request!.Value;
        request.GetProperty("range").Deserialize<HexIDE.Lsp.Messages.Range>().Should().Be(
            new HexIDE.Lsp.Messages.Range(new(start.Line - 1, start.Column - 1), new(end.Line - 1, end.Column - 1)));
        request.GetProperty("context").GetProperty("diagnostics").GetArrayLength().Should().Be(0);
        request.GetProperty("context").GetProperty("only").EnumerateArray().Select(k => k.GetString()).Should().Equal("refactor");
        var serverMenu = (MenuItem)refactor.Items[0]!;
        serverMenu.Header.Should().Be("Scripted server");
        var item = (MenuItem)serverMenu.Items[0]!;
        item.Header.Should().Be("Extract method");
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        for (var i = 0; i < 200 && vm.Document.Text == before; i++) await Task.Delay(10, ct);
        peer.Resolved!.Value.GetProperty("data").GetProperty("id").GetInt32().Should().Be(42);
        vm.Document.Text.Should().Contain("Sub Welcome()").And.Contain("    Welcome");
        vm.Document.UndoStack.Undo();
        vm.Document.Text.Should().Be(before);

        await registry.InjectDiagnosticsAsync(uri, [diagnostic], "scripted");
        client.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(client, new PublishDiagnosticsParams(uri, [diagnostic]));
        Dispatcher.UIThread.RunJobs();
        editor.CaretOffset = vm.Document.GetOffset(diagnostic.Range.Start.Line + 1, diagnostic.Range.Start.Character + 1);
        client.RequestCodeActionsAsync(uri, Arg.Any<HexIDE.Lsp.Messages.Range>(), Arg.Any<Diagnostic[]>(), Arg.Any<CancellationToken>())
            .Returns(call => registry.RequestCodeActionsAsync(uri, call.Arg<HexIDE.Lsp.Messages.Range>(), call.Arg<Diagnostic[]>(), ct));
        peer.Request = null;
        menu.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent));
        for (var i = 0; i < 200 && peer.Request is null; i++) await Task.Delay(10, ct);
        peer.Request!.Value.GetProperty("range").Deserialize<HexIDE.Lsp.Messages.Range>().Should().Be(diagnostic.Range);
        peer.Request.Value.GetProperty("context").GetProperty("diagnostics").GetArrayLength().Should().Be(1);
    }

    [AvaloniaFact]
    public async Task SourceActionsRequestOnlySourceAndRefactorFiltersKindsAndDisabledActions()
    {
        var actions = new[]
        {
            Action() with { Kind = "refactor.rewrite", Title = "Rewrite" },
            Action(disabled: "Save first") with { Kind = "source.sortMembers", Title = "Sort" },
            Action() with { Kind = "quickfix", Title = "Fix" },
            Action() with { Title = "Unclassified" },
            Action() with { Title = "Diagnostic fix", IsQuickFix = true }
        };
        client.RequestCodeActionsAsync(uri, Arg.Any<HexIDE.Lsp.Messages.Range>(), Arg.Any<Diagnostic[]>(), Arg.Any<string[]?>(), Arg.Any<CancellationToken>()).Returns(actions);
        var source = view.FindControl<MenuItem>("SourceActionsMenu")!;
        source.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent));
        await client.Received(1).RequestCodeActionsAsync(uri, Arg.Any<HexIDE.Lsp.Messages.Range>(), Arg.Any<Diagnostic[]>(),
            Arg.Is<string[]?>(only => only != null && only.Length == 1 && only[0] == "source"), Arg.Any<CancellationToken>());
        var sourceItems = ((MenuItem)source.Items[0]!).Items.Cast<MenuItem>().ToArray();
        sourceItems.Select(m => m.Header).Should().Equal("Sort — Save first", "Unclassified");
        var item = sourceItems[0];
        item.IsEnabled.Should().BeFalse();
        var refactor = view.FindControl<MenuItem>("RefactorMenu")!;
        refactor.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent));
        // Server, then its actions as sent: refactor kinds and unclassified non-fixes, no quick fixes, no kind headers.
        ((MenuItem)refactor.Items.Single()!).Items.Cast<MenuItem>().Select(m => m.Header).Should().Equal("Rewrite", "Unclassified");
    }

    [AvaloniaFact]
    public async Task EachMenuShowsTheUnkindedActionsTheServerFilteredForIt()
    {
        // RDCore's shape: every action carries the first kind the client declares (""), but it honours `only`.
        client.RequestCodeActionsAsync(uri, Arg.Any<HexIDE.Lsp.Messages.Range>(), Arg.Any<Diagnostic[]>(),
                Arg.Is<string[]?>(only => only != null && only.SequenceEqual(new[] { "source" })), Arg.Any<CancellationToken>())
            .Returns([Action() with { Kind = "", Title = "Sort procedures by kind, then by name" }]);
        client.RequestCodeActionsAsync(uri, Arg.Any<HexIDE.Lsp.Messages.Range>(), Arg.Any<Diagnostic[]>(),
                Arg.Is<string[]?>(only => only != null && only.SequenceEqual(new[] { "refactor" })), Arg.Any<CancellationToken>())
            .Returns([Action() with { Kind = "", Title = "Move 'Zeta' down" }]);
        var source = view.FindControl<MenuItem>("SourceActionsMenu")!;
        source.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent));
        for (var i = 0; i < 200 && source.Items[0] is MenuItem { IsEnabled: false }; i++) await Task.Delay(10, ct);
        ((MenuItem)source.Items.Single()!).Items.Cast<MenuItem>().Select(m => m.Header)
            .Should().Equal("Sort procedures by kind, then by name");
        var refactor = view.FindControl<MenuItem>("RefactorMenu")!;
        refactor.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent));
        for (var i = 0; i < 200 && refactor.Items[0] is MenuItem { IsEnabled: false }; i++) await Task.Delay(10, ct);
        ((MenuItem)refactor.Items.Single()!).Items.Cast<MenuItem>().Select(m => m.Header)
            .Should().Equal("Move 'Zeta' down");
    }

    [AvaloniaTheory]
    [InlineData(-2, 3, true)]
    [InlineData(0, 0, true)]
    [InlineData(5, 5, false)]
    public async Task SelectionIncludesOnlyOverlappingDiagnostics(int startDelta, int endDelta, bool included)
    {
        var start = diagnostic.Range.Start;
        var overlap = new Diagnostic(new(new(start.Line, start.Character + startDelta),
            new(start.Line, start.Character + endDelta)), "overlap");
        client.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(client, new PublishDiagnosticsParams(uri, [overlap]));
        Dispatcher.UIThread.RunJobs();
        editor.Select(editor.CaretOffset, 5);
        var source = view.FindControl<MenuItem>("SourceActionsMenu")!;
        source.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent));
        await client.Received(1).RequestCodeActionsAsync(uri, Arg.Any<HexIDE.Lsp.Messages.Range>(),
            Arg.Is<Diagnostic[]>(ds => ds.Length == (included ? 1 : 0)), Arg.Any<string[]?>(), Arg.Any<CancellationToken>());
    }

    private sealed class RefactorPeer
    {
        public WorkspaceEdit? Edit;
        public JsonElement? Request, Resolved;
        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) => JsonSerializer.SerializeToElement(new
        {
            capabilities = new { textDocumentSync = 1, codeActionProvider = new { resolveProvider = true } }
        });
        [JsonRpcMethod("initialized")] public void Initialized(JsonElement _) { }
        [JsonRpcMethod("textDocument/didOpen")] public void Open(JsonElement _) { }
        [JsonRpcMethod("textDocument/didChange")] public void Change(JsonElement _) { }
        [JsonRpcMethod("textDocument/codeAction", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Actions(JsonElement p)
        {
            Request = p.Clone();
            return JsonSerializer.SerializeToElement(new[] { new { title = "Extract method", kind = "refactor.extract.method", data = new { id = 42 } } });
        }
        [JsonRpcMethod("codeAction/resolve", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Resolve(JsonElement p)
        {
            Resolved = p.Clone();
            return JsonSerializer.SerializeToElement(new { title = "Extract method", edit = Edit });
        }
        [JsonRpcMethod("shutdown")] public object? Shutdown() => null;
        [JsonRpcMethod("exit")] public void Exit() { }
    }

    private sealed class OrderingPeer
    {
        public List<string> Order { get; } = [];
        private string text = "";
        public bool ServerHadTheEdit;
        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) => JsonSerializer.SerializeToElement(new
        {
            capabilities = new { textDocumentSync = 1, executeCommandProvider = new { commands = new[] { "server.fix" } } }
        });
        [JsonRpcMethod("initialized")] public void Initialized(JsonElement _) { }
        [JsonRpcMethod("textDocument/didOpen", UseSingleObjectParameterDeserialization = true)]
        public void Open(JsonElement p) => text = p.GetProperty("textDocument").GetProperty("text").GetString()!;
        [JsonRpcMethod("textDocument/didChange", UseSingleObjectParameterDeserialization = true)]
        public void Change(JsonElement p)
        {
            text = p.GetProperty("contentChanges")[0].GetProperty("text").GetString()!;
            Order.Add("didChange");
        }
        [JsonRpcMethod("workspace/executeCommand", UseSingleObjectParameterDeserialization = true)]
        public object? Execute(JsonElement _)
        {
            ServerHadTheEdit = text.Contains("Sub Welcome()");
            Order.Add("executeCommand");
            return null;
        }
        [JsonRpcMethod("shutdown")] public object? Shutdown() => null;
        [JsonRpcMethod("exit")] public void Exit() { }
    }

    public void Dispose() { editor.ContextFlyout?.Hide(); window.Close(); vm.Dispose(); GC.SuppressFinalize(this); }
}
