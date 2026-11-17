using System.Collections.Concurrent;
using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

public partial class PullDiagnosticsReportKindTests
{
    // Provider and closed-report shapes measured in https://github.com/hexide-io/HexIDE/issues/4263.
    private sealed class WorkspaceServer(bool enabled = true)
    {
        public readonly ConcurrentQueue<JsonElement> Requests = new();
        public bool Initialized;
        public bool AskedBeforeInitialized;

        [JsonRpcMethod("initialized", UseSingleObjectParameterDeserialization = true)]
        public void OnInitialized(JsonElement _) => Initialized = true;
        public TaskCompletionSource<JsonElement>? Held;
        public TaskCompletionSource<JsonElement>? HeldDocument;
        public string Answer = """
            {"items":[{"uri":"file:///project/Closed.bas","version":null,"kind":"full","resultId":"closed-1","items":[{"range":{"start":{"line":0,"character":0},"end":{"line":0,"character":1}},"message":"closed finding"}]}]}
            """;

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) => JsonDocument.Parse(
            "{\"capabilities\":{\"textDocumentSync\":{\"openClose\":true,\"change\":1},\"diagnosticProvider\":{\"identifier\":\"rdcore\",\"interFileDependencies\":true,\"workspaceDiagnostics\":"
            + (enabled ? "true" : "false") + "}}}").RootElement.Clone();

        [JsonRpcMethod("textDocument/diagnostic", UseSingleObjectParameterDeserialization = true)]
        public Task<JsonElement> Document(JsonElement _) => HeldDocument?.Task ?? Task.FromResult(JsonDocument.Parse("""
            {"kind":"full","resultId":"open-1","items":[{"range":{"start":{"line":0,"character":0},"end":{"line":0,"character":1}},"message":"open finding"}]}
            """).RootElement.Clone());

