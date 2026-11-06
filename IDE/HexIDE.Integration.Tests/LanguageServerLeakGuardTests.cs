using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HexIDE.Lsp;

namespace HexIDE.Integration.Tests;

public class LanguageServerLeakGuardTests
{
    [Fact]
    public void The_suite_guard_names_a_running_server_and_the_test_that_started_it()
    {
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("ping", "-n 60 127.0.0.1")
            : new ProcessStartInfo("sleep", "60");
        start.UseShellExecute = false;
        start.RedirectStandardOutput = true;
        start.CreateNoWindow = true;
        using var process = Process.Start(start)!;
        try
        {
            LaunchedServerGuard.NoteStarted(process);
            Action check = LanguageServerLeakGuard.Check;
            check.Should().Throw<Exception>().WithMessage($"*process {process.Id} (*")
                .Which.Message.Should().Contain(TestContext.Current.Test!.TestDisplayName);
        }
        finally
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
        LanguageServerLeakGuard.StillRunning().Should().NotContain(l => l.Id == process.Id,
            "a server that has exited is not a leak");
    }

    // The path the suite's leaks took: a real composition starts the bundled server for a VB6 document.
    [AvaloniaFact]
    public async Task A_real_composition_ends_the_bundled_server_it_started_when_disposed()
    {
        var test = TestContext.Current.Test!.TestDisplayName;
        var directory = Directory.CreateTempSubdirectory("hexide-leak-guard-");
        var owned = new RealComposition();
        try
        {
            var uri = new Uri(Path.Join(directory.FullName, "Module1.bas")).AbsoluteUri;
            await owned.Composition.LspClient.OpenDocumentAsync(uri, "Attribute VB_Name = \"Module1\"\r\nSub Main()\r\nEnd Sub\r\n",
                TestContext.Current.CancellationToken);
            var launched = LanguageServerLeakGuard.Launched.Values.Where(l => l.Test == test).ToList();
            launched.Should().ContainSingle("opening a VB6 document starts the bundled server")
                .Which.Command.Should().Contain("HexIDE.VbLspServer");
            LanguageServerLeakGuard.StillRunning().Should().Contain(launched[0]);

            await owned.DisposeAsync();

            LanguageServerLeakGuard.StillRunning().Should().NotContain(launched[0],
                "disposing the composition's client stops its server");
        }
        finally
        {
            await owned.DisposeAsync();
            directory.Delete(recursive: true);
        }
    }
}
