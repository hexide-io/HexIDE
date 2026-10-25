namespace HexIDE.Tests.Infrastructure;

public class ReleaseDownloadDocumentationTests
{
    [Fact]
    public void ReadmeExplains32BitLinuxArmAssetSelection()
    {
        Read("README.md").Should().Contain("`arm` for 32-bit Arm (`armv7l`)")
            .And.Contain("hexide-linux-arm-selfcontained.tar.gz");
    }

    private static string Read(params string[] path) =>
        File.ReadAllText(Path.Combine([RepoTree.Root(), .. path])).ReplaceLineEndings("\n");

    [Fact]
    public void ReadmeExplainsLinuxAssetSelectionExtractionAndLaunchBeforeBuilding()
    {
        var readme = Read("README.md");
        var start = readme.IndexOf("### Download and run", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1);
        var end = readme.IndexOf("### Build and run", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        var instructions = readme[start..end];

        instructions.Should().Contain("uname -m").And.Contain("x86_64").And.Contain("aarch64");
        instructions.Should().Contain("mkdir -p ~/hexide\ntar -xzf hexide-linux-amd64-selfcontained.tar.gz -C ~/hexide\n~/hexide/HexIDE");
        instructions.Should().Contain("python3 -m tarfile -e").And.Contain("untested");
        instructions.Should().Contain("ICU").And.Contain("llvmpipe").And.Contain("openSUSE Leap 16");
        instructions.Should().Contain(".NET 10 Runtime");
    }

    [Fact]
    public void ReleaseTemplateNamesPlainRuntimeAndAcknowledgesNativeDependencies()
    {
        var workflow = Read(".github", "workflows", "publish.yml");
        workflow.Should().NotContain("desktop runtime", "the published apps require Microsoft.NETCore.App only");
        workflow.Should().Contain("[.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)");
        workflow.Should().NotContain("no prerequisites");
        var linux = workflow.Split('\n').Single(line => line.Contains("**Linux**"));
        linux.Should().Contain("ICU").And.Contain("libX11").And.Contain("libICE")
            .And.Contain("libSM").And.Contain("fontconfig");
    }

    [Fact]
    public void ReleaseTemplateLinksToDownloadInstructionsAtTheReleaseTag()
    {
        Read(".github", "workflows", "publish.yml").Should().Contain(
            "https://github.com/hexide-io/HexIDE/blob/${{ inputs.tag }}/README.md#download-and-run");
        Read("README.md").Should().Contain("### Download and run");
    }
}