        [JsonRpcMethod("workspace/diagnostic", UseSingleObjectParameterDeserialization = true)]
        public Task<JsonElement> Workspace(JsonElement p)
        {
            if (!Initialized) AskedBeforeInitialized = true;
            Requests.Enqueue(p.Clone());
            return Held?.Task ?? Task.FromResult(JsonDocument.Parse(Answer).RootElement.Clone());
        }
    }

    [Fact]
    [Trait("Issue", "4263")]
    public async Task WorkspacePullStartsAfterInitializationAndRefreshPreservesResultIdsAndUnchangedItems()
    {
        var server = new WorkspaceServer();
        var sut = ClientTalkingTo(server);
        var reports = new ConcurrentQueue<PublishDiagnosticsParams>();
        sut.DiagnosticsPublished += (_, p) => reports.Enqueue(p);
        await sut.StartAsync(TestContext.Current.CancellationToken);
        await Until(() => !reports.IsEmpty, "workspace findings must be published without opening a document");
        reports.Last().Diagnostics.Single().Message.Should().Be("closed finding");
        server.AskedBeforeInitialized.Should().BeFalse();
        server.Requests.Single().GetProperty("identifier").GetString().Should().Be("rdcore");
        server.Requests.Single().GetProperty("previousResultIds").GetArrayLength().Should().Be(0);
        server.Requests.Single().TryGetProperty("partialResultToken", out _).Should().BeFalse();
        server.Answer = """{"items":[{"uri":"file:///project/Closed.bas","version":null,"kind":"unchanged","resultId":"closed-1"}]}""";
        await RefreshWorkspace();
        await Until(() => server.Requests.Count == 2, "refresh must request workspace diagnostics");
        server.Requests.Last().GetProperty("previousResultIds")[0].GetProperty("value").GetString().Should().Be("closed-1");
        await Task.Delay(100, TestContext.Current.CancellationToken);
        reports.Should().ContainSingle();
        server.Answer = """{"items":[]}""";
        await RefreshWorkspace();
        await Until(() => reports.Count == 2, "a URI omitted from the next answer must clear");
        reports.Last().Diagnostics.Should().BeEmpty();
    }

    [Theory]
    [Trait("Issue", "4263")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WorkspaceCapabilityControlsRequestsAndCloseRetention(bool enabled)
    {
        var server = new WorkspaceServer(enabled);
        var sut = ClientTalkingTo(server);
        var reports = new ConcurrentQueue<PublishDiagnosticsParams>();
        sut.DiagnosticsPublished += (_, p) => reports.Enqueue(p);
        await sut.StartAsync(TestContext.Current.CancellationToken);
        if (enabled) await Until(() => !reports.IsEmpty, "initial workspace answer");
        await sut.OpenDocumentAsync(Uri, "x", TestContext.Current.CancellationToken);
        await Until(() => reports.Any(p => p.Uri == Uri && p.Diagnostics.Any()), "document pull must publish");
        if (enabled) server.Held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await sut.CloseDocumentAsync(Uri, TestContext.Current.CancellationToken);
        if (enabled) await Until(() => server.Requests.Count == 2, "close must re-pull workspace diagnostics");
        await Task.Delay(100, TestContext.Current.CancellationToken);
        reports.Last(p => p.Uri == Uri).Diagnostics.Count().Should().Be(enabled ? 1 : 0);
        if (!enabled) server.Requests.Should().BeEmpty();
        else server.Held!.SetResult(JsonDocument.Parse(server.Answer).RootElement.Clone());
    }

    [Fact]
    [Trait("Issue", "4263")]
    public async Task ARefreshCancelsTheWorkspaceRequestOnTheWire()
    {
        await using var capture = new HexIDE.Conversations.ConversationLog();
        var server = new WorkspaceServer { Held = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var sut = ClientTalkingTo(server, capture);
        await sut.StartAsync(TestContext.Current.CancellationToken);
        await Until(() => server.Requests.Count == 1, "first workspace request must reach the server");
        await RefreshWorkspace();
        await Until(() => server.Requests.Count == 2, "refresh must replace the held request");
        await capture.DrainAsync();
        capture.Snapshot("scripted.pull").Should().Contain(e => e.Method == "$/cancelRequest"
            && e.Direction == HexIDE.Conversations.ConversationDirection.Sent);
        server.Held.SetResult(JsonDocument.Parse("""{"items":[]}""").RootElement.Clone());
    }

    [Fact]
    [Trait("Issue", "4263")]
    public async Task WorkspaceReportsCannotReplaceAnOpenDocumentsOwnPull()
    {
        var server = new WorkspaceServer();
        var sut = ClientTalkingTo(server);
        var reports = new ConcurrentQueue<PublishDiagnosticsParams>();
        sut.DiagnosticsPublished += (_, p) => reports.Enqueue(p);
        await sut.StartAsync(TestContext.Current.CancellationToken);
        await Until(() => !reports.IsEmpty, "initial workspace answer");
        await sut.OpenDocumentAsync(Uri, "x", TestContext.Current.CancellationToken);
        await Until(() => reports.Any(p => p.Uri == Uri), "document pull answer");
        server.Answer = server.Answer.Replace("file:///project/Closed.bas", Uri);
        await RefreshWorkspace();
        await Until(() => server.Requests.Count == 2, "workspace refresh answer");
        await Task.Delay(100, TestContext.Current.CancellationToken);
        reports.Last(p => p.Uri == Uri).Diagnostics.Single().Message.Should().Be("open finding");
    }

    [AvaloniaFact]
    [Trait("Issue", "4263")]
    public async Task ClosedWorkspaceFindingsReachRegistryAndPanelWithNavigationAndFiltering()
    {
        var server = new WorkspaceServer();
        var sut = ClientTalkingTo(server);
        await using var registry = new HexIDE.Lsp.LspClientRegistry(
            [new HexIDE.Lsp.LanguageServerRegistration("rdcore", "RDCore", DocumentLanguage.Vb6Extensions,
                DocumentLanguage.Vb6, () => sut, 0)],
            Substitute.For<Microsoft.Extensions.Logging.ILogger<HexIDE.Lsp.LspClientRegistry>>());
        var editors = Substitute.For<HexIDE.IDE.IEditorService>();
        var path = Path.Combine(Path.GetTempPath(), "HexIDE-4263", "Closed.bas");
        var closed = HexIDE.Lsp.LspDocumentUri.ForFile(path);
        server.Answer = server.Answer.Replace("file:///project/Closed.bas", closed);
        editors.DocumentNameForUri(closed).Returns("Closed");
        var projects = new HexIDE.IDE.ProjectManager(editors, Substitute.For<HexIDE.IDE.IEventBus>());
        var project = TestHelpers.CreateProject("P");
        project.AddModule(new HexIDE.Runtime.ProjectElements.ModuleDefinition(project, "Closed",
            HexIDE.Runtime.ProjectElements.ModuleKind.StandardModule) { AbsolutePath = path });
        projects.AddProject(project);
        projects.AddProject(TestHelpers.CreateProject("Other"));
        var vm = new HexIDE.Tools.Diagnostics.DiagnosticsToolViewModel(registry, registry,
            Substitute.For<HexIDE.IDE.IDocumentDockService>(), editors,
            Substitute.For<HexIDE.Localization.ILocalizationService>(), projects);
        await registry.OpenDocumentAsync("file:///project/Opened.bas", "x", TestContext.Current.CancellationToken);
        await Until(() => registry.Diagnostics.Any(d => d.Uri == closed), "workspace finding must reach the registry");
        vm.Refresh();
        var row = vm.Rows.Single(r => r.Uri == closed);
        row.Document.Should().Be("Closed");
        row.Server.Should().Be("RDCore");
        row.Project.Should().Be("P");
        vm.SelectedRow = row;
        vm.NavigateSelected();
        editors.Received().NavigateTo(closed, 1, 1);
        vm.CurrentDocumentOnly = true;
        vm.Rows.Should().BeEmpty("no editor is active, so current-document filtering excludes closed files");
    }

    [Fact]
    [Trait("Issue", "4263")]
    public async Task ClosingADocumentClearsRetainedFindingsIfTheWorkspaceOmitsIt()
    {
        var server = new WorkspaceServer();
        var sut = ClientTalkingTo(server);
        var reports = new ConcurrentQueue<PublishDiagnosticsParams>();
        sut.DiagnosticsPublished += (_, p) => reports.Enqueue(p);
        await sut.StartAsync(TestContext.Current.CancellationToken);
        await Until(() => !reports.IsEmpty, "initial workspace answer");
        await sut.OpenDocumentAsync(Uri, "x", TestContext.Current.CancellationToken);
        await Until(() => reports.Any(p => p.Uri == Uri), "document pull answer");
        server.Answer = """{"items":[]}""";
        await sut.CloseDocumentAsync(Uri, TestContext.Current.CancellationToken);
        await Until(() => server.Requests.Count == 2 && reports.Last(p => p.Uri == Uri).Diagnostics.Length == 0,
            "omitting the newly closed document must clear its retained findings");
    }

    [Fact]
    [Trait("Issue", "4263")]
    public async Task WorkspaceReportsRecognizeAnEquivalentOpenUri()
    {
        var server = new WorkspaceServer();
        var sut = ClientTalkingTo(server);
        var reports = new ConcurrentQueue<PublishDiagnosticsParams>();
        sut.DiagnosticsPublished += (_, p) => reports.Enqueue(p);
        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.OpenDocumentAsync(Uri, "x", TestContext.Current.CancellationToken);
        await Until(() => reports.Any(p => p.Uri == Uri), "document pull answer");
        server.HeldDocument = new(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Answer = """
            {"items":[
              {"uri":"file:///c:/proj/%73ample.py","version":null,"kind":"full","items":[{"range":{"start":{"line":0,"character":0},"end":{"line":0,"character":1}},"message":"workspace finding"}]},
              {"uri":"file:///project/barrier.bas","version":null,"kind":"full","items":[]}
            ]}
            """;
        await RefreshWorkspace();
        await Until(() => reports.Any(p => p.Uri == "file:///project/barrier.bas"), "the workspace answer must be applied");
        try
        {
            reports.Last(p => LspDocumentUri.AreSame(p.Uri, Uri)).Diagnostics.Single().Message.Should().Be("open finding");
        }
        finally
        {
            server.HeldDocument.SetResult(JsonDocument.Parse("""{"kind":"unchanged","resultId":"open-1"}""").RootElement.Clone());
        }
    }

    [Fact]
    [Trait("Issue", "4263")]
    public async Task WorkspaceResultIdsUseOneSlotForEquivalentUris()
    {
        var server = new WorkspaceServer();
        var sut = ClientTalkingTo(server);
        var reports = new ConcurrentQueue<PublishDiagnosticsParams>();
        sut.DiagnosticsPublished += (_, p) => reports.Enqueue(p);
        await sut.StartAsync(TestContext.Current.CancellationToken);
        await Until(() => reports.Count == 1, "initial workspace answer");
        server.Answer = server.Answer.Replace("Closed.bas", "%43losed.bas").Replace("closed-1", "closed-2");
        await RefreshWorkspace();
        await Until(() => reports.Count == 2, "replacement report for the equivalent URI");
        await RefreshWorkspace();
        await Until(() => server.Requests.Count == 3, "the next request must carry the replacement result ID");
        var previous = server.Requests.Last().GetProperty("previousResultIds");
        previous.GetArrayLength().Should().Be(1);
        previous[0].GetProperty("value").GetString().Should().Be("closed-2");
    }

    private Task RefreshWorkspace()
    {
        return _disposables.OfType<JsonRpc>().Last().InvokeAsync("workspace/diagnostic/refresh");
    }
}
