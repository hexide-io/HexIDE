using System.IO.Pipes;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Addins;
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
using HexIDE.Runtime.Serialization;
using HexIDE.Tools;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;
using Location = HexIDE.Lsp.Messages.Location;

namespace HexIDE.Integration.Tests.Views;

public class AddinCodeEditorCommandTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [AvaloniaTheory]
    [InlineData(false, "rdcore.rename", false)]
    [InlineData(true, "rdcore.rename", false)]
    [InlineData(false, "rdcore.showReferences", true)]
    [InlineData(true, "rdcore.showReferences", true)]
    [InlineData(true, "editor.action.rename", false)]
    [InlineData(false, "rdcore.goToLocation", true)]
    public async Task VendorCommandsUseTheirOfferingConnectionAndEditorCommandsStayInCore(bool claimed, string command, bool lens)
    {
        await using var fixture = new Fixture();
        var calls = 0;
        await fixture.StartAsync(claimed, (_, _) => { Interlocked.Increment(ref calls); return Task.CompletedTask; });
        fixture.Peer.Command = fixture.Command(command);
        if (lens)
        {
            var lenses = await fixture.Router.RequestCodeLensesAsync(fixture.Uri, Token);
            await fixture.Editor.RunCodeLensAsync(lenses.Single().Command!);
        }
        else
        {
            var actions = await fixture.ActionsAsync();
            await fixture.Editor.ApplyCodeActionAsync(actions.Single(), Token);
        }
        if (command == "rdcore.goToLocation")
            fixture.Editors.DidNotReceive().OpenCodeForUri(Arg.Any<string>());
        var owned = claimed && command.StartsWith("rdcore.", StringComparison.Ordinal);
        calls.Should().Be(owned ? 1 : 0);
        if (command.EndsWith("rename", StringComparison.Ordinal))
            await fixture.Messages.Received(command == "editor.action.rename" ? 1 : 0).InputBox(Arg.Any<string>(), Arg.Any<string>(), "Greet");
        fixture.Results.Rows.Where(row => row.Location is not null).Should().HaveCount(0);
        fixture.Peer.Executions.Should().Be(!claimed && command is "rdcore.rename" or "rdcore.showReferences" ? 1 : 0);
    }

    [AvaloniaTheory]
    [InlineData("rename")]
    [InlineData("refusal")]
    [InlineData("references")]
    [InlineData("missing")]
    [InlineData("range")]
    public async Task HostCallsRunThroughTheDispatcherInsideAClaimedHandler(string check)
    {
        await using var fixture = new Fixture();
        bool? renamed = null;
        await fixture.StartAsync(true, async (_, token) =>
        {
            Dispatcher.UIThread.CheckAccess().Should().BeFalse();
            if (check == "references")
                await fixture.Connection!.ShowReferencesAsync([fixture.Location], token);
            else
                renamed = await fixture.Connection!.StartRenameAsync(check == "missing" ? "file:///missing.bas" : fixture.Uri,
                    check == "refusal" ? new(0, 0) : check == "range" ? new(-1, 0) : fixture.Range.Start, token);
        });
        fixture.Peer.Command = fixture.Command("rdcore.host");
        var actions = await fixture.ActionsAsync();
        (await fixture.Editor.ApplyCodeActionAsync(actions.Single(), Token)).Should().BeNull();
        if (check == "references") fixture.Results.Rows.Where(row => row.Location is not null).Should().ContainSingle().Which.Location.Should().Be(fixture.Location);
        else
        {
            renamed.Should().Be(check is "rename" or "refusal");
            fixture.Editors.Received(1).OpenCodeForUri(check == "missing" ? "file:///missing.bas" : fixture.Uri);
            if (check == "rename")
            {
                await fixture.Messages.Received(1).InputBox(Arg.Any<string>(), Arg.Any<string>(), "Greet");
                fixture.Editor.CaretOffset.Should().Be(fixture.Editor.Document.GetOffset(fixture.Range.Start.Line + 1, fixture.Range.Start.Character + 1));
            }
            else await fixture.Messages.DidNotReceive().InputBox(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
            if (check == "refusal")
                fixture.Messages.ReceivedCalls().Should().Contain(c => c.GetMethodInfo().Name == "MessageBox"
                    && c.GetArguments().OfType<string>().Any(s => s.Contains("read-only", StringComparison.Ordinal)));
        }
    }

    [AvaloniaFact]
    public async Task HostRenameOpensAnotherDocumentBeforeItsViewExists()
    {
        await using var fixture = new Fixture();
        using var target = new CodeEditorViewModel(fixture.Messages, fixture.Editors, Substitute.For<IProjectService>(),
            new EventBus(), fixture.Router, fixture.Editor.Settings, Substitute.For<IStatusBarService>(),
            new BookmarkService(), new BreakpointService(), Substitute.For<IDebugController>(), new RunScope(),
            new LocalizationService());
        var module = new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "Other"), "Other", ModuleKind.StandardModule);
        module.AbsolutePath = Path.Combine(Path.GetTempPath(), "HexIDE-addin-command", "Other.bas");
        module.UpdateCode("Public Sub OtherName()\r\nEnd Sub\r\n");
        target.Initialize(module);
        var uri = target.GetDocumentUriPublic();
        var offset = target.Document.Text.IndexOf("OtherName", StringComparison.Ordinal);
        var point = target.Document.GetLocation(offset);
        fixture.Editors.OpenCodeForUri(uri).Returns(_ => { Dispatcher.UIThread.VerifyAccess(); return target; });
        await fixture.StartAsync(true, async (_, token) =>
        {
            (await fixture.Connection!.StartRenameAsync(uri, new(point.Line - 1, point.Column - 1), token)).Should().BeTrue();
        });
        fixture.Peer.Command = fixture.Command("rdcore.host");
        var actions = await fixture.ActionsAsync();
        (await fixture.Editor.ApplyCodeActionAsync(actions.Single(), Token)).Should().BeNull();
        fixture.Editors.Received(1).OpenCodeForUri(uri);
        await fixture.Messages.Received(1).InputBox(Arg.Any<string>(), Arg.Any<string>(), "OtherName");
        target.CaretOffset.Should().Be(offset);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostCallsOutsideAHandlerThrowAndShowNothingIncludingAfterCompletion(bool references)
    {
        await using var fixture = new Fixture();
        var ran = false;
        await fixture.StartAsync(true, (_, _) => { ran = true; return Task.CompletedTask; });
        async Task Refused()
        {
            if (references)
                await FluentActions.Awaiting(() => fixture.Connection!.ShowReferencesAsync([fixture.Location], Token))
                    .Should().ThrowAsync<InvalidOperationException>();
            else
                await FluentActions.Awaiting(() => fixture.Connection!.StartRenameAsync(fixture.Uri, fixture.Range.Start, Token))
                    .Should().ThrowAsync<InvalidOperationException>();
        }
        await Refused();
        fixture.Peer.Command = fixture.Command("rdcore.host");
        var actions = await fixture.ActionsAsync();
        (await fixture.Editor.ApplyCodeActionAsync(actions.Single(), Token)).Should().BeNull();
        ran.Should().BeTrue();
        await Refused();
        fixture.Editors.DidNotReceive().OpenCodeForUri(Arg.Any<string>());
        await fixture.Messages.DidNotReceive().InputBox(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        fixture.Results.Rows.Should().BeEmpty();
        fixture.Messages.ReceivedCalls().Should().NotContain(c => c.GetMethodInfo().Name == "MessageBox");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClaimedLensSelectsTheExactRange(bool anotherDocument)
    {
        await using var fixture = new Fixture();
        using var target = new CodeEditorViewModel(fixture.Messages, fixture.Editors, Substitute.For<IProjectService>(),
            new EventBus(), fixture.Router, fixture.Editor.Settings, Substitute.For<IStatusBarService>(),
            new BookmarkService(), new BreakpointService(), Substitute.For<IDebugController>(), new RunScope(),
            new LocalizationService());
        var module = new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "Other"), "Other", ModuleKind.StandardModule);
        module.AbsolutePath = Path.Combine(Path.GetTempPath(), "HexIDE-addin-command", "Other.bas");
        module.UpdateCode("Public Sub OtherName()\r\nEnd Sub\r\n");
        target.Initialize(module);
        var editor = anotherDocument ? target : fixture.Editor;
        var uri = editor.GetDocumentUriPublic();
        var text = anotherDocument ? "OtherName" : "Greet";
        var offset = editor.Document.Text.IndexOf(text, StringComparison.Ordinal);
        var point = editor.Document.GetLocation(offset);
        var range = new HexIDE.Lsp.Messages.Range(new(point.Line - 1, point.Column - 1),
            new(point.Line - 1, point.Column - 1 + text.Length));
        if (!anotherDocument) fixture.Show(editor);
        fixture.Editors.OpenCodeForUri(uri).Returns(_ =>
        {
            Dispatcher.UIThread.VerifyAccess();
            if (anotherDocument) fixture.Show(editor);
            return editor;
        });
        bool? navigated = null;
        await fixture.StartAsync(true, async (_, token) =>
        {
            Dispatcher.UIThread.CheckAccess().Should().BeFalse();
            navigated = await fixture.Connection!.GoToLocationAsync(uri, range, token);
        });
        fixture.Peer.Command = fixture.Command("rdcore.host");
        var lenses = await fixture.Router.RequestCodeLensesAsync(fixture.Uri, Token);
        await fixture.Editor.RunCodeLensAsync(lenses.Single().Command!);
        navigated.Should().BeTrue();
        fixture.Editors.Received(1).OpenCodeForUri(uri);
        editor.SelectionStart.Should().Be(offset);
        editor.SelectionLength.Should().Be(text.Length);
        editor.Document.GetText(editor.SelectionStart, editor.SelectionLength).Should().Be(text);
        fixture.View!.FindControl<AvaloniaEdit.TextEditor>("TextEditor")!.SelectedText.Should().Be(text);
        fixture.Peer.Executions.Should().Be(0);
    }

    [AvaloniaTheory]
    [InlineData("end")]
    [InlineData("start")]
    [InlineData("column")]
    [InlineData("reversed")]
    [InlineData("missing")]
    public async Task ClaimedNavigationRefusesInvalidLocations(string check)
    {
        await using var fixture = new Fixture();
        var range = check switch
        {
            "start" => new HexIDE.Lsp.Messages.Range(new(-1, 0), fixture.Range.End),
            "column" => new(fixture.Range.Start, new(fixture.Range.End.Line, int.MaxValue)),
            "reversed" => new(fixture.Range.End, fixture.Range.Start),
            _ => new(fixture.Range.Start, new(int.MaxValue, 0)),
        };
        var caret = fixture.Editor.CaretOffset;
        bool? navigated = null;
        await fixture.StartAsync(true, async (_, token) =>
            navigated = await fixture.Connection!.GoToLocationAsync(check == "missing" ? "file:///missing.bas" : fixture.Uri,
                range, token));
        fixture.Peer.Command = fixture.Command("rdcore.host");
        var lenses = await fixture.Router.RequestCodeLensesAsync(fixture.Uri, Token);
        await fixture.Editor.RunCodeLensAsync(lenses.Single().Command!);
        navigated.Should().BeFalse();
        fixture.Editor.SelectionLength.Should().Be(0);
        fixture.Editor.CaretOffset.Should().Be(caret);
    }

    [AvaloniaFact]
    public async Task NavigationOutsideAHandlerThrowsBeforeAndAfterCompletion()
    {
        await using var fixture = new Fixture();
        await fixture.StartAsync(true, (_, _) => Task.CompletedTask);
        async Task Refused() => await FluentActions.Awaiting(() =>
            fixture.Connection!.GoToLocationAsync(fixture.Uri, fixture.Range, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        await Refused();
        fixture.Peer.Command = fixture.Command("rdcore.host");
        var lenses = await fixture.Router.RequestCodeLensesAsync(fixture.Uri, Token);
        await fixture.Editor.RunCodeLensAsync(lenses.Single().Command!);
        await Refused();
        fixture.Editors.DidNotReceive().OpenCodeForUri(Arg.Any<string>());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ILoggerFactory logs = LoggerFactory.Create(_ => { });
        private readonly NamedPipeServerStream pipe;
        private readonly Task accepting;
        private JsonRpc? rpc;
        private Window? window;
        public CodeEditorView? View { get; private set; }
        public void Show(CodeEditorViewModel editor)
        {
            View = new CodeEditorView { DataContext = editor };
            window = new Window { Content = View, Width = 800, Height = 600 };
            window.Show();
        }
        public Peer Peer { get; } = new();
        public IEditorService Editors { get; } = Substitute.For<IEditorService>();
        public IWindowManager Messages { get; } = Substitute.For<IWindowManager>();
        public LspClientRegistry Router { get; }
        public CodeEditorViewModel Editor { get; }
        public FindReferencesToolViewModel Results { get; }
        public AddinServerConnection? Connection { get; private set; }
        public string Uri => Editor.GetDocumentUriPublic();
        public HexIDE.Lsp.Messages.Range Range { get; }
        public Location Location => new(Uri, Range);
        private readonly EventBus events = new();
        private readonly IDisposable subscription;
        private readonly string pipeName = "hexide-ui-" + Guid.NewGuid().ToString("N")[..8];

        public Fixture()
        {
            pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            accepting = Task.Run(async () =>
            {
                await pipe.WaitForConnectionAsync(Token);
                rpc = new JsonRpc(new HeaderDelimitedMessageHandler(pipe, pipe, new SystemTextJsonFormatter()), Peer);
                rpc.StartListening();
            });
            Router = new LspClientRegistry([], logs.CreateLogger<LspClientRegistry>());
            var localization = new LocalizationService();
            localization.Apply("en");
            var settings = Substitute.For<ISettingsService>();
            settings.TabWidth.Returns(4);
            Editor = new CodeEditorViewModel(Messages, Editors, Substitute.For<IProjectService>(), events, Router,
                settings, Substitute.For<IStatusBarService>(), new BookmarkService(), new BreakpointService(),
                Substitute.For<IDebugController>(), new RunScope(), localization);
            var module = new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "Module1", ModuleKind.StandardModule);
            module.AbsolutePath = Path.Combine(Path.GetTempPath(), "HexIDE-addin-command", "Module1.bas");
            module.UpdateCode("Public Sub Greet()\r\nEnd Sub\r\n");
            Editor.Initialize(module);
            var point = Editor.Document.GetLocation(Editor.Document.Text.IndexOf("Greet", StringComparison.Ordinal));
            Range = new(new(point.Line - 1, point.Column - 1), new(point.Line - 1, point.Column + 4));
            Editors.OpenCodeForUri(Uri).Returns(_ => { Dispatcher.UIThread.VerifyAccess(); return Editor; });
            Messages.InputBox(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(Task.FromResult<string?>(null));
            var projects = Substitute.For<IProjectManager>();
            projects.LoadedProjects.Returns([]);
            var documents = Substitute.For<IDocumentDockService>();
            documents.OpenDocuments.Returns([]);
            Results = new FindReferencesToolViewModel(Router, Editors, projects, documents, localization);
            subscription = events.Subscribe<ShowReferenceLocationsEvent>(e =>
            {
                Dispatcher.UIThread.VerifyAccess();
                Results.ShowLocations(e.Locations);
            });
        }

        public Command Command(string name) => new("Run", name, [JsonSerializer.SerializeToElement(Uri),
            JsonSerializer.SerializeToElement(Range.Start), JsonSerializer.SerializeToElement(new[] { Location })]);

        public Task<OfferedCodeAction[]> ActionsAsync() => Router.RequestCodeActionsAsync(Uri, Range,
            Router.Diagnostics.Select(d => d.Diagnostic).ToArray(), Token);

        public async Task StartAsync(bool claimed, AddinClientCommand handler)
        {
            Peer.DeclaredCommands = claimed ? [] : ["rdcore.rename", "rdcore.showReferences"];
            var contributions = new LanguageServerContributions(Substitute.For<ISettingsService>(), Router, new LanguageServerRegistrationFactory(logs),
                new LanguageServerConfigLoader(logs.CreateLogger<LanguageServerConfigLoader>()),
                new HashSet<string>(StringComparer.Ordinal), logs.CreateLogger<LanguageServerContributions>(),
                new CodeEditorClientActions(() => Editors, events));
            Connection = contributions.Register(new AddinLanguageServer(new LanguageServerEntry
            {
                Id = "probe", DisplayName = "Probe", Extensions = [".bas"], LanguageId = "vb6", Transport = "pipe", PipeName = pipeName,
            })
            {
                CommandPrefix = "rdcore.",
                ClientCommands = claimed ? new Dictionary<string, AddinClientCommand>
                {
                    ["rdcore.rename"] = handler, ["rdcore.showReferences"] = handler, ["rdcore.host"] = handler,
                } : new Dictionary<string, AddinClientCommand>(),
            }, "Probe Add-in");
            await Router.OpenDocumentAsync(Uri, Editor.Document.Text, Token);
            await accepting;
            var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnDiagnostics(object? sender, PublishDiagnosticsParams message) => published.TrySetResult();
            Router.DiagnosticsPublished += OnDiagnostics;
            try
            {
                await rpc!.NotifyWithParameterObjectAsync("textDocument/publishDiagnostics",
                    new PublishDiagnosticsParams(Uri, [new(Range, "Probe diagnostic")]));
                await published.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            }
            finally { Router.DiagnosticsPublished -= OnDiagnostics; }
        }

        public async ValueTask DisposeAsync()
        {
            window?.Close();
            if (Connection is not null) await Connection.DisposeAsync();
            await Router.DisposeAsync();
            Editor.Dispose();
            subscription.Dispose();
            rpc?.Dispose();
            await pipe.DisposeAsync();
            logs.Dispose();
        }
    }

    public sealed class Peer
    {
        public Command? Command { get; set; }
        public string[] DeclaredCommands { get; set; } = [];
        public int Executions { get; private set; }
        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) => JsonSerializer.SerializeToElement(new
        {
            capabilities = new
            {
                textDocumentSync = 1, codeActionProvider = true, codeLensProvider = new { },
                executeCommandProvider = new { commands = DeclaredCommands },
            }
        });
        [JsonRpcMethod("initialized")] public void Initialized(JsonElement _) { }
        [JsonRpcMethod("textDocument/didOpen")] public void Open(JsonElement _) { }
        [JsonRpcMethod("textDocument/didChange")] public void Change(JsonElement _) { }
        [JsonRpcMethod("textDocument/didClose")] public void Close(JsonElement _) { }
        [JsonRpcMethod("textDocument/codeAction", UseSingleObjectParameterDeserialization = true)]
        public Command[] Actions(JsonElement _) => [Command!];
        [JsonRpcMethod("textDocument/codeLens", UseSingleObjectParameterDeserialization = true)]
        public CodeLens[] Lenses(JsonElement _) => [new(new(new(0, 0), new(0, 0)), Command)];
        [JsonRpcMethod("workspace/executeCommand", UseSingleObjectParameterDeserialization = true)]
        public object? Execute(JsonElement _) { Executions++; return null; }
        [JsonRpcMethod("shutdown")] public object? Shutdown() => null;
        [JsonRpcMethod("exit")] public void Exit() { }
    }
}
