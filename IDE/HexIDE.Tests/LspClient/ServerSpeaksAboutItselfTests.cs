using System.Text.Json;
using HexIDE.IDE;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// A server talking about itself, rather than about the developer's code.
///
/// <para>
/// <c>window/logMessage</c> and <c>window/showMessage</c> are the only channel a server has for "I cannot
/// find your toolchain" or "I am running degraded". HexIDE discarded both, and since a user can attach any
/// server by editing <c>lsp-servers.json</c>, that made a misconfigured server look exactly like a broken
/// IDE — it starts, it connects, no diagnostics appear, and nothing anywhere says why
/// (hexide-io/HexIDE#289).
/// </para>
/// </summary>
public class ServerSpeaksAboutItselfTests : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _clients = [];
    private readonly List<IDisposable> _disposables = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var c in _clients)
        {
            try { await c.DisposeAsync(); } catch { /* teardown is best effort */ }
        }
        foreach (var d in _disposables)
        {
            try { d.Dispose(); } catch { /* teardown is best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>Initializes, then says whatever the test told it to say.</summary>
    private sealed class TalkativeServer
    {
        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) =>
            JsonDocument.Parse("""{"capabilities":{"textDocumentSync":1}}""").RootElement.Clone();

        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }

        public JsonRpc? Rpc;

        public Task SayAsync(string method, LspMessageType type, string message) =>
            Rpc!.NotifyWithParameterObjectAsync(method, new { type = (int)type, message });
    }

    private sealed class RecordingLogger : ILogger<VBLspClient>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public IReadOnlyList<(LogLevel Level, string Message)> Entries
        {
            get { lock (_entries) return _entries.ToArray(); }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_entries) _entries.Add((logLevel, formatter(state, exception)));
        }
    }

    private async Task<(VBLspClient Client, TalkativeServer Server, RecordingLogger Log)> ConnectedAsync()
    {
        var server = new TalkativeServer();
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();

        var serverRpc = new JsonRpc(
            new HeaderDelimitedMessageHandler(serverSide, serverSide, new SystemTextJsonFormatter()), server);
        server.Rpc = serverRpc;
        serverRpc.StartListening();
        _disposables.Add(serverRpc);

        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>())));

        var log = new RecordingLogger();
        var client = new VBLspClient(transport, log, DocumentLanguage.Vb6);
        _clients.Add(client);
        await client.StartAsync();
        return (client, server, log);
    }

    private static async Task<T> Eventually<T>(Func<T?> probe, string what) where T : class
    {
        for (var i = 0; i < 100; i++)
        {
            if (probe() is { } value) return value;
            await Task.Delay(20);
        }
        throw new TimeoutException($"Timed out waiting for {what}.");
    }

    // ── logMessage: the log, and only the log ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(LspMessageType.Error, LogLevel.Error)]
    [InlineData(LspMessageType.Warning, LogLevel.Warning)]
    [InlineData(LspMessageType.Info, LogLevel.Information)]
    [InlineData(LspMessageType.Log, LogLevel.Debug)]
    public async Task ALoggedMessageIsWrittenAtTheSeverityTheServerDeclared(
        LspMessageType declared, LogLevel expected)
    {
        // The severity is the server's to choose. Flattening everything to one level would make an "I am
        // broken" indistinguishable from running commentary, which is most of the value gone.
        var (_, server, log) = await ConnectedAsync();

        await server.SayAsync("window/logMessage", declared, "toolchain not found");

        var entry = await Eventually(
            () => log.Entries.FirstOrDefault(e => e.Message.Contains("toolchain not found")) is
                  { Message: not null } hit ? (object)hit : null,
            "the server's log message to reach the logger");

        (((LogLevel Level, string Message))entry).Level.Should().Be(expected);
    }

    [Fact]
    public async Task ALoggedMessageIsNotPutInFrontOfTheUser()
    {
        // logMessage is where a server puts detail, and a server in a bad state can be voluble on it.
        // Surfacing it would trade a silent failure for an unusable IDE.
        var (client, server, _) = await ConnectedAsync();
        var shown = 0;
        client.MessageShown += (_, _) => Interlocked.Increment(ref shown);

        await server.SayAsync("window/logMessage", LspMessageType.Error, "chatty detail");
        await Task.Delay(300);

        shown.Should().Be(0, "logMessage is for the log; showMessage is the channel that asks for attention");
    }

    // ── showMessage: surfaced, and logged as well ────────────────────────────────────────────────────

    [Fact]
    public async Task AShownMessageReachesASubscriber()
    {
        var (client, server, _) = await ConnectedAsync();
        ShowMessageParams? seen = null;
        client.MessageShown += (_, p) => seen = p;

        await server.SayAsync("window/showMessage", LspMessageType.Error, "cannot find latexmk");

        var got = await Eventually(() => seen, "the server's message to reach a subscriber");
        got.Message.Should().Be("cannot find latexmk");
        got.Type.Should().Be(LspMessageType.Error);
    }

    [Fact]
    public async Task AShownMessageIsAlsoLogged()
    {
        // Both, deliberately: a message the user never sees is the bug being fixed, and a message with no
        // trace afterwards is the next one — the status bar clears itself, and then it never happened.
        var (_, server, log) = await ConnectedAsync();

        await server.SayAsync("window/showMessage", LspMessageType.Warning, "degraded mode");

        await Eventually(
            () => log.Entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains("degraded mode"))
                  ? "found" : null,
            "the shown message to also be logged");
    }

    // ── The reporter: what a user actually sees ──────────────────────────────────────────────────────

    [Fact]
    public void TheStatusBarShowsAServerMessage()
    {
        var client = Substitute.For<ILspClient>();
        var statusBar = Substitute.For<IStatusBarService>();
        using var sut = new LanguageServerMessageReporter(client, statusBar);

        client.MessageShown += Raise.Event<EventHandler<ShowMessageParams>>(
            client, new ShowMessageParams(LspMessageType.Error, "cannot find latexmk"));

        statusBar.Received(1).SetTemporaryMessage(
            Arg.Is<string>(m => m.Contains("cannot find latexmk")), Arg.Any<TimeSpan>());
    }

    [Fact]
    public void AnErrorLingersLongerThanChatter()
    {
        var client = Substitute.For<ILspClient>();
        var statusBar = Substitute.For<IStatusBarService>();
        using var sut = new LanguageServerMessageReporter(client, statusBar);

        client.MessageShown += Raise.Event<EventHandler<ShowMessageParams>>(
            client, new ShowMessageParams(LspMessageType.Error, "fatal"));
        client.MessageShown += Raise.Event<EventHandler<ShowMessageParams>>(
            client, new ShowMessageParams(LspMessageType.Info, "ready"));

        var durations = statusBar.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IStatusBarService.SetTemporaryMessage))
            .Select(c => (TimeSpan)c.GetArguments()[1]!)
            .ToArray();

        durations.Should().HaveCount(2);
        durations[0].Should().BeGreaterThan(durations[1], "an error is worth reading twice; chatter is not");
    }

    [Fact]
    public void AMultiLineMessageIsFlattenedSoTheStatusBarDoesNotJump()
    {
        var client = Substitute.For<ILspClient>();
        var statusBar = Substitute.For<IStatusBarService>();
        using var sut = new LanguageServerMessageReporter(client, statusBar);

        client.MessageShown += Raise.Event<EventHandler<ShowMessageParams>>(
            client, new ShowMessageParams(LspMessageType.Info, "line one\r\n  line two\n\nline three"));

        statusBar.Received(1).SetTemporaryMessage(
            Arg.Is<string>(m => m.EndsWith("line one line two line three")), Arg.Any<TimeSpan>());
    }

    [Fact]
    public void ARunawayMessageIsTruncatedRatherThanFillingTheStatusBar()
    {
        var client = Substitute.For<ILspClient>();
        var statusBar = Substitute.For<IStatusBarService>();
        using var sut = new LanguageServerMessageReporter(client, statusBar);

        client.MessageShown += Raise.Event<EventHandler<ShowMessageParams>>(
            client, new ShowMessageParams(LspMessageType.Info, new string('x', 5000)));

        statusBar.Received(1).SetTemporaryMessage(
            Arg.Is<string>(m => m.Length < 300 && m.EndsWith("…")), Arg.Any<TimeSpan>());
    }

    [Fact]
    public void AnEmptyMessageIsNotShownAtAll()
    {
        // A server sending nothing should not blank the status bar over whatever it was saying.
        var client = Substitute.For<ILspClient>();
        var statusBar = Substitute.For<IStatusBarService>();
        using var sut = new LanguageServerMessageReporter(client, statusBar);

        client.MessageShown += Raise.Event<EventHandler<ShowMessageParams>>(
            client, new ShowMessageParams(LspMessageType.Info, "   \r\n  "));

        statusBar.DidNotReceiveWithAnyArgs().SetTemporaryMessage(default!, default);
    }

    [Fact]
    public void DisposingStopsReporting()
    {
        var client = Substitute.For<ILspClient>();
        var statusBar = Substitute.For<IStatusBarService>();
        var sut = new LanguageServerMessageReporter(client, statusBar);
        sut.Dispose();

        client.MessageShown += Raise.Event<EventHandler<ShowMessageParams>>(
            client, new ShowMessageParams(LspMessageType.Error, "after disposal"));

        statusBar.DidNotReceiveWithAnyArgs().SetTemporaryMessage(default!, default);
    }
}
