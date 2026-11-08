using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

public class ReleaseBuildTests
{
    [Fact]
    public void PublishIncludes32BitLinuxArm()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoTree.Root(), ".github", "workflows", "publish.yml"))
            .ReplaceLineEndings("\n");
        workflow.Should().MatchRegex(
            @"(?m)^\s+- \{ name: linux-arm,\s+runner: ubuntu-latest,\s+rid: linux-arm,\s+os: linux \}$");
    }

    [Fact]
    public void LinuxArmCrossPublishUsesHostAddinPacker()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoTree.Root(), ".github", "workflows", "publish.yml"))
            .ReplaceLineEndings("\n");
        workflow.Should().Contain("if: matrix.rid == 'linux-arm'\n")
            .And.Contain("dotnet publish HexIDE.AddinPacker -f net10.0 -c Release -r linux-x64 --self-contained false -o bin/addin-packer-host/")
            .And.Contain("ADDIN_PACKER_OVERRIDE=-p:AddinPacker=$PWD/bin/addin-packer-host/HexIDE.AddinPacker.dll");
        var desktopPublishes = workflow.Split('\n')
            .Where(line => line.Contains("run: dotnet publish HexIDE.Desktop")).ToArray();
        desktopPublishes.Should().HaveCount(2);
        desktopPublishes.Should().OnlyContain(line => line.Contains("${{ env.ADDIN_PACKER_OVERRIDE }}"));
    }

    public static bool IsLinux => OperatingSystem.IsLinux();

    [Fact(Skip = "The CI shell guard runs on Linux", SkipUnless = nameof(IsLinux))]
    public void OutputGuardRejectsContaminatedAndMissingOutputs()
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "bash",
            ArgumentList = { Path.Combine(RepoTree.Root(), "scripts", "check-release-output.selftest.sh") },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, output + error);
    }

    private static string Job => Regex.Match(
        File.ReadAllText(Path.Combine(RepoTree.Root(), ".github", "workflows", "build.yml"))
            .ReplaceLineEndings("\n"),
        @"(?ms)^  build-ide:\n.*?(?=^  [a-z][\w-]*:|\z)").Value;

    [Fact]
    public void BothShippingProjectsCompileInRelease()
    {
        Job.Should().Contain("dotnet build HexIDE.Desktop -f net10.0 -c Release");
        Job.Should().Contain("dotnet build HexIDE.Standalone -f net10.0 -c Release");
    }

    [Fact]
    public void ReleaseOutputIsCheckedAfterBuilding()
    {
        var build = Job.IndexOf("dotnet build HexIDE.Desktop -f net10.0 -c Release", StringComparison.Ordinal);
        var check = Job.IndexOf("bash ../scripts/check-release-output.sh HexIDE.Desktop/bin/Release/net10.0", StringComparison.Ordinal);
        build.Should().BeGreaterThanOrEqualTo(0);
        check.Should().BeGreaterThan(build);
        Job.Should().NotContain("continue-on-error: true\n        run: bash ../scripts/check-release-output.sh");
    }
}
