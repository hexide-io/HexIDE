using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

public class WorkflowActionVersionTests
{
    [Theory]
    [InlineData("actions/checkout", "v7")]
    [InlineData("actions/setup-dotnet", "v6")]
    [InlineData("actions/cache", "v6")]
    [InlineData("actions/setup-node", "v7")]
    [InlineData("actions/upload-artifact", "v7")]
    [InlineData("actions/download-artifact", "v8")]
    [InlineData("softprops/action-gh-release", "v3")]
    public void EveryUseHasTheReviewedMajor(string action, string version)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "IDE"))
                             && Directory.Exists(Path.Combine(dir.FullName, "LspServer"))))
            dir = dir.Parent;
        dir.Should().NotBeNull("the workflow sources must be available to this guard");

        var uses = new List<string>();
        foreach (var workflow in new[] { "build.yml", "publish.yml" })
        {
            var source = File.ReadAllText(Path.Combine(dir!.FullName, ".github", "workflows", workflow))
                .ReplaceLineEndings("\n");
            foreach (Match match in Regex.Matches(source,
                         @"(?m)^\s*(?:-\s*)?uses:\s*" + Regex.Escape(action) + @"@(?<version>\S+)\s*$"))
                uses.Add(match.Groups["version"].Value);
        }

        uses.Should().NotBeEmpty($"{action} must still be exercised by a workflow");
        uses.Should().OnlyContain(actual => actual == version,
            $"all uses of {action} must use the major reviewed for #1742");
    }
}
