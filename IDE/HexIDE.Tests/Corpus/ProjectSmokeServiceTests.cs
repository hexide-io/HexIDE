using System;
using System.IO;
using System.Threading.Tasks;

namespace HexIDE.Tests.Corpus;

public class ProjectSmokeServiceTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("hexide-smoke-service-").FullName;

    public void Dispose()
    {
        Directory.Delete(root, true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task The_real_project_service_opens_and_saves_an_unchanged_project()
    {
        var path = Path.Join(root, "Test.vbp");
        File.WriteAllText(path, "Type=Exe\r\nName=\"Test\"\r\n");
        var opened = false;
        var result = await ProjectSmokeHarness.Check(root, path, async copied =>
        {
            var save = await ProjectSmokeService.Open(copied);
            opened = true;
            return save;
        });
        opened.Should().BeTrue();
        result.Crash.Should().BeNull();
        result.Opened.Should().BeTrue();
        result.Save.Should().Be("identical");
        result.Failed.Should().BeFalse();
    }
}
