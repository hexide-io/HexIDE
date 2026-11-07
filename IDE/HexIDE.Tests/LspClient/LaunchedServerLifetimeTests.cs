using System.Diagnostics;
using System.IO;

namespace HexIDE.Tests.LspClient;

public class LaunchedServerLifetimeTests
{
    [Theory]
    [InlineData("stdio")]
    [InlineData("pipe")]
    public Task Helper_starts_and_reports_ready(string transport) => RunHelper(transport, killOwner: false);

    [Theory]
    [InlineData("stdio")]
    [InlineData("pipe")]
    public Task Killing_the_owner_without_disposal_ends_the_launched_server_and_its_child(string transport) =>
        RunHelper(transport, killOwner: true);

    public static bool OnUnix => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    [Theory(Skip = "The guard is used on Unix.", SkipUnless = nameof(OnUnix))]
    [InlineData("child")]
    [InlineData("terminable-child")]
    public async Task Clean_server_exit_ends_its_child_and_preserves_exit_code(string childMode)
    {
        var ready = Path.Join(Path.GetTempPath(), $"hexide-2297-{Guid.NewGuid():N}");
        var start = GuardStart("clean-server", childMode, ready);
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        using var guard = Process.Start(start)!;
        Process? child = null;
        try
        {
            var pid = await guard.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            child = Process.GetProcessById(int.Parse(pid!));
            child.HasExited.Should().BeFalse();
            await guard.StandardInput.WriteLineAsync();
            await guard.WaitForExitAsync(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            guard.ExitCode.Should().Be(37);
            child.WaitForExit(10000).Should().BeTrue("a clean server exit must also end its descendants");
        }
        finally
        {
            foreach (var process in new[] { guard, child })
            {
                if (process is null) continue;
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
            child?.Dispose();
            File.Delete(ready);
        }
    }

    [Theory(Skip = "The guard is used on Unix.", SkipUnless = nameof(OnUnix))]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(37)]
    public async Task Guard_preserves_server_exit_code(int code)
    {
        using var guard = Process.Start(GuardStart("exit", code.ToString()))!;
        await guard.WaitForExitAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        guard.ExitCode.Should().Be(code);
    }

    [Fact(Skip = "The guard is used on Unix.", SkipUnless = nameof(OnUnix))]
    public async Task Guard_preserves_stdio_bytes_and_eof()
    {
        var start = GuardStart("echo");
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        using var guard = Process.Start(start)!;
        var frame = System.Text.Encoding.UTF8.GetBytes("Content-Length: 15\r\n\r\n{\"jsonrpc\":\"2\"}");
        await guard.StandardInput.BaseStream.WriteAsync(frame, TestContext.Current.CancellationToken);
        guard.StandardInput.Close();
        using var received = new MemoryStream();
        await guard.StandardOutput.BaseStream.CopyToAsync(received, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await guard.WaitForExitAsync(TestContext.Current.CancellationToken);
        received.ToArray().Should().Equal(frame);
        guard.ExitCode.Should().Be(0);
    }

    private static ProcessStartInfo GuardStart(params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Join(AppContext.BaseDirectory, "HexIDE.LspGuard"))
        {
            UseShellExecute = false,
        };
        start.ArgumentList.Add("--");
        start.ArgumentList.Add("dotnet");
        start.ArgumentList.Add(Path.Join(AppContext.BaseDirectory, "LspTestHost", "HexIDE.Lsp.TestHost.dll"));
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }

    private static async Task RunHelper(string transport, bool killOwner)
    {
        var directory = Path.Join(Path.GetTempPath(), $"hexide-406-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var pids = Path.Join(directory, "pids");
        var gate = Path.Join(directory, "gate");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.ArgumentList.Add(Path.Join(AppContext.BaseDirectory, "LspTestHost", "HexIDE.Lsp.TestHost.dll"));
        foreach (var argument in new[] { transport, pids, gate }) start.ArgumentList.Add(argument);
        using var owner = Process.Start(start)!;
        Process? server = null;
        Process? child = null;
        try
        {
            var ready = await owner.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            ready.Should().Be("ready", "the helper must start successfully; stderr: {0}",
                ready is null ? await owner.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken) : "");
            var ids = (await File.ReadAllTextAsync(pids, TestContext.Current.CancellationToken)).Split(' ');
            server = Process.GetProcessById(int.Parse(ids[0]));
            child = Process.GetProcessById(int.Parse(ids[1]));
            server.HasExited.Should().BeFalse();
            child.HasExited.Should().BeFalse();

            if (!killOwner) return;

            owner.Kill(); // Deliberately kill only the owner: the job or guard must end the servers.
            await owner.WaitForExitAsync(TestContext.Current.CancellationToken);

            server.WaitForExit(10000).Should().BeTrue("the job or guard must end the server, even if it ignores SIGTERM");
            child.WaitForExit(10000).Should().BeTrue("descendants inherit the job or process group");
        }
        finally
        {
            foreach (var process in new[] { owner, server, child })
            {
                if (process is null) continue;
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
            server?.Dispose();
            child?.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }
}
