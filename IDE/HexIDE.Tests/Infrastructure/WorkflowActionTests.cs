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
        // The build names macOS on Apple silicon and on Intel advisory as well (#5564, #5674). Neither is a required check
        // or runs for a pull request, so the docs-only job has no stand-in for them and keeps the shorter name.
        builds.Should().Contain("name: build-ide (${{ matrix.name }}${{ (matrix.name == 'windows-arm' || matrix.name == 'macos' || matrix.name == 'macos-intel') && ', advisory' || '' }})");
        builds.Should().Contain("continue-on-error: ${{ matrix.name == 'windows-arm' || matrix.name == 'macos' || matrix.name == 'macos-intel' }}");
        docs.Should().Contain("runs-on: ubuntu-latest");
        builds.Should().Contain("runs-on: ${{ matrix.runner }}");

        // The matrix is a JSON list chosen by event (#4218): for a release branch push, a push to main, any other push or a
        // merge batch, and a pull request's own run, in that order. macOS runs only for the first two (#5674), and macOS on
        // Intel only for a release branch (#5564). The docs-only job stands in for every platform of a merge batch's list.
        builds.Should().Contain("startsWith(github.ref, 'refs/heads/release/')");
        builds.Should().Contain("github.ref == 'refs/heads/main'");
        builds.Should().Contain("(github.event_name != 'pull_request' || startsWith(github.head_ref, 'batch/'))");
        var lists = Regex.Matches(builds, @"'(\[\{""name"".*?\}\])'").Select(match => match.Groups[1].Value).ToArray();
        lists.Should().HaveCount(4, "the release list, main's list, a batch's list and the pull request's own list");
        lists[0].Should().Be(lists[1].TrimEnd(']') + @",{""name"":""macos-intel"",""runner"":""macos-15-intel""}]",
            "a release runs every leg main runs, and macOS on Intel");
        lists[1].Should().Be(lists[2].TrimEnd(']') + @",{""name"":""macos"",""runner"":""macos-latest""}]",
            "main runs every leg a batch runs, and macOS");
        Regex.Matches(lists[3], @"""name"":""([\w-]+)""").Select(match => match.Groups[1].Value).Should().Equal("windows");
        docs.Should().NotContain("name: macos");
        var names = Regex.Matches(lists[2], @"\{""name"":""([\w-]+)"",""runner"":""([\w-]+)""\}");
        names.Select(match => match.Groups[1].Value).Should().BeEquivalentTo(new[] { "windows", "windows-arm" });
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

        // Windows itself stays out of the advisory set: it's the one required leg (#1331, #5674).
        build.Should().Contain("continue-on-error: ${{ matrix.name == 'windows-arm' || matrix.name == 'macos' || matrix.name == 'macos-intel' }}");
        build.Should().NotContain("matrix.name == 'windows' ||");
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
        var uses = workflows.SelectMany(path => Regex.Matches(File.ReadAllText(path),
                @"uses:\s*" + Regex.Escape(action) + @"@([^\s]+)(?:[ \t]+(#[^\r\n]*))?")
            .Select(match => (Ref: match.Groups[1].Value, Comment: match.Groups[2].Value.Trim()))).ToArray();

        uses.Should().NotBeEmpty();
        ReviewedCommits.TryGetValue(action, out var commit);
        foreach (var use in uses)
            if (use.Ref != version)
                $"{use.Ref} {use.Comment}".Should().Be(commit, $"{action} is used at the reviewed {version}, or at its reviewed commit");
        if (commit is not null)
            uses.Should().Contain(use => $"{use.Ref} {use.Comment}" == commit, "a reviewed commit nothing uses has gone stale");
    }

    // The job that holds the release signing key pins its actions by commit, so a tag moved upstream can't reach the
    // key (#4740). Each commit is the release named beside it, of the reviewed major version, checked against the tag
    // through GitHub's API on 2026-11-23. Bumping one means checking the new commit the same way.
    private static readonly Dictionary<string, string> ReviewedCommits = new()
    {
        ["actions/checkout"] = "3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1",
        ["actions/upload-artifact"] = "cf430e030ddbb5b0abf93d22962f4752f3646cd9 # v7.0.2",
        ["actions/download-artifact"] = "9000827ccba6bdab643e8b6fd33ac0654aef8333 # v8.0.2",
    };

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
