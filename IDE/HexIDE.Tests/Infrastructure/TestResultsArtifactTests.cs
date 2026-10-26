using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

public class TestResultsArtifactTests
{
    [Theory]
    [InlineData("build-ide", "IDE")]
    [InlineData("build-ide-desktop", "IDE")]
    [InlineData("build-lsp-server", "LspServer")]
    public void TestJobsPreserveCrashLogsOnFailure(string job, string folder)
    {
        var workflow = File.ReadAllText(Path.Combine(RepoTree.Root(), ".github", "workflows", "build.yml"))
            .ReplaceLineEndings("\n");
        var body = Regex.Match(workflow, $@"(?ms)^  {Regex.Escape(job)}:\n.*?(?=^  [a-z][\w-]*:|\z)").Value;
        body.Should().NotBeEmpty();
        body.Should().Contain("dotnet test ");

        var uploads = Regex.Matches(body, @"(?ms)^      - name: [^\n]*\n.*?(?=^      - |\z)")
            .Select(match => match.Value)
            .Where(step => step.Contains("uses: actions/upload-artifact@", StringComparison.Ordinal))
            .ToArray();
        uploads.Should().ContainSingle("each test job must preserve its host's logs when a test crashes (#1309)");
        var upload = uploads[0];
        upload.Should().Contain("        if: failure()\n");
        upload.Should().Contain("          name: test-results-${{ github.job }}-${{ runner.os }}\n",
            "desktop matrix legs must not collide with each other or with the other jobs");
        upload.Should().Contain($"          path: {folder}/**/TestResults/**\n",
            "upload paths are relative to the workspace, not defaults.run.working-directory");
        upload.Should().Contain("          retention-days: 7\n");
    }
}
