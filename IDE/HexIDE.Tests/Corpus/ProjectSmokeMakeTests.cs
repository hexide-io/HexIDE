using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace HexIDE.Tests.Corpus;

public class ProjectSmokeMakeTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Make_writes_a_runner_and_archive_or_reports_the_missing_runtime(bool runtimeAvailable)
    {
        var root = Directory.CreateTempSubdirectory("hexide-smoke-make-").FullName;
        try
        {
            var project = Path.Join(root, "Test.vbp");
            File.WriteAllText(project, "Type=Exe\r\nName=\"Test\"\r\nRetained=0\r\n");
            var runtime = Path.Join(root, "runtime");
            var runner = Path.Join(runtime, "standalone", ProjectService.StandaloneRunnerName()!);
            if (runtimeAvailable)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(runner)!);
                // Tests the Make packaging path, not whether a published runtime can start.
                File.WriteAllText(runner, "runner fixture");
            }
            var result = await ProjectSmokeService.Make(project, runtime);
            result.Status.Should().Be(runtimeAvailable ? "made" : "refused", result.Detail);
            var archive = Path.Join(root, "smoke-output", "Program.dll");
            File.Exists(archive).Should().Be(runtimeAvailable);
            if (runtimeAvailable)
            {
                using var zip = ZipFile.OpenRead(archive);
                zip.GetEntry("Program.vbp").Should().NotBeNull();
                File.ReadAllText(Path.Join(root, "smoke-output", OperatingSystem.IsWindows() ? "Program.exe" : "Program"))
                    .Should().Be("runner fixture");
            }
            else result.Detail.Should().Contain("Str.MakeProject.RuntimeNotFound");
        }
        finally { Directory.Delete(root, true); }
    }
}
