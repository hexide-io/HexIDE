namespace HexIDE.Tests.Infrastructure;

public class TranslationIssueFormTests
{
    [Fact]
    public void TranslationFormCollectsTheCorrectionWithOnlyTheStringKeyOptional()
    {
        var form = File.ReadAllText(Path.Combine(RepoTree.Root(), ".github", "ISSUE_TEMPLATE", "translation.yml"))
            .ReplaceLineEndings("\n");

        form.Should().Contain("labels: [translation]");
        foreach (var id in new[] { "language", "location", "current", "suggestion" })
        {
            var field = form.Split("  - type:").Single(section => section.Contains($"    id: {id}\n", StringComparison.Ordinal));
            field.Should().Contain("      label:").And.Contain("      required: true");
        }

        var key = form.Split("  - type:").Single(section => section.Contains("    id: key\n", StringComparison.Ordinal));
        key.Should().Contain("Str.*").And.Contain("      required: false");
        form.Should().Contain("pack id").And.Contain("de");
        form.Split("  - type:").Should().HaveCount(6, "the short form has just five fields");
    }
}
