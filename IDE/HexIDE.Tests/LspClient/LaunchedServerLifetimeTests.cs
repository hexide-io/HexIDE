using System.Diagnostics;
using System.IO;

namespace HexIDE.Tests.LspClient;

public class LaunchedServerLifetimeTests
{
    public static bool OnWindows => OperatingSystem.IsWindows();

    [Theory]
    [InlineData("stdio")]
    [InlineData("pipe")]
    public Task Helper_starts_and_reports_ready(string transport) => RunHelper(transport, killOwner: false);

    [Theory(Skip = "Job objects exist only on Windows.", SkipUnless = nameof(OnWindows))]
    [InlineData("stdio")]
    [InlineData("pipe")]
    public Task Killing_the_owner_without_disposal_ends_the_launched_server_and_its_child(string transport) =>
        RunHelper(transport, killOwner: true);

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

            owner.Kill(); // Deliberately not entireProcessTree: only the OS job can end the servers.
            await owner.WaitForExitAsync(TestContext.Current.CancellationToken);

            server.WaitForExit(10000).Should().BeTrue("the OS closes the killed owner's job handle");
            child.WaitForExit(10000).Should().BeTrue("descendants inherit job membership");
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
