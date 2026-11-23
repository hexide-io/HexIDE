using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

public class WorkflowActionTests
{
    [Fact]
    public void TestStepsSelectProjectsForTheNativeMtpRunner()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoTree.Root(), ".github", "workflows", "build.yml"))
            .ReplaceLineEndings("\n");
        var commands = Regex.Matches(workflow, @"dotnet test ([^\r\n]+)");
        commands.Should().NotBeEmpty();
        foreach (Match command in commands)
            command.Groups[1].Value.Should().StartWith("--project ");
    }

    [Fact]
    public void TestReportPathsRemainProjectLocal()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoTree.Root(), ".github", "workflows", "build.yml"))
            .ReplaceLineEndings("\n");
        var commands = Regex.Matches(workflow,
            @"dotnet test --project (HexIDE\.[\w.]+)/[^\n]+(?:\n +--[^\n]+)*")
            .Where(command => command.Value.Contains("--results-directory")).ToArray();
        commands.Should().NotBeEmpty();
        foreach (var command in commands)
            command.Value.Should().Contain($"--results-directory {command.Groups[1].Value}/TestResults");
    }

    [Fact]
    public void RequiredForeignServerStepsSkipImagePathAndPrintTheResolutionReport()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoTree.Root(), ".github", "workflows", "build.yml"))
            .ReplaceLineEndings("\n");
        var steps = Regex.Matches(workflow, @"(?ms)^      - name: .*?(?=^      - name:|\z)")
            .Select(match => match.Value).ToArray();
        var required = steps.Where(step => step.Contains("HEXIDE_REQUIRE_FOREIGN_LSP: '1'")).ToArray();
        required.Should().HaveCount(2);
        foreach (var step in required)
        {
            step.Should().Contain("HEXIDE_FOREIGN_LSP_NO_PATH: '1'");
            step.Should().Contain("HEXIDE_FOREIGN_LSP_REPORT: ${{ runner.temp }}/foreign-lsp.txt");
        }
        var jobs = Regex.Matches(workflow, @"(?ms)^  [\w-]+:\n.*?(?=^  [\w-]+:|\z)")
            .Select(match => match.Value).Where(job => job.Contains("HEXIDE_REQUIRE_FOREIGN_LSP: '1'"));
        foreach (var job in jobs)
            Regex.Matches(job, "name: Report resolved foreign servers").Count.Should().Be(1);
        var reports = steps.Where(step => step.StartsWith("      - name: Report resolved foreign servers\n")).ToArray();
        reports.Should().HaveCount(required.Length);
        foreach (var step in reports)
        {
            step.Should().Contain("if: always()");
            step.Should().Contain("cat \"$FOREIGN_LSP_REPORT\"");
            step.Should().Contain("FOREIGN_LSP_REPORT: ${{ runner.temp }}/foreign-lsp.txt");
        }
    }

    [Fact]
    public void DocsOnlyDesktopChecksMatchTheRealBuildsWithoutUsingDesktopRunners()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoTree.Root(), ".github", "workflows", "build.yml"))
            .ReplaceLineEndings("\n");
        var jobs = Regex.Matches(workflow, @"(?ms)^  ([\w-]+):\n(.*?)(?=^  [\w-]+:|\z)")
            .ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value);

        jobs.Should().ContainKey("build-ide-desktop-docs");
        var docs = jobs["build-ide-desktop-docs"];
        var builds = jobs["build-ide-desktop"];
        docs.Should().Contain("needs: changes");
        docs.Should().Contain("if: ${{ !cancelled() && needs.changes.outputs.skip == 'true' }}");
        builds.Should().Contain("if: ${{ !cancelled() && needs.changes.outputs.skip != 'true' }}");
        docs.Should().Contain("name: build-ide (${{ matrix.name }}${{ matrix.name == 'windows-arm' && ', advisory' || '' }})");
        // The build names macOS on Intel advisory as well (#5564). It isn't a required check and runs only for a release
        // branch push, never a pull request, so the docs-only job has no stand-in for it and keeps the shorter name.
        builds.Should().Contain("name: build-ide (${{ matrix.name }}${{ (matrix.name == 'windows-arm' || matrix.name == 'macos-intel') && ', advisory' || '' }})");
        docs.Should().Contain("runs-on: ubuntu-latest");
        builds.Should().Contain("runs-on: ${{ matrix.runner }}");

        // The matrix is a JSON list chosen by event (#4218): all three platforms for a push or a merge batch, Windows alone
        // for a pull request's own run. The docs-only job stands in for every platform of the full list. A release branch
        // push adds macOS on Intel to the full list (#5564).
        builds.Should().Contain("startsWith(github.ref, 'refs/heads/release/')");
        builds.Should().Contain("(github.event_name != 'pull_request' || startsWith(github.head_ref, 'batch/'))");
        var lists = Regex.Matches(builds, @"'(\[\{""name"".*?\}\])'").Select(match => match.Groups[1].Value).ToArray();
        lists.Should().HaveCount(3, "the release list, the full list and the pull request's own list");
        lists[0].Should().Be(lists[1].TrimEnd(']') + @",{""name"":""macos-intel"",""runner"":""macos-15-intel""}]",
            "a release runs every leg a push runs, and macOS on Intel");
        Regex.Matches(lists[2], @"""name"":""([\w-]+)""").Select(match => match.Groups[1].Value).Should().Equal("windows");
        var names = Regex.Matches(lists[1], @"\{""name"":""([\w-]+)"",""runner"":""([\w-]+)""\}");
        names.Select(match => match.Groups[1].Value).Should().BeEquivalentTo(new[] { "windows", "macos", "windows-arm" });
        foreach (Match name in names)
        {
            docs.Should().MatchRegex(@"(?m)^          - \{ name: " + name.Groups[1].Value + @" \}$");
            name.Groups[2].Value.Should().Be(name.Groups[1].Value == "windows-arm"
                ? "windows-11-arm" : name.Groups[1].Value + "-latest");
        }

        docs.Should().NotContain("uses:");
        Regex.Matches(docs, @"(?m)^        run: (.+)$").Select(match => match.Groups[1].Value)
            .Should().Equal("echo 'Build skipped for a docs-only change.'");
    }

    [Fact]
    public void WindowsArmIsAdvisoryAndKeepsTheWindowsProofsRequired()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoTree.Root(), ".github", "workflows", "build.yml"))
            .ReplaceLineEndings("\n");
        var build = Regex.Match(workflow, @"(?ms)^  build-ide-desktop:\n(.*?)(?=^  [\w-]+:|\z)").Value;

        build.Should().Contain("continue-on-error: ${{ matrix.name == 'windows-arm' || matrix.name == 'macos-intel' }}");
        build.Should().Contain("fail-fast: false");
        build.Should().Contain("{\"name\":\"windows-arm\",\"runner\":\"windows-11-arm\"}");
        build.Should().Contain("name: Publish the standalone runner (for the made-program test)\n        if: runner.os == 'Windows'");
        build.Should().Contain("HEXIDE_REQUIRE_MADE_PROGRAM: ${{ runner.os == 'Windows' && '1' || '0' }}");
        build.Should().Contain("HEXIDE_REQUIRE_FOREIGN_LSP: '1'");
        build.Should().Contain("foreign-lsp-${{ matrix.name }}-${{ hashFiles(");
    }

    [Theory]
    [InlineData("actions/checkout", "v7")]
    [InlineData("actions/cache", "v6")]
    [InlineData("actions/setup-dotnet", "v6")]
    [InlineData("actions/setup-node", "v7")]
    [InlineData("actions/upload-artifact", "v7")]
    [InlineData("actions/download-artifact", "v8")]
    [InlineData("softprops/action-gh-release", "efb35369e0ad2afab669f228072c1b0d510eae64")]
    public void EveryUseHasTheReviewedActionVersion(string action, string version)
    {
        var workflows = Directory.GetFiles(Path.Combine(RepoTree.Root(), ".github", "workflows"), "*.yml");
        var versions = workflows.SelectMany(path => Regex.Matches(File.ReadAllText(path),
                @"uses:\s*" + Regex.Escape(action) + @"@([^\s]+)")
            .Select(match => match.Groups[1].Value)).ToArray();

        versions.Should().NotBeEmpty();
        versions.Should().AllBe(version);
    }

    // A hung test otherwise holds its job for GitHub's six-hour default, and cancelling the job to free the queue
    // throws its log away, so nothing says which test hung. A step that times out ends the job normally, log kept.
    [Fact]
    public void EveryTestStepHasATimeout()
    {
        var lines = File.ReadAllLines(Path.Combine(RepoTree.Root(), ".github", "workflows", "build.yml"));
        var steps = lines.Select((line, index) => (line, index))
            .Where(s => Regex.IsMatch(s.line, @"^\s*- name: Run .*tests", RegexOptions.IgnoreCase)).ToArray();

        steps.Should().NotBeEmpty();
        steps.Where(s => !lines.Skip(s.index + 1).TakeWhile(l => !Regex.IsMatch(l, @"^\s*- name:"))
                .Any(l => Regex.IsMatch(l, @"^\s*timeout-minutes:\s*\d+")))
            .Select(s => s.line.Trim()).Should().BeEmpty("every test step needs a timeout-minutes");
    }
}
