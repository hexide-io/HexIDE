using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using DesktopProgram = HexIDE.Desktop.Program;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// <c>Win32SessionEndHook</c> answers Windows' end of session on a window Avalonia's Win32 platform creates for itself, found
/// by its class name: an Avalonia internal (#4646). This fails the build if an Avalonia upgrade stops creating it, rather
/// than leaving the IDE on its fallback without anyone knowing.
/// </summary>
public class PlatformWindowProbeTests
{
    public static bool IsWindows => OperatingSystem.IsWindows();

    [Fact(Skip = "Avalonia's Win32 platform runs only on Windows", SkipUnless = nameof(IsWindows))]
    public async Task Avalonia_still_creates_the_window_the_session_end_hook_answers_on()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(typeof(DesktopProgram).Assembly.Location)!,
            "HexIDE.Desktop.dll"));
        start.Environment["HEXIDE_PROBE_PLATFORM_WINDOW"] = "1";
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        try
        {
            await process.WaitForExitAsync(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
            (await stdout).Should().Contain("platform window: found", await stderr);
            process.ExitCode.Should().Be(0);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
                await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            }
        }
    }
}
