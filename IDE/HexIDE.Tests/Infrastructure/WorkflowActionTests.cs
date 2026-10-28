using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

public class WorkflowActionTests
{
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
        docs.Should().Contain("name: build-ide (${{ matrix.name }})");
        builds.Should().Contain("name: build-ide (${{ matrix.name }})");
        docs.Should().Contain("runs-on: ubuntu-latest");
        builds.Should().Contain("runs-on: ${{ matrix.runner }}");

        var names = Regex.Matches(builds, @"\{ name: (\w+), runner: ([\w-]+) \}");
        names.Select(match => match.Groups[1].Value).Should().BeEquivalentTo(new[] { "windows", "macos" });
        foreach (Match name in names)
        {
            docs.Should().MatchRegex(@"(?m)^          - \{ name: " + name.Groups[1].Value + @" \}$");
            name.Groups[2].Value.Should().Be(name.Groups[1].Value + "-latest");
        }

        docs.Should().NotContain("uses:");
        Regex.Matches(docs, @"(?m)^        run: (.+)$").Select(match => match.Groups[1].Value)
            .Should().Equal("echo 'Build skipped for a docs-only change.'");
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
