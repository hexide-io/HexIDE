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

    /// <summary>
    /// A distinct pipe name per test, so a stray server from one cannot answer another.
    ///
    /// <para>
    /// The caller's name is HASHED rather than spelled. It used to be interpolated whole, so the name was
    /// as long as the test method — <c>AServerThatDiesBeforeItsPipeExistsStillExplainsItself</c> alone
    /// makes 70 characters, against macOS's budget of 43, and every test here threw before asserting
    /// anything the first time this suite ran on a Mac (#694). A hash keeps the per-test distinctness that
    /// mattered and costs the readability of a name only a stack trace ever shows.
    /// </para>
    /// <para>
    /// SHA-256 rather than <c>string.GetHashCode</c>, which is randomised per process on .NET Core.
    /// Truncating the method name would not do either: three of these begin <c>TheExitC</c>.
    /// </para>
    /// </summary>
    private static string PipeName([System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        var digest = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(caller));
        return $"hexide-{Convert.ToHexString(digest)[..8].ToLowerInvariant()}-{Environment.ProcessId}";
    }

    private static NamedPipeLspTransport Transport(NamedPipeLaunch launch, string pipeName) =>
        new(pipeName,
            NamedPipeRole.Connect,
            Substitute.For<ILogger<NamedPipeLspTransport>>(),
            launch,
            // The server here never connects, so every test would otherwise pay the 30-second default.
            // The connect outcome is not what is under test; what the record holds about the process is.
            connectTimeout: TimeSpan.FromSeconds(3));

    [Fact]
    public async Task StandardOutputIsReadEvenWhenTheServerNeverCreatesItsPipe()
    {
        var launch = OperatingSystem.IsWindows()
            ? new NamedPipeLaunch("cmd.exe", "/c \"echo stdout-one& echo stdout-two& exit 9\"")
            : new NamedPipeLaunch("/bin/sh", "-c \"echo stdout-one; echo stdout-two; exit 9\"");
        await using var transport = Transport(launch, PipeName());
        var seen = new List<TransportNotice>();
        var gate = new Lock();
        var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.Notice += (_, n) =>
        {
            lock (gate)
            {
                seen.Add(n);
                if (seen.Count(notice => notice.Kind == TransportNoticeKind.StandardOutput) >= 2) reported.TrySetResult();
            }
        };

        var handler = await transport.ConnectAsync(new SystemTextJsonFormatter(), TestContext.Current.CancellationToken);
        handler.Should().BeNull();
        await ProcessNoticeWait.ForAsync(reported.Task,
            "two standard-output lines from the server that never creates its pipe", TestContext.Current.CancellationToken,
            received: () => ProcessNoticeWait.Describe(seen, gate));
        lock (gate)
        {
            seen.Where(n => n.Kind == TransportNoticeKind.StandardOutput).Select(n => n.Text)
                .Should().Equal("stdout-one", "stdout-two");
            seen.Should().NotContain(n => n.Kind == TransportNoticeKind.StandardError);
        }
    }

    [Fact]
    public async Task EveryLineTheServerWroteToStandardErrorReachesTheNoticeChannel()
    {
        // Before this the ProcessStartInfo did not set RedirectStandardError at all, so these lines were
        // not merely unrecorded — they were unreadable.
        await using var transport = Transport(WritesToStderrThenExits(0), PipeName());

        var seen = new List<TransportNotice>();
        var gate = new Lock();
        var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.Notice += (_, n) =>
        {
            lock (gate)
            {
                seen.Add(n);
                if (seen.Count(notice => notice.Kind == TransportNoticeKind.StandardError) >= 2) reported.TrySetResult();
            }
        };

        await transport.ConnectAsync(
            new SystemTextJsonFormatter(), TestContext.Current.CancellationToken);

        await ProcessNoticeWait.ForAsync(reported.Task,
            "two standard-error lines from the launched pipe server", TestContext.Current.CancellationToken,
            received: () => ProcessNoticeWait.Describe(seen, gate));

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
        var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.Notice += (_, n) =>
        {
            lock (gate)
            {
                seen.Add(n);
                if (seen.Count(notice => notice.Kind == TransportNoticeKind.Lifecycle) >= 1) reported.TrySetResult();
            }
        };

        await transport.ConnectAsync(
            new SystemTextJsonFormatter(), TestContext.Current.CancellationToken);
        await ProcessNoticeWait.ForAsync(reported.Task,
            "the launched pipe server exit-code notice", TestContext.Current.CancellationToken,
            received: () => ProcessNoticeWait.Describe(seen, gate));

        TransportNotice exit;
        lock (gate) exit = seen.Last(n => n.Kind == TransportNoticeKind.Lifecycle);

        exit.Text.Should().Be("process exited with code 3");
        exit.Fault.Should().BeTrue("a non-zero exit is a failure (#945)");
    }

    [Fact]
    public async Task TheExitCodeArrivesBeforeTheConnectionIsTornDown()
    {
        // Closed is what tears the connection down, and a record that has stopped accepting entries cannot
        // hold the one fact the reader came for.
        await using var transport = Transport(WritesToStderrThenExits(1), PipeName());

        var order = new List<string>();
        var gate = new Lock();
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

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
        await ProcessNoticeWait.ForAsync(closed.Task, "the launched pipe server Closed event",
            TestContext.Current.CancellationToken);

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
        var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.Notice += (_, n) =>
        {
            lock (gate)
            {
                seen.Add(n);
                if (seen.Count(notice => notice.Kind == TransportNoticeKind.StandardError) >= 2 &&
                    seen.Count(notice => notice.Kind == TransportNoticeKind.Lifecycle) >= 1)
                    reported.TrySetResult();
            }
        };

        var handler = await transport.ConnectAsync(
            new SystemTextJsonFormatter(), TestContext.Current.CancellationToken);

        handler.Should().BeNull("nothing ever created the pipe, so there is no channel to hand back");
        transport.LastFailure.Should().NotBeNullOrEmpty("the connect failure is reported in its own words");

        await ProcessNoticeWait.ForAsync(reported.Task,
            "two standard-error lines and the exit-code notice from the server that never creates its pipe",
            TestContext.Current.CancellationToken, received: () => ProcessNoticeWait.Describe(seen, gate));

        List<TransportNotice> notices;
        lock (gate) notices = [.. seen];

        notices.Should().Contain(n => n.Kind == TransportNoticeKind.StandardError && n.Text == First,
            "the server's own words are the whole account of a startup failure");
        notices.Should().Contain(n => n.Kind == TransportNoticeKind.Lifecycle && n.Text.Contains("code 9"),
            "and the exit code says it failed rather than finished");
    }

    [Fact]
    public async Task AServerThatExitsWhileTeardownWaitsStillReportsItsExitCode()
    {
        // Teardown unsubscribed Exited and then gave the server a grace period to exit in, so a server that took
        // that grace, by exiting just after the connect attempt gave up, was reported by nothing. Every test above
        // depended on its server beating a 3 s connect timeout, and twice one did not (#2685). This server exits
        // half a second after the test starts teardown: after Exited is unsubscribed, and well inside the grace.
        var directory = Path.Combine(Path.GetTempPath(), $"hexide-2685-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var ready = Path.Combine(directory, "ready");
            var gate = Path.Combine(directory, "gate");
            var launch = new NamedPipeLaunch("dotnet", "", ArgumentList:
            [
                Path.Join(AppContext.BaseDirectory, "LspTestHost", "HexIDE.Lsp.TestHost.dll"), "teardown-probe",
                ready, gate, Path.Combine(directory, "marker"), "500", "3",
            ]);
            await using var transport = new NamedPipeLspTransport(PipeName(), NamedPipeRole.Connect,
                Substitute.For<ILogger<NamedPipeLspTransport>>(), launch, connectTimeout: TimeSpan.FromMinutes(2));
            var seen = new List<TransportNotice>();
            var lockSeen = new Lock();
            transport.Notice += (_, n) => { lock (lockSeen) seen.Add(n); };

            using var teardown = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var connecting = transport.ConnectAsync(new SystemTextJsonFormatter(), teardown.Token);
            var started = System.Diagnostics.Stopwatch.StartNew();
            while (!File.Exists(ready) && !connecting.IsCompleted && started.Elapsed < TimeSpan.FromMinutes(1))
                await Task.Delay(20, TestContext.Current.CancellationToken);
            File.Exists(ready).Should().BeTrue("the server must be running before teardown starts");

            await teardown.CancelAsync();
            await File.WriteAllTextAsync(gate, "teardown", TestContext.Current.CancellationToken);
            (await connecting).Should().BeNull("the connect attempt was abandoned");

            // ConnectAsync returns only once teardown has finished, so the report has been made if it is ever made.
            File.Exists(Path.Combine(directory, "marker")).Should().BeTrue("the server exited by itself, inside the grace");
            List<TransportNotice> exits;
            lock (lockSeen) exits = seen.Where(n => n.Kind == TransportNoticeKind.Lifecycle).ToList();
            exits.Should().ContainSingle("a server's end is reported once, and teardown must not drop it")
                .Which.Text.Should().Be("process exited with code 3");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
}
