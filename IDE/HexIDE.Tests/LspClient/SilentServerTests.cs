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

    /// <summary>Never completes initialize unless the callback fails or closes the connection.</summary>
    private sealed class MuteServer
    {
        private readonly TaskCompletionSource _never =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Action? OnInitialize { get; set; }
        public TaskCompletionSource ConnectionClosed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _calls;
        public int InitializeCalls => Volatile.Read(ref _calls);

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public async Task<JsonElement> Initialize(JsonElement _)
        {
            Interlocked.Increment(ref _calls);
            OnInitialize?.Invoke();
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
            lock (_entries) _entries.Add((logLevel, exception is null
                ? formatter(state, exception)
                : $"{formatter(state, exception)}{Environment.NewLine}{exception}"));
        }
    }

    private VBLspClient ClientTalkingTo(
        MuteServer server, TimeSpan timeout, bool canReconnect, ILogger<VBLspClient>? logger = null,
        bool closeOnInitialize = false)
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
                serverRpc.Disconnected += (_, _) => server.ConnectionClosed.TrySetResult();
                if (closeOnInitialize) server.OnInitialize = serverSide.Dispose;
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

        var start = sut.StartAsync(TestContext.Current.CancellationToken);
        // xUnit1051 suppressed deliberately: this Task.Delay is the TIMEOUT ARM of the race, not
        // work the test is waiting on. Giving it the test's cancellation token would make the
        // timeout itself cancellable — the guard would vanish exactly when a cancelled run most
        // needs it to fire, and WhenAny would settle on a faulted task rather than a timeout.
#pragma warning disable xUnit1051
        var finished = await Task.WhenAny(start, Task.Delay(TimeSpan.FromSeconds(20)));
#pragma warning restore xUnit1051

        finished.Should().BeSameAs(start,
            "a server that never answers initialize must not hold the handshake open indefinitely");
        await start;
    }

    [Fact]
    public async Task ASilentServerLeavesTheClientNotRunningRatherThanHalfConnected()
    {
        var sut = ClientTalkingTo(new MuteServer(), TimeSpan.FromMilliseconds(300), canReconnect: false);

        await sut.StartAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

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

        await sut.StartAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

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

        // Only the handshake deadline may give up here; runner cancellation belongs to the outer wait.
        using var handshake = new CancellationTokenSource();
        await sut.StartAsync(handshake.Token).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        logger.Entries.Should().ContainSingle(e =>
                e.Level == LogLevel.Warning && e.Message.Contains("did not answer initialize"),
            "the whole bug is that this failure said nothing, so the giving-up must be visible. "
          + "Last attempt: {0}. Recorded logs: {1}",
            sut.LastAttempt is { } attempt
                ? string.Join(Environment.NewLine, attempt.Steps)
                : "(none)",
            string.Join(Environment.NewLine, logger.Entries.Select(e => $"{e.Level}: {e.Message}")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnEarlyHandshakeFailureIsReportedAtWarningAndTornDown(bool closeConnection)
    {
        var logger = new RecordingLogger();
        var server = new MuteServer
        {
            OnInitialize = () => throw new InvalidOperationException("initialize refused by test server")
        };
        var sut = ClientTalkingTo(server, TimeSpan.FromSeconds(30), canReconnect: false,
            logger: logger, closeOnInitialize: closeConnection);

        await sut.StartAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        server.InitializeCalls.Should().Be(1, "the server must receive initialize before failing");
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning
            && e.Message.Contains("Abandoning the handshake")
            && e.Message.Contains("initialize failed")
            && (closeConnection || e.Message.Contains("initialize refused by test server")));
        sut.IsRunning.Should().BeFalse();
        sut.AdvertisedCapabilities.Should().BeNull();
        await server.ConnectionClosed.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AReconnectingTransportKeepsTryingRatherThanGivingUpOnce()
    {
        // For a transport that can re-dial, abandoning one handshake must hand over to the retry loop.
        // DisposeRpc detaches the disconnect handler before disposing, so nothing starts that loop by
        // itself — it has to be started deliberately, and this is what says so.
        var server = new MuteServer();
        var sut = ClientTalkingTo(server, TimeSpan.FromMilliseconds(200), canReconnect: true);

        await sut.StartAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        // The loop's first delay is a second, so allow for one further attempt beyond the initial one.
        await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        server.InitializeCalls.Should().BeGreaterThan(1,
            "a transport that can reconnect should retry the handshake rather than stay silently dead");
    }

    [Fact]
    public async Task StopAsyncDuringTheHandshakeIsNotReportedAsAFailure()
    {
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new MuteServer { OnInitialize = () => received.TrySetResult() };
        var logger = new RecordingLogger();
        var sut = ClientTalkingTo(server, TimeSpan.FromSeconds(30), canReconnect: false, logger: logger);
        var start = sut.StartAsync(TestContext.Current.CancellationToken);
        await received.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        await sut.StopAsync();
        await start.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        server.InitializeCalls.Should().Be(1);
        logger.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning,
            "stopping the client tears down RPC without cancelling the caller's token");
        sut.LastAttempt!.Steps.Last().Outcome.Should().Be(LanguageConnectionStepOutcome.Cancelled);
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
        await Task.Delay(200, TestContext.Current.CancellationToken);
        await stopping.CancelAsync();

        await start.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        logger.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning,
            "a shutdown that races the handshake is routine, and a timeout that cries wolf on every exit "
          + "is one a reader learns to ignore");
    }
}
