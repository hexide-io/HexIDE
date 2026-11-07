using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

public class TestCountFloorTests
{
    [Theory]
    [InlineData("RUNTIME")]
    [InlineData("IDE")]
    [InlineData("INTEGRATION")]
    [InlineData("LSP_SERVER")]
    public void FullSuiteFloorsAllowAtMostFivePercentLoss(string suite)
    {
        var workflow = File.ReadAllText(Path.Combine(RepoTree.Root(), ".github", "workflows", "build.yml"));
        var row = Regex.Match(workflow, @"MIN_TESTS_" + suite + @": +(\d+) +# (\d+)");

        row.Success.Should().BeTrue("each floor records its measured total beside it");
        var floor = int.Parse(row.Groups[1].Value);
        var measured = int.Parse(row.Groups[2].Value);
        floor.Should().BeGreaterThanOrEqualTo((int)(measured * 0.95));
    }

    [Fact]
    public void EveryCiTestInvocationHasAFloor()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoTree.Root(), ".github", "workflows", "build.yml"))
            .ReplaceLineEndings("\n");
        var commands = Regex.Matches(workflow, @"dotnet test [^\n]+(?:\n +--[^\n]+)*")
            .Select(m => m.Value).ToArray();

        commands.Should().NotBeEmpty();
        commands.Where(c => !c.Contains("--minimum-expected-tests "))
            .Should().BeEmpty("a short run must fail, including a filtered CI step (#883)");
    }
}
