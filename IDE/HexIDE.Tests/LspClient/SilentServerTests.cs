using System.Text.Json;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// A server that accepts the connection and then says nothing.
///
/// <para>
/// This is the failure with no symptoms. The process starts, the transport connects, and the
/// <c>initialize</c> reply never comes — so the await never returns, nothing throws, nothing is logged, and
/// language features simply never appear. It is not a hang from the user's side, because the handshake runs
/// fire-and-forget from startup, which is exactly what makes it hard to notice
/// (hexide-io/HexIDE#231).
/// </para>
///
/// <para>
/// It is also not hypothetical. A deadlocked handler or a parse that outlives the server's own budget
/// reaches it from our side, and a replaceable-backend design invites people to point this client at
/// servers whose startup behaviour nobody here controls.
/// </para>
/// </summary>
public class SilentServerTests : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _clients = [];
    private readonly List<IDisposable> _disposables = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            try { await client.DisposeAsync(); } catch { /* teardown is best effort */ }
        }
        lock (_disposables)
        {
            foreach (var d in _disposables)
            {
                try { d.Dispose(); } catch { /* teardown is best effort */ }
            }
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>Answers nothing, ever. The <c>initialize</c> request arrives and simply never completes.</summary>
    private sealed class MuteServer
    {
        private readonly TaskCompletionSource _never =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _calls;
        public int InitializeCalls => Volatile.Read(ref _calls);

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public async Task<JsonElement> Initialize(JsonElement _)
        {
            Interlocked.Increment(ref _calls);
            await _never.Task;              // deliberately never completed
            return default;
        }
    }

    /// <summary>
    /// Records what was logged, at what level.
    ///
    /// <para>
    /// Hand-written rather than an NSubstitute double, and not a lapse from the project's mocking rule: the
    /// substitute form of this assertion is <c>ReceivedWithAnyArgs</c>, which matches <em>any</em>
    /// arguments — including the log level. The first draft of these tests used it and passed while the
    /// client was logging an Error rather than the Warning being asserted, which is precisely the vacuous
    /// green this suite exists to avoid. Capturing the level makes the assertion mean what it says.
    /// </para>
    /// </summary>
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

    private VBLspClient ClientTalkingTo(
        MuteServer server, TimeSpan timeout, bool canReconnect, ILogger<VBLspClient>? logger = null)
    {
        // A fresh channel per connect, because that is what reconnecting means. Handing back the same pair
        // would give the retry a stream this client had already disposed, and the test would then be
        // measuring a dead socket rather than a server that stays silent.
        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.CanReconnect.Returns(canReconnect);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var (clientSide, serverSide) = FullDuplexStream.CreatePair();
                var serverRpc = new JsonRpc(
                    new HeaderDelimitedMessageHandler(serverSide, serverSide, new SystemTextJsonFormatter()),
                    server);
                serverRpc.StartListening();
                lock (_disposables) _disposables.Add(serverRpc);

                return Task.FromResult<IJsonRpcMessageHandler?>(
                    new HeaderDelimitedMessageHandler(
                        clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>()));
            });

        var client = new VBLspClient(
            transport, logger ?? Substitute.For<ILogger<VBLspClient>>(),
            DocumentLanguage.Vb6, workspace: null, initializeTimeout: timeout);
        _clients.Add(client);
        return client;
    }

    [Fact]
    public async Task StartingAgainstASilentServerReturnsRatherThanWaitingForever()
    {
        // The bug itself. Before the timeout this await never completed, so every assertion below was
        // unreachable — the test would have hung rather than failed, which is its own kind of useless.
        var sut = ClientTalkingTo(new MuteServer(), TimeSpan.FromMilliseconds(300), canReconnect: false);

        var start = sut.StartAsync();
        var finished = await Task.WhenAny(start, Task.Delay(TimeSpan.FromSeconds(20)));

        finished.Should().BeSameAs(start,
            "a server that never answers initialize must not hold the handshake open indefinitely");
        await start;
    }

    [Fact]
    public async Task ASilentServerLeavesTheClientNotRunningRatherThanHalfConnected()
    {
        var sut = ClientTalkingTo(new MuteServer(), TimeSpan.FromMilliseconds(300), canReconnect: false);

        await sut.StartAsync().WaitAsync(TimeSpan.FromSeconds(20));

        sut.IsRunning.Should().BeFalse("the handshake never completed, so there is nothing to talk to");
        sut.AdvertisedCapabilities.Should().BeNull("nothing was ever advertised");
    }

    [Fact]
    public async Task TheServerIsActuallyAsked_SoTheTimeoutIsNotMeasuringAnEmptyConnection()
    {
        // Guards the test itself. If the wiring were wrong the request might never be sent at all, and
        // every assertion here would still pass while proving something else entirely.
        var server = new MuteServer();
        var sut = ClientTalkingTo(server, TimeSpan.FromMilliseconds(300), canReconnect: false);

        await sut.StartAsync().WaitAsync(TimeSpan.FromSeconds(20));

        server.InitializeCalls.Should().Be(1, "the client must have actually sent initialize and waited");
    }

    [Fact]
    public async Task GivingUpIsReportedAtWarning_BecauseSilenceIsTheSymptom()
    {
        // The point of the fix is not that it recovers — over stdio it cannot. It is that the failure
        // stops being invisible. If this ever drops to Debug, the bug is functionally back.
        var logger = new RecordingLogger();
        var sut = ClientTalkingTo(new MuteServer(), TimeSpan.FromMilliseconds(300), canReconnect: false,
            logger: logger);

        await sut.StartAsync().WaitAsync(TimeSpan.FromSeconds(20));

        logger.Entries.Should().ContainSingle(e =>
                e.Level == LogLevel.Warning && e.Message.Contains("did not answer initialize"),
            "the whole bug is that this failure said nothing, so the giving-up must be visible");
    }

    [Fact]
    public async Task AReconnectingTransportKeepsTryingRatherThanGivingUpOnce()
    {
        // For a transport that can re-dial, abandoning one handshake must hand over to the retry loop.
        // DisposeRpc detaches the disconnect handler before disposing, so nothing starts that loop by
        // itself — it has to be started deliberately, and this is what says so.
        var server = new MuteServer();
        var sut = ClientTalkingTo(server, TimeSpan.FromMilliseconds(200), canReconnect: true);

        await sut.StartAsync().WaitAsync(TimeSpan.FromSeconds(20));

        // The loop's first delay is a second, so allow for one further attempt beyond the initial one.
        await Task.Delay(TimeSpan.FromSeconds(3));

        server.InitializeCalls.Should().BeGreaterThan(1,
            "a transport that can reconnect should retry the handshake rather than stay silently dead");
    }

    [Fact]
    public async Task StoppingDuringTheHandshakeIsNotReportedAsAFailure()
    {
        // Shutdown races the handshake routinely — closing the IDE while a server is still starting. That
        // is ordinary, and must not look like a fault in the log or the timeout would cry wolf on exit.
        var logger = new RecordingLogger();
        var sut = ClientTalkingTo(new MuteServer(), TimeSpan.FromSeconds(30), canReconnect: false,
            logger: logger);

        using var stopping = new CancellationTokenSource();
        var start = sut.StartAsync(stopping.Token);
        await Task.Delay(200);
        await stopping.CancelAsync();

        await start.WaitAsync(TimeSpan.FromSeconds(20));

        logger.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning,
            "a shutdown that races the handshake is routine, and a timeout that cries wolf on every exit "
          + "is one a reader learns to ignore");
    }
}
