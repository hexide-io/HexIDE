using System.Diagnostics;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

public class NamedPipeShutdownTests
{
    // The server is the test host's teardown-probe, which says when it is running and exits a set time after the test
    // starts teardown. Both cases used to time a cmd.exe and ping from before the launch, so a slow start on a loaded
    // runner spent the grace and failed them (#2543). Now the clock starts with teardown, once the server is up.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TeardownDuringBringUpGivesTheLaunchedServerTimeToExit(bool exitsNaturally)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hexide-teardown-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var ready = Path.Combine(directory, "ready");
            var gate = Path.Combine(directory, "gate");
            var marker = Path.Combine(directory, "marker");
            var grace = LaunchedServerGuard.ExitTimeout;
            // Long enough that teardown without a grace kills it first, short enough to fit well inside the grace; or,
            // for a server that must be killed, well past the grace.
            var exitsAfter = exitsNaturally ? TimeSpan.FromMilliseconds(500) : grace + TimeSpan.FromSeconds(3);
            var launch = new NamedPipeLaunch("dotnet", "", ArgumentList:
            [
                Path.Join(AppContext.BaseDirectory, "LspTestHost", "HexIDE.Lsp.TestHost.dll"), "teardown-probe",
                ready, gate, marker, ((int)exitsAfter.TotalMilliseconds).ToString(),
            ]);
            await using var transport = new NamedPipeLspTransport(
                $"hexide-{Guid.NewGuid():N}"[..19], NamedPipeRole.Listen,
                Substitute.For<ILogger<NamedPipeLspTransport>>(), launch,
                connectTimeout: TimeSpan.FromMinutes(2));

            using var teardown = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var connecting = transport.ConnectAsync(new SystemTextJsonFormatter(), teardown.Token);
            var started = Stopwatch.StartNew();
            while (!File.Exists(ready) && !connecting.IsCompleted && started.Elapsed < TimeSpan.FromMinutes(1))
                await Task.Delay(20, TestContext.Current.CancellationToken);
            File.Exists(ready).Should().BeTrue("the server must start before teardown can be timed against it");

            var elapsed = Stopwatch.StartNew();
            await File.WriteAllTextAsync(gate, "teardown", TestContext.Current.CancellationToken);
            await teardown.CancelAsync();
            var handler = await connecting;
            elapsed.Stop();

            handler.Should().BeNull("this server is still bringing up and never opens its pipe");
            if (exitsNaturally)
            {
                elapsed.Elapsed.Should().BeLessThan(grace, "teardown returns once the server has exited");
            }
            else
            {
                elapsed.Elapsed.Should().BeGreaterThanOrEqualTo(grace, "a server that is still running gets the grace");
                // A hang guard only: the marker below is what shows the server was killed rather than waited out.
                elapsed.Elapsed.Should().BeLessThan(grace + TimeSpan.FromSeconds(30));
                var exitDue = exitsAfter - elapsed.Elapsed + TimeSpan.FromSeconds(1);
                if (exitDue > TimeSpan.Zero) await Task.Delay(exitDue, TestContext.Current.CancellationToken);
            }
            File.Exists(marker).Should().Be(exitsNaturally,
                "a quick server gets to exit itself; a lingering process tree must be killed");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
