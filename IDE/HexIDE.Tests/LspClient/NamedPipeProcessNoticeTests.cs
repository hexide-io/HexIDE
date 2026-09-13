using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

/// <summary>
/// A pipe server HexIDE launched is HexIDE's to observe, and these drive a real process to prove it.
/// </summary>
/// <remarks>
/// <b>The sibling of <see cref="StdioProcessNoticeTests"/>, and it exists because that work stopped one
/// transport short.</b> This transport can launch a server too — that is what <see cref="NamedPipeLaunch"/>
/// and the <c>command</c> field of an <c>lsp-servers.json</c> pipe entry are for — and until
/// hexide-io/HexIDE#403 it redirected no standard error, never read an exit code, and answered
/// <see cref="NamedPipeLspTransport.Unobservable"/> with null: the record asserting nothing was missing
/// while both halves were.
///
/// <para>
/// <b>A pipe makes that worse than stdio did, which is the reason these are not just a copy.</b> A stdio
/// server that dies leaves a broken stream to attribute. A pipe is dialled, so a server that dies before
/// its pipe exists presents as a bare connect timeout: no stream, no partial conversation, and — before
/// this — nothing else. The last test here is that case, and it is the one worth having.
/// </para>
/// </remarks>
public class NamedPipeProcessNoticeTests
{
    private const string First = "hexide-pipe-stderr-one";
    private const string Second = "hexide-pipe-stderr-two";

    /// <summary>
    /// A shell that writes two lines to standard error and exits, never creating a pipe.
    /// </summary>
    /// <remarks>
    /// Not creating the pipe is the point rather than a shortcut: it is what a server that fails a startup
    /// precondition does, and it is the shape this transport handled worst.
    /// </remarks>
    private static NamedPipeLaunch WritesToStderrThenExits(int code) =>
        OperatingSystem.IsWindows()
            ? new NamedPipeLaunch(
                "cmd.exe", $"/c \" >&2 echo {First}& >&2 echo {Second}& exit {code}\"")
            : new NamedPipeLaunch(
                "/bin/sh", $"-c \">&2 echo {First}; >&2 echo {Second}; exit {code}\"");

    /// <summary>A distinct pipe name per test, so a stray server from one cannot answer another.</summary>
    private static string PipeName([System.Runtime.CompilerServices.CallerMemberName] string caller = "") =>
        $"hexide-test-{caller}-{Environment.ProcessId}";

    private static NamedPipeLspTransport Transport(NamedPipeLaunch launch, string pipeName) =>
        new(pipeName,
            NamedPipeRole.Connect,
            Substitute.For<ILogger<NamedPipeLspTransport>>(),
            launch,
            // The server here never connects, so every test would otherwise pay the 30-second default.
            // The connect outcome is not what is under test; what the record holds about the process is.
            connectTimeout: TimeSpan.FromSeconds(3));

    [Fact]
    public async Task EveryLineTheServerWroteToStandardErrorReachesTheNoticeChannel()
    {
        // Before this the ProcessStartInfo did not set RedirectStandardError at all, so these lines were
        // not merely unrecorded — they were unreadable.
        await using var transport = Transport(WritesToStderrThenExits(0), PipeName());

        var seen = new List<TransportNotice>();
        var gate = new Lock();
        transport.Notice += (_, n) => { lock (gate) seen.Add(n); };

        await transport.ConnectAsync(
            new SystemTextJsonFormatter(), TestContext.Current.CancellationToken);

        await WaitFor(() => Count(seen, gate, TransportNoticeKind.StandardError) >= 2);

        List<string> lines;
        lock (gate)
            lines = seen.Where(n => n.Kind == TransportNoticeKind.StandardError).Select(n => n.Text).ToList();

        lines.Should().Equal(new[] { First, Second },
            "each line is its own notice, in order and verbatim — a stack trace is the case this is for");
    }

    [Fact]
    public async Task TheExitCodeIsReadAndReported()
    {
        // OnServerExited was `=> Closed?.Invoke(...)`. The number was never asked for.
        await using var transport = Transport(WritesToStderrThenExits(3), PipeName());

        var seen = new List<TransportNotice>();
        var gate = new Lock();
        transport.Notice += (_, n) => { lock (gate) seen.Add(n); };

        await transport.ConnectAsync(
            new SystemTextJsonFormatter(), TestContext.Current.CancellationToken);
        await WaitFor(() => Count(seen, gate, TransportNoticeKind.Lifecycle) >= 1);

        TransportNotice exit;
        lock (gate) exit = seen.Last(n => n.Kind == TransportNoticeKind.Lifecycle);

        exit.Text.Should().Be("process exited with code 3");
    }

