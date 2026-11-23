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

    // ── The first-party signing key (#4740) ──────────────────────────────────────────────────────────────────
    // Every restore, build and publish runs the build-time code of the packages it pulls in, so the key is only ever
    // on a runner that does none of them: publish.yml's sign-addins job, behind the addin-signing environment.

    private static string Publish => File.ReadAllText(
        Path.Combine(RepoTree.Root(), ".github", "workflows", "publish.yml")).ReplaceLineEndings("\n");

    private static string PublishJob(string name) => Regex.Match(
        Publish, $@"(?ms)^  {Regex.Escape(name)}:\n.*?(?=^  [a-z][\w-]*:|\z)").Value;

    [Fact]
    public void Only_the_signing_job_names_the_first_party_key()
    {
        var workflows = Directory.GetFiles(Path.Combine(RepoTree.Root(), ".github", "workflows"), "*.y*ml");
        var namings = workflows.Sum(file => Regex.Matches(File.ReadAllText(file), @"secrets\.HEXIDE_FIRSTPARTY_KEY\b").Count);

        namings.Should().Be(1, "only publish.yml's sign-addins job may receive the key");
        PublishJob("sign-addins").Should().Contain("${{ secrets.HEXIDE_FIRSTPARTY_KEY }}")
            .And.Contain("\n    environment: addin-signing\n");
    }

    [Fact]
    public void The_signing_job_builds_nothing_and_pins_its_actions()
    {
        var job = PublishJob("sign-addins");

        job.Should().NotBeEmpty();
        job.Should().NotContain("dotnet ", "the packer arrives built and self-contained, so nothing here restores or builds")
            .And.NotContain("setup-dotnet").And.NotContain("msbuild");
        job.Should().Contain("\n    permissions:\n      contents: read\n");
        var uses = Regex.Matches(job, @"(?m)^\s+(?:- )?uses: (\S+)").Select(m => m.Groups[1].Value).ToArray();
        uses.Should().NotBeEmpty().And.OnlyContain(
            action => Regex.IsMatch(action, @"@[0-9a-f]{40}$"), "a moved tag would run new code beside the key");
        job.Should().Contain("artifact-ids: ${{ needs.packer.outputs.artifact-id }}")
            .And.Contain("artifact-ids: ${{ needs.addins.outputs.artifact-id }}");
    }

    [Fact]
    public void Build_legs_take_the_signed_addins_instead_of_packing()
    {
        var build = PublishJob("build");

        build.Should().Contain("needs: [preflight, sign-addins]")
            .And.Contain("artifact-ids: ${{ needs.sign-addins.outputs.artifact-id }}");
        build.Should().NotContain("firstparty").And.NotContain("AddinPacker")
            .And.NotContain("AddinTrustDir", "a build leg holds no key and packs nothing");
        build.IndexOf("Add the signed first-party add-ins to both flavours", StringComparison.Ordinal).Should()
            .BeLessThan(build.IndexOf("- name: Stage both flavours", StringComparison.Ordinal),
                "the macOS seal and the Windows installer must take the add-ins in");
    }

    [Fact]
    public void Release_and_Desktop_build_read_one_set_of_pack_arguments()
    {
        var arguments = File.ReadAllText(Path.Combine(RepoTree.Root(), "IDE", "HexIDE.AIChat", "pack.args"))
            .ReplaceLineEndings("\n");
        var desktop = File.ReadAllText(Path.Combine(RepoTree.Root(), "IDE", "HexIDE.Desktop", "HexIDE.Desktop.csproj"));

        arguments.Should().StartWith("--id\ncom.hexide.aichat\n");
        arguments.Should().NotContain("--src").And.NotContain("--out").And.NotContain("--trust");
        PublishJob("sign-addins").Should().Contain("done < IDE/HexIDE.AIChat/pack.args")
            .And.Contain("--out \"$RUNNER_TEMP/addins-signed/com.hexide.aichat\"");
        desktop.Should().Contain("<ReadLinesFromFile File=\"$(MSBuildProjectDirectory)/../HexIDE.AIChat/pack.args\">")
            .And.Contain("--out &quot;$(OutDir)addins/com.hexide.aichat&quot;");
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
        Job.Should().Contain("dotnet build HexIDE.Desktop -f net11.0 -c Release");
        Job.Should().Contain("dotnet build HexIDE.Standalone -f net11.0 -c Release");
    }

    [Fact]
    public void ReleaseOutputIsCheckedAfterBuilding()
    {
        var build = Job.IndexOf("dotnet build HexIDE.Desktop -f net11.0 -c Release", StringComparison.Ordinal);
        var check = Job.IndexOf("bash ../scripts/check-release-output.sh HexIDE.Desktop/bin/Release/net11.0", StringComparison.Ordinal);
        build.Should().BeGreaterThanOrEqualTo(0);
        check.Should().BeGreaterThan(build);
        Job.Should().NotContain("continue-on-error: true\n        run: bash ../scripts/check-release-output.sh");
    }
}
