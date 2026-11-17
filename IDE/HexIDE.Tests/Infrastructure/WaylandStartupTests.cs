using System.Diagnostics;
using System.Text;

namespace HexIDE.Tests.Infrastructure;

public class WaylandStartupTests
{
    public static bool IsLinux => OperatingSystem.IsLinux();

    [Theory(Skip = "Wayland startup is Linux-only", SkipUnless = nameof(IsLinux))]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Unavailable_display_reports_selection_source_and_respects_launcher_logging(bool standalone, bool environment)
    {
        // Keep the Wayland socket path below the 108-byte Unix limit, regardless of checkout depth (#4437).
        var directory = Directory.CreateTempSubdirectory("wl-").FullName;
        try
        {
            Encoding.UTF8.GetByteCount(Path.Combine(directory, "wayland-0")).Should().BeLessThan(108,
                "the Unix socket path limit is 108 bytes; shorten TMPDIR");
            var launcher = standalone ? typeof(HexIDE.Standalone.Program).Assembly : typeof(HexIDE.Desktop.Program).Assembly;
            var tests = typeof(WaylandStartupTests).Assembly.Location;
            var start = new ProcessStartInfo("dotnet")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                WorkingDirectory = directory,
                ArgumentList =
                {
                    "exec", "--runtimeconfig", Path.ChangeExtension(tests, "runtimeconfig.json"),
                    "--depsfile", Path.ChangeExtension(tests, "deps.json"), launcher.Location
                }
            };
            start.Environment.Remove("WAYLAND_DISPLAY");
            start.Environment.Remove("DISPLAY");
            // Prevent an ambient compositor at wayland-0 from satisfying the connection.
            start.Environment["XDG_RUNTIME_DIR"] = directory;
            start.Environment["XDG_DATA_HOME"] = directory;
            start.Environment["LANG"] = "en_US.UTF-8";
            start.Environment["LC_ALL"] = "en_US.UTF-8";
            start.Environment["HEXIDE_PLATFORM"] = environment ? "wayland" : "x11";
            if (!environment)
            {
                start.ArgumentList.Add("--platform");
                start.ArgumentList.Add("wayland");
            }
            if (!standalone)
            {
                start.ArgumentList.Add("--newproject-unsaved");
                start.ArgumentList.Add("--user-data-dir");
                start.ArgumentList.Add(Path.Combine(directory, "profile"));
            }
            using var process = Process.Start(start)!;
            var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var finished = process.WaitForExit(60_000);
            if (!finished)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
            finished.Should().BeTrue("startup should refuse without entering the event loop");
            var error = await stderr;
            process.ExitCode.Should().Be(2, error + await stdout);
            var line = error.TrimEnd('\r', '\n');
            var expected = environment
                ? "Native Wayland was required by HEXIDE_PLATFORM=wayland, but no Wayland display could be reached: Unable to connect to Wayland display. Set HEXIDE_PLATFORM to x11 or auto, or unset it."
                : "Native Wayland was required by --platform wayland, but no Wayland display could be reached: Unable to connect to Wayland display. Start with --platform x11 or --platform auto instead.";
            line.Should().Be(expected);
            error.Should().Be(line + Environment.NewLine);
            var logs = Path.Combine(directory, "HexIDE", "logs");
            if (standalone)
                Directory.Exists(logs).Should().BeFalse("a made program must not write the IDE's log");
            else
            {
                var log = Directory.GetFiles(Path.Combine(logs, "ide"), "*.log")
                    .Should().ContainSingle().Subject;
                File.ReadAllText(log).Should().Contain(line + Environment.NewLine);
            }
            Directory.GetFiles(directory, "core*").Should().BeEmpty();
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
