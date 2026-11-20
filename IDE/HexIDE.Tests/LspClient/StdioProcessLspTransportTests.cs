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
    [Fact]
    public async Task HalfCloseEndsStdinButStillReadsTheServersFinalFrame()
    {
        var token = TestContext.Current.CancellationToken;
        var server = typeof(global::HexIDE.Lsp.TestServer.Program).Assembly.Location;
        await using var transport = new StdioProcessLspTransport(
            new LspServerInfo("dotnet", "", Path.GetTempPath(), ArgumentList: [server, "eof"]),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<StdioProcessLspTransport>.Instance);
        using var handler = (HeaderDelimitedMessageHandler)(await transport.ConnectAsync(Formatter(), token))!;
        ((ILspTransport)transport).TryEndOutput().Should().BeTrue();
        var message = await handler.ReadAsync(token).AsTask().WaitAsync(TimeSpan.FromSeconds(5), token);
        message.Should().BeAssignableTo<StreamJsonRpc.Protocol.JsonRpcRequest>().Which.Method.Should().Be("probe/eof");
    }

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

    /// <summary>
    /// The same trivial command, but naming NO working directory of its own — so the workspace decides.
    /// </summary>
    /// <remarks>
    /// Every other <see cref="LspServerInfo"/> in this file passes <see cref="Path.GetTempPath"/> as the
    /// third argument, which takes the explicit branch and returns before the workspace is ever consulted.
    /// That is why the workspace-derived working directory had no coverage at all, and why #278 could sit
    /// in a file with tests in it.
    /// </remarks>
    private static LspServerInfo ATrivialCommandRootedNowhere() =>
        OperatingSystem.IsWindows()
            ? new LspServerInfo("cmd.exe", "/c exit 0", "")
            : new LspServerInfo("/bin/sh", "-c \"exit 0\"", "");

    /// <summary>A workspace that answers with whatever directory the test wants it to.</summary>
    private sealed class Workspace(string? directory) : ILspWorkspace
    {
        public string? Directory { get; } = directory;

        public IReadOnlyList<LspWorkspaceFolder> Folders { get; } = [];
    }

    /// <summary>The shape <c>ProjectService.ProjectFilesDirectory</c> mints for a project with no file yet.</summary>
    /// <remarks>
    /// A GUID suffix, so it is guaranteed not to exist: that method assigns and memoises the path without
    /// creating anything, and the four call sites that write a file into it create it at that moment.
    /// </remarks>
    private static string AnUnsavedProjectsScratchPath() =>
        Path.Combine(Path.GetTempPath(), $"hexide_Project1_{Guid.NewGuid():N}");

    public static bool OnUnix => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    [Theory(Skip = "The guard is used on Unix.", SkipUnless = nameof(OnUnix))]
    [InlineData(false, "tree-clean")]
    [InlineData(false, "tree")]
    [InlineData(true, "tree")]
    public async Task ExitAndDisposalEndDescendantsWithinTheDeadline(bool pipe, string mode)
    {
        var token = TestContext.Current.CancellationToken;
        var server = typeof(global::HexIDE.Lsp.TestServer.Program).Assembly.Location;
        var info = new LspServerInfo("dotnet", "", Path.GetTempPath(), ArgumentList: [server, mode]);
        var name = $"h4152-{Guid.NewGuid():N}"[..20];
        await using ILspTransport sut = pipe
            ? new NamedPipeLspTransport(name, NamedPipeRole.Listen,
                Substitute.For<ILogger<NamedPipeLspTransport>>(),
                new NamedPipeLaunch("dotnet", "", ArgumentList: [server, mode, name]))
            : new StdioProcessLspTransport(info, _logger);
        var childReady = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var notices = new System.Collections.Concurrent.ConcurrentQueue<TransportNotice>();
        sut.Notice += (_, notice) =>
        {
            notices.Enqueue(notice);
            if (notice.Text.StartsWith("child:", StringComparison.Ordinal))
                childReady.TrySetResult(int.Parse(notice.Text[6..]));
        };
        using var handler = (HeaderDelimitedMessageHandler)(await sut.ConnectAsync(Formatter(), token))!;
        using var rpc = new JsonRpc(handler);
        rpc.StartListening();
        using var child = System.Diagnostics.Process.GetProcessById(
            await childReady.Task.WaitAsync(TimeSpan.FromSeconds(10), token));
        child.HasExited.Should().BeFalse();
        // Assert the actual shutdown reply before timing exit through transport disposal.
        var reply = await rpc.InvokeAsync<System.Text.Json.JsonElement>("shutdown")
            .WaitAsync(TimeSpan.FromSeconds(10), token);
        reply.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        sut.ShutdownCompleted();
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        await rpc.NotifyAsync("exit");
        await sut.DisposeAsync();
        var disposed = elapsed.Elapsed;
        // 500 ms scheduling margin over the two-second total; clean exit has a tighter
        // 1.5 s ceiling so restoring the guard's fixed two-second delay fails too.
        var bound = TimeSpan.FromSeconds(mode == "tree-clean" ? 1.5 : 2.5);
        while (!child.HasExited && elapsed.Elapsed < bound)
            await Task.Delay(10, token);
        elapsed.Stop();
        // Observe the test's own read loop, which ends with the transport, after the timing above (#4833).
        try { await rpc.Completion; } catch { /* its end is expected; only leaving it unobserved is the fault */ }
        TestContext.Current.TestOutputHelper!.WriteLine(
            $"{(pipe ? "pipe" : "stdio")} {mode}: exit to disposed {disposed.TotalMilliseconds:F0} ms; child ended by {elapsed.Elapsed.TotalMilliseconds:F0} ms");
        child.HasExited.Should().BeTrue("teardown must end descendants within the same bound");
        elapsed.Elapsed.Should().BeLessThan(bound);
        if (mode == "tree-clean")
            notices.Should().Contain(n => n.Text == "process exited with code 0" && !n.Fault);
    }

    [Fact]
    public async Task DisposalWaitsForTheKilledProcessToExit()
    {
        var adapter = typeof(global::HexIDE.Dap.TestAdapter.Program).Assembly.Location;
        var info = new LspServerInfo("dotnet", $"\"{adapter}\" hang", Path.GetTempPath());
        await using var sut = new StdioProcessLspTransport(info, _logger);
        using var handler = (HeaderDelimitedMessageHandler)(await sut.ConnectAsync(Formatter(), Timeout()))!;
        // Keep an independent process handle: the transport disposes its own before returning.
        var child = (System.Diagnostics.Process)typeof(StdioProcessLspTransport)
            .GetField("_process", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(sut)!;
        await child.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken)
            .AsTask().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        using var observer = System.Diagnostics.Process.GetProcessById(child.Id);
        observer.HasExited.Should().BeFalse();

        await sut.DisposeAsync();

        // HasExited can wait on .NET's shared child wait state and hide an unreaped child.
        // On Linux, check the kernel record first, before asking .NET about exit.
        if (OperatingSystem.IsLinux())
            Directory.Exists($"/proc/{observer.Id}").Should().BeFalse(
                "a killed child must be reaped before disposal returns");
        observer.HasExited.Should().BeTrue("disposal must wait for termination, not just request a kill");
    }

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
    public async Task AWorkspaceDirectoryThatDoesNotExistYetStillStartsTheServer()
    {
        // hexide-io/HexIDE#278. An unsaved project's directory is a real string naming nothing until the
        // first file is written there, and handing that to Process.Start throws "The directory name is
        // invalid" — so every brand-new project got an IDE with no language features and no stated reason.
        // Not a first-run hazard: #260 gave the path a GUID suffix, so it is a fresh non-existent directory
        // every time.
        var scratch = AnUnsavedProjectsScratchPath();
        await using var sut = new StdioProcessLspTransport(
            ATrivialCommandRootedNowhere(), _logger, new Workspace(scratch));

        var handler = await sut.ConnectAsync(Formatter(), Timeout());

        handler.Should().NotBeNull();

        // Not decoration. The other way to make the line above pass is to create the directory before
        // launching, and that was rejected: nothing in the tree ever reaps a scratch directory, so it would
        // leave an empty GUID-named directory in TEMP for every unsaved project ever opened. Without this
        // assertion that decision is unguarded and the next reader's one-line fix silently reverses it.
        Directory.Exists(scratch).Should().BeFalse();
    }

    [Fact]
    public async Task AnExplicitWorkingDirectoryIsNotSecondGuessed()
    {
        // The other side of #278, and the reason the existence check lives only on the workspace branch. A
        // directory somebody named in configuration is their intent: if it is wrong they need to be told,
        // not quietly given the IDE's own directory and a server answering about the wrong tree. So this
        // one is expected to FAIL to launch.
        var named = Path.Combine(Path.GetTempPath(), $"no-such-cwd-{Guid.NewGuid():N}");
        var info = OperatingSystem.IsWindows()
            ? new LspServerInfo("cmd.exe", "/c exit 0", named)
            : new LspServerInfo("/bin/sh", "-c \"exit 0\"", named);
        await using var sut = new StdioProcessLspTransport(info, _logger, new Workspace(Path.GetTempPath()));

        var handler = await sut.ConnectAsync(Formatter(), Timeout());

        handler.Should().BeNull();
        // The directory is the whole fault and the OS message does not name it on Windows, so the
        // transport has to. Without this the report reads as though the executable were the problem.
        sut.LastFailure.Should().Contain(named);
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
