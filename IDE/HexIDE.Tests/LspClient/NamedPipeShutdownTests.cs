using System.Diagnostics;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

public class NamedPipeShutdownTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(5, false)]
    public async Task TeardownDuringBringUpGivesTheLaunchedServerTwoSecondsToExit(
        int seconds, bool exitsNaturally)
    {
        var marker = Path.Combine(Path.GetTempPath(), $"hexide-{Guid.NewGuid():N}.txt");
        var launch = OperatingSystem.IsWindows()
            ? new NamedPipeLaunch("cmd.exe", $"/c \"ping -n {seconds + 1} 127.0.0.1 >nul & echo done > \"{marker}\"\"")
            : new NamedPipeLaunch("/bin/sh", $"-c \"(sleep {seconds}; echo done > '{marker}') & wait\"");
        await using var transport = new NamedPipeLspTransport(
            $"hexide-{Guid.NewGuid():N}"[..19], NamedPipeRole.Listen,
            Substitute.For<ILogger<NamedPipeLspTransport>>(), launch,
            connectTimeout: TimeSpan.FromMilliseconds(150));
        try
        {
            var elapsed = Stopwatch.StartNew();
            var handler = await transport.ConnectAsync(new SystemTextJsonFormatter(),
                TestContext.Current.CancellationToken);
            handler.Should().BeNull("this server is still bringing up and never opens its pipe");
            if (!exitsNaturally)
            {
                elapsed.Elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(2));
                elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(4));
                await Task.Delay(TimeSpan.FromSeconds(seconds), TestContext.Current.CancellationToken);
            }
            File.Exists(marker).Should().Be(exitsNaturally,
                "a quick server gets to exit itself; a lingering process tree must be killed");
        }
        finally
        {
            File.Delete(marker);
        }
    }
}
