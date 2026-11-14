namespace HexIDE.Tests.Infrastructure;

public class BugReportIssueFormTests
{
    [Fact]
    public void BugReportCollectsReproductionAndOffersAnOptionalLogWithDisclosure()
    {
        var form = File.ReadAllText(Path.Combine(RepoTree.Root(), ".github", "ISSUE_TEMPLATE", "bug_report.yml"))
            .ReplaceLineEndings("\n");
        foreach (var id in new[] { "happened", "steps", "expected", "version", "os" })
        {
            var field = form.Split("  - type:").Single(section => section.Contains($"    id: {id}\n", StringComparison.Ordinal));
            field.Should().Contain("      label:").And.Contain("      required: true");
        }
        form.Should().Contain("Help → About");
        var log = form.Split("  - type:").Single(section => section.Contains("    id: log\n", StringComparison.Ordinal));
        log.Should().Contain("      required: false").And.Contain("Help → Open HexIDE Log Folder");
        foreach (var disclosure in new[] { "user profile", "account name", "project and file names", "exception text",
                     "a path, a value or code", "public issue publishes all of that", "Read it first", "remove anything", "leave it out and say you have one" })
            log.Should().Contain(disclosure);
        log.Should().NotContain("SECURITY.md").And.NotContain("privately");
    }
}
