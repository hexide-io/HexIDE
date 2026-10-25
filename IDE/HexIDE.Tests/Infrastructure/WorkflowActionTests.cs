using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

public class WorkflowActionTests
{
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
}
