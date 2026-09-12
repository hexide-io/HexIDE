using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

/// <summary>
/// The stdio transport is the only one that owns a process, and these drive a real one.
/// </summary>
/// <remarks>
/// <b>A fake transport cannot prove this and a fake process barely can.</b> The claim under test is that
/// <see cref="StdioProcessLspTransport.Unobservable"/> returning null is true — that the server's start,
/// its standard error and its exit code really are HexIDE's to observe. Every part of that runs through
/// <see cref="System.Diagnostics.Process"/>: <c>BeginErrorReadLine</c>'s line splitting, <c>Exited</c>
/// firing on a pool thread, and <c>ExitCode</c> being readable at the moment it is asked for. Substituting
/// any of it would test the substitute.
///
/// <para>
/// So the "server" here is the platform shell, told to write to standard error and exit with a code
/// nobody could produce by accident. It speaks no LSP, which does not matter: nothing below the transport
/// is under test, and a server that has just crashed does not speak LSP either — that is the case these
/// exist for.
/// </para>
///
/// <para>
/// The exit code carries meaning in this protocol: LSP has a server exit 0 when a shutdown preceded exit
/// and 1 otherwise, which is how hexide-io/HexIDE#312 was found by reading one. Nothing in the tree read
/// an exit code until the record needed it.
/// </para>
/// </remarks>
public class StdioProcessNoticeTests
{
    private const string First = "hexide-stderr-one";
    private const string Second = "hexide-stderr-two";

    /// <summary>
    /// A shell that writes two lines to standard error and exits with <paramref name="code"/>.
    /// </summary>
    /// <remarks>
    /// Written out per platform rather than shared, because the two shells differ in the separator (<c>&amp;</c>
    /// against <c>;</c>) and in how the redirect must be placed: <c>echo x 1&gt;&amp;2</c> is the form everyone
    /// writes and it is the one that leaves a trailing space on cmd, which would make a verbatim assertion
    /// fail for a reason that has nothing to do with the transport.
    /// </remarks>
    private static LspServerInfo WritesToStderrThenExits(int code) =>
        OperatingSystem.IsWindows()
            ? new LspServerInfo(
                "cmd.exe", $"/c \" >&2 echo {First}& >&2 echo {Second}& exit {code}\"", "")
            : new LspServerInfo(
                "/bin/sh", $"-c \">&2 echo {First}; >&2 echo {Second}; exit {code}\"", "");

    private static StdioProcessLspTransport Transport(LspServerInfo info) =>
        new(info, Substitute.For<ILogger<StdioProcessLspTransport>>());

    [Fact]
    public async Task EveryLineTheServerWroteToStandardErrorReachesTheNoticeChannel()
    {
        // Before this, standard error went to a debug log line and nowhere else — which is not somewhere
        // anybody looks when a server dies mid-conversation.
        await using var transport = Transport(WritesToStderrThenExits(0));

        var seen = new List<TransportNotice>();
        var gate = new Lock();
        transport.Notice += (_, n) => { lock (gate) seen.Add(n); };

        var handler = await transport.ConnectAsync(
            new SystemTextJsonFormatter(), TestContext.Current.CancellationToken);
        handler.Should().NotBeNull("the shell started, whatever it then said");

        await WaitFor(() => Count(seen, gate, TransportNoticeKind.StandardError) >= 2);

        List<string> lines;
        lock (gate)
            lines = seen.Where(n => n.Kind == TransportNoticeKind.StandardError).Select(n => n.Text).ToList();

        // The collection overload, explicitly: Equal(params string[]) would take the reason below as a
        // third expected line, which is a way to fail that says nothing about the transport.
        lines.Should().Equal(new[] { First, Second },
            "each line is its own notice, in the order the server wrote them, and verbatim — "
            + "a stack trace split across lines is the case this is for");
    }

    [Fact]
    public async Task TheExitCodeIsReadAndReported()
    {
        // LSP gives this number a meaning, and until now nothing in the tree read it.
        await using var transport = Transport(WritesToStderrThenExits(3));

        var seen = new List<TransportNotice>();
        var gate = new Lock();
        transport.Notice += (_, n) => { lock (gate) seen.Add(n); };

        await transport.ConnectAsync(new SystemTextJsonFormatter(), TestContext.Current.CancellationToken);
        await WaitFor(() => Count(seen, gate, TransportNoticeKind.Lifecycle) >= 1);

        TransportNotice exit;
        lock (gate) exit = seen.Last(n => n.Kind == TransportNoticeKind.Lifecycle);

        exit.Text.Should().Be("process exited with code 3",
            "the number is the finding; 'the process exited' is what the reader already knew");
    }

    [Fact]
    public async Task TheExitCodeArrivesBeforeTheConnectionIsTornDown()
    {
        // Closed is what tears the connection down, and a record that has stopped accepting entries cannot
        // hold the one fact the reader came for. This ordering is the whole reason the notice is raised
        // inside OnProcessExited rather than alongside it.
        await using var transport = Transport(WritesToStderrThenExits(1));

        var order = new List<string>();
        var gate = new Lock();
        var closed = new TaskCompletionSource();

        transport.Notice += (_, n) =>
        {
            if (n.Kind != TransportNoticeKind.Lifecycle) return;
            lock (gate) order.Add("exit-code");
        };
        transport.Closed += (_, _) =>
        {
            lock (gate) order.Add("closed");
            closed.TrySetResult();
        };

        await transport.ConnectAsync(new SystemTextJsonFormatter(), TestContext.Current.CancellationToken);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        lock (gate) order.Should().Equal("exit-code", "closed");
    }

    [Fact]
    public async Task AProcessThatNeverStartedReportsNothingRatherThanGuessing()
    {
        // The failure path in ConnectAsync tears the Process down and nulls it, so there is no exit code to
        // read and no stderr to have heard. LastFailure is the honest answer there, and it is a different
        // channel on purpose: a notice would put "process exited with code 0" on a record for a process
        // that never ran, which is worse than the silence it replaced.
        await using var transport = Transport(
            new LspServerInfo("hexide-no-such-executable-9f3c", "", ""));

        var seen = new List<TransportNotice>();
        var gate = new Lock();
        transport.Notice += (_, n) => { lock (gate) seen.Add(n); };

        var handler = await transport.ConnectAsync(
            new SystemTextJsonFormatter(), TestContext.Current.CancellationToken);

        handler.Should().BeNull("nothing launched, so there is no transport to hand back");
        transport.LastFailure.Should().Contain("hexide-no-such-executable-9f3c");
        lock (gate) seen.Should().BeEmpty();
    }

    private static int Count(List<TransportNotice> seen, Lock gate, TransportNoticeKind kind)
    {
        lock (gate) return seen.Count(n => n.Kind == kind);
    }

    /// <summary>
    /// Polls until the condition holds. Both channels are asynchronous by construction — stderr is pumped
    /// by <c>BeginErrorReadLine</c> and <c>Exited</c> is raised on a pool thread — and they are not ordered
    /// against each other, so waiting on one says nothing about the other.
    /// </summary>
    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(25, TestContext.Current.CancellationToken);

        condition().Should().BeTrue("the process was expected to report within 30 seconds");
    }
}
