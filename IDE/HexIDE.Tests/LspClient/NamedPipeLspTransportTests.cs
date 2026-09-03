using System.IO.Pipes;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// Contract tests for the named-pipe transport, driven over <b>real</b> named pipes rather than a
/// substitute. A transport is the one component whose whole job is the channel, so a mocked channel
/// would assert nothing that matters — every bug worth catching here (wrong role, wrong framing,
/// a hang instead of a failure) only exists at the OS boundary.
/// </summary>
public class NamedPipeLspTransportTests
{
    private readonly ILogger<NamedPipeLspTransport> _logger = Substitute.For<ILogger<NamedPipeLspTransport>>();

    // A fresh name per test: pipe names are a machine-global namespace, so a fixed one would make
    // these tests fail each other under parallel execution.
    private static string UniquePipeName() => $"hexide-test-{Guid.NewGuid():N}";

    private static IJsonRpcMessageFormatter Formatter() => new SystemTextJsonFormatter();

    // Bounds the test's own side of every pipe operation. Without it a regression that makes the
    // transport never connect would hang the run rather than fail it — and a hung CI job reads as
    // infrastructure trouble, not as the bug it is.
    private static CancellationToken Timeout() => new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;

    private NamedPipeLspTransport CreateSut(
        string pipeName, NamedPipeRole role, TimeSpan? timeout = null, NamedPipeLaunch? launch = null)
        => new(pipeName, role, _logger, launch, timeout ?? TimeSpan.FromSeconds(10));

    [Fact]
    public async Task InListenRole_TheTransportCreatesThePipeAndAServerDialsIn()
    {
        // The vscode `--pipe` convention: the editor owns the endpoint, the server connects to it.
        var pipeName = UniquePipeName();
        await using var sut = CreateSut(pipeName, NamedPipeRole.Listen);

        var connecting = sut.ConnectAsync(Formatter());

        await using var server = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await server.ConnectAsync(Timeout());

        var handler = await connecting;

        handler.Should().NotBeNull("the transport must hand back a bound message handler once the server dials in");
        sut.IsAlive.Should().BeTrue();
    }

    [Fact]
    public async Task InConnectRole_TheTransportDialsAServerThatAlreadyOwnsThePipe()
    {
        var pipeName = UniquePipeName();
        await using var server = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var accepting = server.WaitForConnectionAsync(Timeout());

        await using var sut = CreateSut(pipeName, NamedPipeRole.Connect);
        var handler = await sut.ConnectAsync(Formatter());
        await accepting;

        handler.Should().NotBeNull();
        sut.IsAlive.Should().BeTrue();
    }

    [Fact]
    public async Task AJsonRpcCallRoundTripsOverTheConnectedPipe()
    {
        // The framing assertion. Content-Length headers are what the other end expects, and nothing
        // above this class would notice a framing mismatch until a real server hung mid-handshake.
        var pipeName = UniquePipeName();
        await using var sut = CreateSut(pipeName, NamedPipeRole.Listen);

        var connecting = sut.ConnectAsync(Formatter());

        await using var serverPipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await serverPipe.ConnectAsync(Timeout());

        using var serverRpc = new JsonRpc(new HeaderDelimitedMessageHandler(serverPipe, serverPipe, Formatter()), new EchoServer());
        serverRpc.StartListening();

        var handler = await connecting;
        using var clientRpc = new JsonRpc(handler!);
        clientRpc.StartListening();

        var reply = await clientRpc.InvokeAsync<string>("echo", "ping");

        reply.Should().Be("ping");
    }

    [Fact]
    public async Task WhenNothingIsListening_ItTimesOutToNullRatherThanHanging()
    {
        // The failure this transport exists to make visible. Picking the wrong role does not fail
        // loudly — both ends simply wait — so an unbounded connect would be an invisible permanent
        // degradation, which is exactly the shape of #231 on the handshake.
        await using var sut = CreateSut(UniquePipeName(), NamedPipeRole.Connect, TimeSpan.FromMilliseconds(250));

        var connect = async () => await sut.ConnectAsync(Formatter());

        (await connect.Should().NotThrowAsync()).Which.Should().BeNull();
        sut.IsAlive.Should().BeFalse();
    }

    [Fact]
    public async Task ACallerCancellationIsHonouredAndStillDegradesGracefully()
    {
        await using var sut = CreateSut(UniquePipeName(), NamedPipeRole.Connect, TimeSpan.FromMinutes(5));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var handler = await sut.ConnectAsync(Formatter(), cts.Token);

        handler.Should().BeNull("a cancelled start disables LSP rather than failing IDE startup");
    }

    [Fact]
    public async Task ATransportThatOwnsTheServerProcessDoesNotAutoReconnect()
    {
        // Matches StdioProcessLspTransport: a spawned child is one-shot. A pre-existing endpoint we
        // merely dialled can be re-dialled, because something else owns its lifetime.
        var launch = new NamedPipeLaunch("does-not-matter", "--pipe {pipe}");
        await using var owns = CreateSut(UniquePipeName(), NamedPipeRole.Listen, launch: launch);
        await using var dials = CreateSut(UniquePipeName(), NamedPipeRole.Connect);

        owns.CanReconnect.Should().BeFalse();
        dials.CanReconnect.Should().BeTrue();
    }

    [Fact]
    public async Task AnUnstartableServerDegradesToNullInsteadOfThrowing()
    {
        var launch = new NamedPipeLaunch(
            Path.Combine(Path.GetTempPath(), $"hexide-no-such-exe-{Guid.NewGuid():N}"), "--pipe {pipe}");
        await using var sut = CreateSut(UniquePipeName(), NamedPipeRole.Listen, TimeSpan.FromMilliseconds(250), launch);

        var connect = async () => await sut.ConnectAsync(Formatter());

        (await connect.Should().NotThrowAsync()).Which.Should().BeNull();
    }

    private sealed class EchoServer
    {
        [JsonRpcMethod("echo")]
        public static string Echo(string value) => value;
    }
}
