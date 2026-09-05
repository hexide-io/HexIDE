using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// Contract tests for the stdio transport, driven against <b>real</b> child processes.
///
/// <para>
/// This transport had no tests of its own. It was exercised only through
/// <see cref="ForeignServerIntegrationTests"/>, which skip when no foreign server is installed — so on CI,
/// where none is, the transport the IDE actually ships had zero coverage.
/// </para>
///
/// <para>
/// It matters more now than it did. The transport used to ask <c>ILspServerLocator</c> where "the" server
/// was, so the only command it could ever launch was one HexIDE itself had placed. It now launches what it
/// is given, which makes <b>a command that does not work</b> an ordinary user mistake rather than an
/// impossible state (hexide-io/HexIDE#255).
/// </para>
/// </summary>
public class StdioProcessLspTransportTests
{
    private readonly ILogger<StdioProcessLspTransport> _logger =
        Substitute.For<ILogger<StdioProcessLspTransport>>();

    private static IJsonRpcMessageFormatter Formatter() => new SystemTextJsonFormatter();

    // Bounds the test's own side of every launch. Without it, a regression that makes ConnectAsync hang
    // would hang the run rather than fail it — and a hung CI job reads as infrastructure trouble.
    private static CancellationToken Timeout() => new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;

    /// <summary>A command that exists and exits immediately, on whichever OS is running the test.</summary>
    private static LspServerInfo ATrivialCommand() =>
        OperatingSystem.IsWindows()
            ? new LspServerInfo("cmd.exe", "/c exit 0", Path.GetTempPath())
            : new LspServerInfo("/bin/sh", "-c \"exit 0\"", Path.GetTempPath());

    [Fact]
    public async Task TheTransportLaunchesTheCommandItWasGiven()
    {
        // The point of the change, stated positively: no locator is consulted and none is available — the
        // command comes from the caller. Asserting a handler comes back is asserting a process was started,
        // since the handler is built over that process's own stdin/stdout streams.
        await using var sut = new StdioProcessLspTransport(ATrivialCommand(), _logger);

        var handler = await sut.ConnectAsync(Formatter(), Timeout());

        handler.Should().NotBeNull();
    }

    [Fact]
    public async Task ACommandThatDoesNotExistYieldsNoTransportRatherThanThrowing()
    {
        // THE new failure mode. Before, the command was always one HexIDE had located, so this could only
        // happen to a broken install; now it is what a typo in a config file looks like. It has to degrade
        // — language features absent — rather than take down whatever started the server.
        var missing = new LspServerInfo(
            Path.Combine(Path.GetTempPath(), $"no-such-server-{Guid.NewGuid():N}"), "", Path.GetTempPath());
        await using var sut = new StdioProcessLspTransport(missing, _logger);

        var handler = await sut.ConnectAsync(Formatter(), Timeout());

        handler.Should().BeNull();
    }

    [Fact]
    public async Task AFailedLaunchLeavesNothingThatThrowsWhenPolled()
    {
        // The trap the transport's own comment names: a Process object that was constructed but never
        // started throws on HasExited, so IsAlive would throw on every poll rather than answering false.
        // The registry polls IsAlive to decide whether to route, so this would surface as an exception from
        // opening a document — nowhere near the server that failed to start.
        var missing = new LspServerInfo(
            Path.Combine(Path.GetTempPath(), $"no-such-server-{Guid.NewGuid():N}"), "", Path.GetTempPath());
        await using var sut = new StdioProcessLspTransport(missing, _logger);

        await sut.ConnectAsync(Formatter(), Timeout());

        sut.IsAlive.Should().BeFalse();
        sut.Invoking(t => _ = t.IsAlive).Should().NotThrow();
    }

    [Fact]
    public async Task DisposingAfterAFailedLaunchIsSafe()
    {
        var missing = new LspServerInfo(
            Path.Combine(Path.GetTempPath(), $"no-such-server-{Guid.NewGuid():N}"), "", Path.GetTempPath());
        var sut = new StdioProcessLspTransport(missing, _logger);
        await sut.ConnectAsync(Formatter(), Timeout());

        await sut.Invoking(t => t.DisposeAsync().AsTask()).Should().NotThrowAsync();
    }

    [Fact]
    public async Task DisposingWithoutEverConnectingIsSafe()
    {
        // A registration whose server is never reached — nothing claimed its language — is disposed on
        // shutdown having never connected.
        var sut = new StdioProcessLspTransport(ATrivialCommand(), _logger);

        await sut.Invoking(t => t.DisposeAsync().AsTask()).Should().NotThrowAsync();
    }

    [Fact]
    public void ASpawnedSubprocessDoesNotAdvertiseReconnection()
    {
        // A crashed child is not re-spawned within the session; the registry must not retry one. Stated
        // here because it is a contract the registry reads, not an implementation detail.
        var sut = new StdioProcessLspTransport(ATrivialCommand(), _logger);

        sut.CanReconnect.Should().BeFalse();
    }
}
