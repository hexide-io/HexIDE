using System.IO.Pipes;
using System.Text.Json;
using HexIDE.Addins;
using HexIDE.Conversations;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// A language server an add-in contributes (#457, #2760): added to the router while the IDE runs, checked as an entry
/// of the configuration file is, gated before it launches, and given a channel of its own on HexIDE's connection.
/// </summary>
public class AddinServerRegistrationTests : IDisposable
{
    private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(b => { });

    public void Dispose()
    {
        _loggerFactory.Dispose();
        GC.SuppressFinalize(this);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private LspClientRegistry Router(params LanguageServerRegistration[] registrations) =>
        new(registrations, _loggerFactory.CreateLogger<LspClientRegistry>());

    private static LanguageServerRegistration Markdown(Func<ILspClient> client, string id = "md") =>
        new(id, "Markdown", [".md"], "markdown", client);

    private static (Func<ILspClient> Factory, List<ILspClient> Made) FakeClients()
    {
        var made = new List<ILspClient>();
        return (() =>
        {
            var client = Substitute.For<ILspClient>();
            client.IsRunning.Returns(true);
            made.Add(client);
            return client;
        }, made);
    }

    private LanguageServerContributions Contributions(LspClientRegistry router, params string[] configured) =>
        new(router, new LanguageServerRegistrationFactory(_loggerFactory),
            new LanguageServerConfigLoader(_loggerFactory.CreateLogger<LanguageServerConfigLoader>()),
            configured.ToHashSet(StringComparer.Ordinal),
            _loggerFactory.CreateLogger<LanguageServerContributions>());

    private static LanguageServerEntry PipeEntry(string id, string pipeName) => new()
    {
        Id = id,
        DisplayName = "Probe",
        Extensions = [".md"],
        LanguageId = "markdown",
        Transport = "pipe",
        PipeName = pipeName,
    };

    // ── the router ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AServerAddedWhileDocumentsAreOpen_IsOpenedOnTheOnesItClaims()
    {
        // An add-in that waited for consent registers after the window opened, with the startup project's documents open.
        var router = Router();
        await router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", Token);
        await router.OpenDocumentAsync("file:///c:/p/Module1.bas", "Sub Main()\nEnd Sub", Token);
        var (factory, made) = FakeClients();

        var added = router.Add(Markdown(factory));
        await added.Opened.WaitAsync(TimeSpan.FromSeconds(30), Token);

        var client = made.Should().ContainSingle("an open document it claims starts it").Subject;
        await client.Received(1).OpenDocumentAsync("file:///c:/p/README.md", "# hi", Arg.Any<CancellationToken>());
        await client.DidNotReceive().OpenDocumentAsync("file:///c:/p/Module1.bas", Arg.Any<string>(), Arg.Any<CancellationToken>());
        router.Connections.Should().ContainSingle(c => c.Id == "md");
    }

    [Fact]
    public async Task AServerAddedWhileItsGateHoldsItsStart_StallsNoOtherDocumentsOpen()
    {
        // The workspace is what makes an open pass through the router's restart gate, which a replay must not hold.
        var workspace = Substitute.For<ILspWorkspace>();
        workspace.Directory.Returns("c:/p");
        var (others, _) = FakeClients();
        var router = new LspClientRegistry([new LanguageServerRegistration("txt", "Text", [".txt"], "plaintext", others)],
            _loggerFactory.CreateLogger<LspClientRegistry>(), workspace);
        await router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (factory, _) = FakeClients();

        var added = router.Add(Markdown(factory) with
        {
            BeforeLaunch = async token => { entered.TrySetResult(); await release.Task.WaitAsync(token); },
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);

        // Another language's document opens while the contributed server is still held at its gate.
        await router.OpenDocumentAsync("file:///c:/p/notes.txt", "notes", Token).WaitAsync(TimeSpan.FromSeconds(30), Token);

        release.SetResult();
        await added.Opened.WaitAsync(TimeSpan.FromSeconds(30), Token);
    }

    [Fact]
    public async Task AServerAddedWithNothingOpenThatItClaims_StartsOnTheFirstDocumentItClaims()
    {
        var router = Router();
        var (factory, made) = FakeClients();

        var added = router.Add(Markdown(factory));
        await added.Opened.WaitAsync(TimeSpan.FromSeconds(30), Token);
        made.Should().BeEmpty("nothing it claims is open");

        await router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", Token);
        await made.Should().ContainSingle().Subject.Received(1)
            .OpenDocumentAsync("file:///c:/p/README.md", "# hi", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AnIdTheRouterKnows_CannotBeAddedTwice()
    {
        var router = Router(Markdown(FakeClients().Factory));

        var adding = () => router.Add(Markdown(FakeClients().Factory));

        adding.Should().Throw<InvalidOperationException>();
        router.Connections.Should().ContainSingle();
    }

    [Fact]
    public async Task TakingAContributionOut_StopsItsServer_AndRemovesItsRow()
    {
        var router = Router();
        var (factory, made) = FakeClients();
        var added = router.Add(Markdown(factory));
        await router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", Token);

        await added.DisposeAsync();

        await made.Should().ContainSingle().Subject.Received(1).StopAsync();
        router.Connections.Should().BeEmpty();
        added.Client.Should().BeNull();

        await router.OpenDocumentAsync("file:///c:/p/Other.md", "# again", Token);
        made.Should().ContainSingle("a server taken out is not started again");
    }

    [Fact]
    public async Task TheGateBeforeLaunch_RunsBeforeTheClientIsMade_AndTheStartWaitsForIt()
    {
        var order = new List<string>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (factory, _) = FakeClients();
        var router = Router(Markdown(() => { order.Add("client"); return factory(); }) with
        {
            BeforeLaunch = async token => { order.Add("gate"); await release.Task.WaitAsync(token); order.Add("gate done"); },
        });

        var opening = router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", Token);
        SpinWait.SpinUntil(() => order.Count > 0, TimeSpan.FromSeconds(30)).Should().BeTrue();
        order.Should().Equal(["gate"], "nothing is made while the gate has not let it through");

        release.SetResult();
        await opening.WaitAsync(TimeSpan.FromSeconds(30), Token);

        order.Should().Equal("gate", "gate done", "client");
    }

    [Fact]
    public async Task AGateThatThrows_FailsTheStart_WithItsMessage()
    {
        var (factory, made) = FakeClients();
        var router = Router(Markdown(factory) with
        {
            BeforeLaunch = _ => throw new InvalidOperationException("the server's project file could not be written"),
        });

        await router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", Token);

        made.Should().BeEmpty("a gate that refuses launches nothing");
        var row = router.Connections.Should().ContainSingle().Subject;
        row.State.Should().Be(LanguageConnectionState.Failed);
        row.Attempt!.Steps.Should().Contain(step => step.Detail == "the server's project file could not be written");
    }

    // ── the contribution ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AContributedServer_IsListedUnderTheAddinThatContributedIt()
    {
        var router = Router();

        using var connection = Contributions(router).Register(new AddinLanguageServer(PipeEntry("probe", "unused")), "My Add-in");

        router.Connections.Should().ContainSingle().Which.Origin.Should().Be("My Add-in");
        connection.Id.Should().Be("probe");
        connection.IsRunning.Should().BeFalse("it starts on the first document it claims");
    }

    [Fact]
    public void AnIdTheConfigurationNames_KeepsItsConfiguredEntry_AndTheContributionIsNotAdded()
    {
        // The user's file names it, switched off: the user's entry still wins, and the add-in is told.
        var router = Router();

        var registering = () => Contributions(router, "probe").Register(new AddinLanguageServer(PipeEntry("probe", "unused")), "My Add-in");

        registering.Should().Throw<InvalidOperationException>();
        router.Connections.Should().BeEmpty();
        router.ConfigurationProblems.Should().ContainSingle(p => p.EntryId == "probe" && p.EntryRejected && p.Message.Contains("My Add-in"));
    }

    [Fact]
    public void AnEntryTheChecksReject_IsAProblemInTheWindow_AndNotAdded()
    {
        var router = Router();
        var entry = PipeEntry("probe", "unused");
        entry.Extensions = null;

        var registering = () => Contributions(router).Register(new AddinLanguageServer(entry), "My Add-in");

        registering.Should().Throw<ArgumentException>();
        router.Connections.Should().BeEmpty();
        router.ConfigurationProblems.Should().Contain(p => p.EntryId == "probe" && p.EntryRejected);
    }

    [Theory]
    [InlineData("textDocument/", "a namespace of the protocol's own")]
    [InlineData("workspace/x/", "inside a namespace of the protocol's own")]
    [InlineData("$/", "the protocol's own")]
    [InlineData("probe", "no slash")]
    [InlineData("/", "nothing before the slash")]
    public void APrefixThatIsNotTheAddinsToUse_IsRefused(string prefix, string because)
    {
        var router = Router();

        var registering = () => Contributions(router).Register(
            new AddinLanguageServer(PipeEntry("probe", "unused")) { MessagePrefix = prefix }, "My Add-in");

        registering.Should().Throw<ArgumentException>(because);
        router.Connections.Should().BeEmpty();
    }

    [Fact]
    public void AMethodClaimedOutsideThePrefix_IsRefused()
    {
        var router = Router();

        var registering = () => Contributions(router).Register(new AddinLanguageServer(PipeEntry("probe", "unused"))
        {
            MessagePrefix = "probe/",
            ServerMethods = new Dictionary<string, AddinServerMethod> { ["window/showMessageRequest"] = (_, _) => Task.FromResult<JsonElement?>(null) },
        }, "My Add-in");

        registering.Should().Throw<ArgumentException>();
        router.Connections.Should().BeEmpty();
    }

    [Fact]
    public async Task ASendOutsideThePrefix_IsRefused()
    {
        var router = Router();
        using var connection = Contributions(router).Register(
            new AddinLanguageServer(PipeEntry("probe", "unused")) { MessagePrefix = "probe/" }, "My Add-in");

        var sending = () => connection.RequestAsync("textDocument/hover", null, Token);

        await sending.Should().ThrowAsync<ArgumentException>();
    }

    // ── the channel, on the wire ────────────────────────────────────────────────────────────────

    // A server of the add-in's: it echoes a request of its own, takes a notification of its own, and asks the client a
    // question of its own once it is initialised.
    private sealed class ProbeServer
    {
        private readonly TaskCompletionSource<JsonElement> _noted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<JsonElement> Noted => _noted.Task;
        public Task Initialized => _initialized.Task;

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) => JsonDocument.Parse(
            """{"capabilities":{"textDocumentSync":{"openClose":true,"change":1}}}""").RootElement.Clone();

        [JsonRpcMethod("initialized", UseSingleObjectParameterDeserialization = true)]
        public void OnInitialized(JsonElement _) => _initialized.TrySetResult();

        [JsonRpcMethod("probe/echo", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Echo(JsonElement p) => p.Clone();

        [JsonRpcMethod("probe/note", UseSingleObjectParameterDeserialization = true)]
        public void Note(JsonElement p) => _noted.TrySetResult(p.Clone());

        /// <summary>The server's own connection, for a request it answers by asking the client first.</summary>
        public JsonRpc? Rpc { get; set; }

        // As a program that shows a dialog is run: the client's request is answered only after the server's question to the
        // client is.
        [JsonRpcMethod("probe/run", UseSingleObjectParameterDeserialization = true)]
        public async Task<JsonElement> RunAsync(JsonElement _)
        {
            var answer = await Rpc!.InvokeWithParameterObjectAsync<JsonElement>("probe/ask", new { q = "while running" });
            return JsonDocument.Parse($$"""{"ran":{{answer.GetProperty("answer").GetInt32()}}}""").RootElement.Clone();
        }
    }

    [Fact]
    public async Task TheAddinsMessages_GoOverHexIdesConnection_BothWays_AndAreCaptured()
    {
        var pipeName = "hexide-addin-" + Guid.NewGuid().ToString("N");
        var server = new ProbeServer();
        using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var serving = pipe.WaitForConnectionAsync(Token).ContinueWith(_ =>
        {
            var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(pipe, pipe, new SystemTextJsonFormatter()), server);
            server.Rpc = rpc;
            rpc.StartListening();
            return rpc;
        }, TaskScheduler.Default);

        await using var capture = new ConversationLog(armEveryConnection: true);
        var router = Router();
        var contributions = new LanguageServerContributions(router, new LanguageServerRegistrationFactory(_loggerFactory, capture: capture),
            new LanguageServerConfigLoader(_loggerFactory.CreateLogger<LanguageServerConfigLoader>()),
            new HashSet<string>(StringComparer.Ordinal), _loggerFactory.CreateLogger<LanguageServerContributions>());
        var asked = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = contributions.Register(new AddinLanguageServer(PipeEntry("probe", pipeName))
        {
            MessagePrefix = "probe/",
            ServerMethods = new Dictionary<string, AddinServerMethod>
            {
                ["probe/ask"] = (parameters, _) =>
                {
                    asked.TrySetResult(parameters);
                    return Task.FromResult<JsonElement?>(JsonDocument.Parse("""{"answer":42}""").RootElement.Clone());
                },
            },
        }, "My Add-in");
        await using var _ = connection;

        await router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", Token);
        var rpc = await serving;
        await server.Initialized.WaitAsync(TimeSpan.FromSeconds(30), Token);
        connection.IsRunning.Should().BeTrue();

        // The add-in's request, and its notification, reach the server as they were sent.
        var echoed = await connection.RequestAsync("probe/echo", JsonDocument.Parse("""{"n":1}""").RootElement, Token);
        echoed!.Value.GetProperty("n").GetInt32().Should().Be(1);
        await connection.NotifyAsync("probe/note", JsonDocument.Parse("""{"m":"x"}""").RootElement, Token);
        (await server.Noted.WaitAsync(TimeSpan.FromSeconds(30), Token)).GetProperty("m").GetString().Should().Be("x");

        // The server's question is the add-in's to answer; one nobody claimed is not found, as before.
        var answer = await rpc.InvokeWithParameterObjectAsync<JsonElement>("probe/ask", new { q = "?" }, Token);
        answer.GetProperty("answer").GetInt32().Should().Be(42);
        (await asked.Task.WaitAsync(TimeSpan.FromSeconds(30), Token))!.Value.GetProperty("q").GetString().Should().Be("?");
        var unclaimed = () => rpc.InvokeWithParameterObjectAsync<JsonElement>("probe/unclaimed", new { }, Token);
        await unclaimed.Should().ThrowAsync<RemoteMethodNotFoundException>();

        // The server asks while the add-in's own request waits for its answer, as a program that shows a dialog does.
        var ran = await connection.RequestAsync("probe/run", JsonDocument.Parse("{}").RootElement, Token).WaitAsync(TimeSpan.FromSeconds(30), Token);
        ran!.Value.GetProperty("ran").GetInt32().Should().Be(42);

        // A request of the add-in's own the server does not know is refused by the server, and the connection survives it.
        var unknown = () => connection.RequestAsync("probe/unknown", JsonDocument.Parse("{}").RootElement, Token);
        await unknown.Should().ThrowAsync<RemoteMethodNotFoundException>();
        connection.IsRunning.Should().BeTrue("a server's refusal of an add-in's request is the add-in's to handle, not the connection's end");

        // Over HexIDE's own connection, so the Protocol Inspector has them.
        await capture.DrainAsync();
        var record = capture.Snapshot("probe");
        record.Select(e => e.Method).Should().Contain(["probe/echo", "probe/note", "probe/ask"]);
        record.Where(e => e.Method == "probe/ask" && e.Direction == ConversationDirection.Received).Should()
            .HaveCount(2).And.OnlyContain(e => e.Outcome == ConversationOutcome.Answered,
                "the add-in's answer goes out over the same connection and completes the server's request in the record");

        rpc.Dispose();
    }
}
