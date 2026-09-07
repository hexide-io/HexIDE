using System.Text.Json;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// How far a connect attempt got, and where it stopped.
///
/// <para>
/// Every failure on this path used to converge on one observable outcome — no connection, and an exception
/// written to a log nobody reads. A command that does not exist, a pipe with nobody on it, a handshake never
/// answered, a reply that could not be read, and an ordinary shutdown were a single value by the time
/// anything above could look (hexide-io/HexIDE#259).
/// </para>
/// </summary>
public class ConnectAttemptChainTests : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _disposables = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var d in _disposables)
        {
            try { await d.DisposeAsync(); } catch { /* teardown is best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>A transport that refuses to connect and says why, exactly as the real ones now do.</summary>
    private static ILspTransport RefusingTransport(string reason)
    {
        var t = Substitute.For<ILspTransport>();
        t.IsAlive.Returns(false);
        t.LastFailure.Returns(reason);
        t.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IJsonRpcMessageHandler?>(null));
        return t;
    }

    private VBLspClient ClientOver(ILspTransport transport, TimeSpan? initializeTimeout = null)
    {
        var c = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), "vb6",
            initializeTimeout: initializeTimeout);
        _disposables.Add(c);
        return c;
    }

    [Fact]
    public async Task ATransportThatWillNotConnectSaysSoInItsOwnWords()
    {
        // The transport's `null` return is its entire vocabulary for failure. Without LastFailure, "the
        // command is not on PATH" and "nothing was listening on that pipe" are the same value.
        var sut = ClientOver(RefusingTransport("could not start 'rumdl': No such file or directory"));

        await sut.StartAsync(TestContext.Current.CancellationToken);

        var attempt = sut.LastAttempt;
        attempt.Should().NotBeNull();
        attempt!.ReachedStage.Should().Be(LanguageConnectionStage.Connecting);

        var stop = attempt.Steps.Single(s => s.Outcome == LanguageConnectionStepOutcome.StoppedHere);
        stop.Stage.Should().Be(LanguageConnectionStage.Connecting);
        stop.Detail.Should().Contain("rumdl").And.Contain("No such file",
            "the transport already composed this sentence for its log line and then threw it away");
    }

    [Fact]
    public async Task ExactlyOneRungIsMarkedAsTheBreak()
    {
        // A chain with two breaks, or none, is not a chain — it is a list. The single StoppedHere is what
        // makes "where did this stop" answerable by looking rather than by reading every rung.
        var sut = ClientOver(RefusingTransport("nope"));

        await sut.StartAsync(TestContext.Current.CancellationToken);

        sut.LastAttempt!.Steps.Count(s => s.Outcome == LanguageConnectionStepOutcome.StoppedHere)
            .Should().Be(1);
    }

    [Fact]
    public async Task AStageAppearsOnceEvenWhenItIsBothReachedAndTheBreak()
    {
        // Found by looking at the running IDE, not by a test. Reaching a stage and then stopping there is
        // the SAME rung changing outcome; appending rendered "Connecting" twice, once with a tick and once
        // with a cross, which reads as two attempts rather than one that failed where it started.
        var sut = ClientOver(RefusingTransport("nope"));

        await sut.StartAsync(TestContext.Current.CancellationToken);

        var stages = sut.LastAttempt!.Steps.Select(s => s.Stage).ToList();
        stages.Should().OnlyHaveUniqueItems("one rung per stage, whatever happened at it");
        sut.LastAttempt.Steps.Single(s => s.Stage == LanguageConnectionStage.Connecting)
            .Outcome.Should().Be(LanguageConnectionStepOutcome.StoppedHere,
                "the surviving rung is the one carrying the failure, not the optimistic one");
    }

    [Fact]
    public async Task AServerThatNeverAnswersStopsAtTheHandshakeRatherThanAtTheConnection()
    {
        // The distinction the whole chain exists for. Both of these present as "not working"; one is a
        // server that is not there and the other is a server that is there and silent, and they need
        // different responses from whoever is looking.
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();
        // Captured out here on purpose: TestContext.Current is ambient to the test, and reading it from
        // inside a background lambda is not reliable.
        var ct = TestContext.Current.CancellationToken;
        _ = Task.Run(async () =>
        {
            // Accept the connection, read whatever arrives, and answer nothing at all.
            var buffer = new byte[1024];
            try { while (await serverSide.ReadAsync(buffer, ct) > 0) { } } catch { /* closed */ }
        }, ct);

        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>())));

        var sut = ClientOver(transport, initializeTimeout: TimeSpan.FromMilliseconds(250));

        await sut.StartAsync(TestContext.Current.CancellationToken);

        var attempt = sut.LastAttempt;
        attempt.Should().NotBeNull();
        attempt!.ReachedStage.Should().Be(LanguageConnectionStage.HandshakeSent,
            "the channel opened; it is the reply that never came");

        attempt.Steps.Should().Contain(s => s.Stage == LanguageConnectionStage.Connected
                                         && s.Outcome == LanguageConnectionStepOutcome.Reached);

        var stop = attempt.Steps.Single(s => s.Outcome == LanguageConnectionStepOutcome.StoppedHere);
        stop.Stage.Should().Be(LanguageConnectionStage.HandshakeSent);
        stop.Detail.Should().Contain("did not answer");
    }

    [Fact]
    public async Task ASuccessfulConnectionRecordsEveryRungAndBreaksNowhere()
    {
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();
        var server = new MinimalServer();
        var serverRpc = new JsonRpc(
            new HeaderDelimitedMessageHandler(serverSide, serverSide, new SystemTextJsonFormatter()), server);
        serverRpc.StartListening();

        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>())));

        var sut = ClientOver(transport);

        await sut.StartAsync(TestContext.Current.CancellationToken);

        var attempt = sut.LastAttempt;
        attempt.Should().NotBeNull();
        attempt!.ReachedStage.Should().Be(LanguageConnectionStage.Initialized);
        attempt.Steps.Should().OnlyContain(s => s.Outcome == LanguageConnectionStepOutcome.Reached);
        attempt.Steps.Select(s => s.Stage).Should().Equal([
            LanguageConnectionStage.Connecting,
            LanguageConnectionStage.Connected,
            LanguageConnectionStage.HandshakeSent,
            LanguageConnectionStage.Initialized,
        ], "the rungs are the sequence, so their order is part of what is being asserted");
    }

    [Fact]
    public async Task TheRungsCarryHowLongTheyTook()
    {
        // A handshake is bounded in tens of seconds. Without the timing, "stopped at the handshake" cannot
        // distinguish a server that refused instantly from one that hung until the timeout.
        var sut = ClientOver(RefusingTransport("nope"));

        await sut.StartAsync(TestContext.Current.CancellationToken);

        sut.LastAttempt!.Steps.Should().OnlyContain(s => s.At != null);
    }

    private sealed class MinimalServer
    {
        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) =>
            JsonDocument.Parse("""{"capabilities":{}}""").RootElement.Clone();

        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }
    }
}