    [Fact]
    public async Task TheExitCodeArrivesBeforeTheConnectionIsTornDown()
    {
        // Closed is what tears the connection down, and a record that has stopped accepting entries cannot
        // hold the one fact the reader came for.
        await using var transport = Transport(WritesToStderrThenExits(1), PipeName());

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

        await transport.ConnectAsync(
            new SystemTextJsonFormatter(), TestContext.Current.CancellationToken);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        lock (gate) order.Should().Equal("exit-code", "closed");
    }

    [Fact]
    public async Task AServerThatDiesBeforeItsPipeExistsStillExplainsItself()
    {
        // THE CASE THIS TRANSPORT HANDLED WORST, and the reason these tests are not a copy of the stdio
        // ones. The channel is dialled rather than inherited, so a server that never gets as far as
        // creating its pipe leaves the client with a connect timeout and nothing to attribute it to.
        //
        // ConnectAsync returns null here — correctly, there is no transport — and the record's only
        // account of why is what came through Notice. Note the client reads Unobservable only after a
        // SUCCESSFUL connect, so on this path Notice is not merely the better channel, it is the only one.
        await using var transport = Transport(WritesToStderrThenExits(9), PipeName());

        var seen = new List<TransportNotice>();
        var gate = new Lock();
        transport.Notice += (_, n) => { lock (gate) seen.Add(n); };

        var handler = await transport.ConnectAsync(
            new SystemTextJsonFormatter(), TestContext.Current.CancellationToken);

        handler.Should().BeNull("nothing ever created the pipe, so there is no channel to hand back");
        transport.LastFailure.Should().NotBeNullOrEmpty("the connect failure is reported in its own words");

        await WaitFor(() =>
            Count(seen, gate, TransportNoticeKind.StandardError) >= 2 &&
            Count(seen, gate, TransportNoticeKind.Lifecycle) >= 1);

        List<TransportNotice> notices;
        lock (gate) notices = [.. seen];

        notices.Should().Contain(n => n.Kind == TransportNoticeKind.StandardError && n.Text == First,
            "the server's own words are the whole account of a startup failure");
        notices.Should().Contain(n => n.Kind == TransportNoticeKind.Lifecycle && n.Text.Contains("code 9"),
            "and the exit code says it failed rather than finished");
    }

    [Fact]
    public async Task APipeMerelyDialledSaysWhatItCannotSee()
    {
        // The other half of the same honesty: with no launch there is genuinely no process here, so
        // Unobservable must say so rather than answering null. Null is the claim that nothing is missing,
        // and it is only for the case where that is true.
        await using var transport = new NamedPipeLspTransport(
            PipeName(),
            NamedPipeRole.Connect,
            Substitute.For<ILogger<NamedPipeLspTransport>>(),
            launch: null,
            connectTimeout: TimeSpan.FromSeconds(1));

        var seen = new List<TransportNotice>();
        var gate = new Lock();
        transport.Notice += (_, n) => { lock (gate) seen.Add(n); };

        await transport.ConnectAsync(
            new SystemTextJsonFormatter(), TestContext.Current.CancellationToken);

        transport.Unobservable.Should().NotBeNullOrEmpty()
            .And.Subject.Should().Contain("already running",
                "somebody else started it, so its exit code is not ours to report");

        lock (gate) seen.Should().BeEmpty("there is no process on this side to report on");
    }

    private static int Count(List<TransportNotice> seen, Lock gate, TransportNoticeKind kind)
    {
        lock (gate) return seen.Count(n => n.Kind == kind);
    }

    /// <summary>
    /// Polls until the condition holds. Standard error is pumped by <c>BeginErrorReadLine</c> and
    /// <c>Exited</c> is raised on a pool thread; the two are not ordered against each other, so waiting
    /// on one says nothing about the other.
    /// </summary>
    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(25, TestContext.Current.CancellationToken);

        condition().Should().BeTrue("the process was expected to report within 30 seconds");
    }
}
