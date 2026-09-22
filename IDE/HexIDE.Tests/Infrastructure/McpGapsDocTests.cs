using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// Guards <c>docs/mcp-server-gaps.md</c> against the one kind of drift a test can see (hexide-io/HexIDE#362).
/// </summary>
/// <remarks>
/// <para>
/// The file said a shutdown removed "its 38 tools" from a session. The server had 54 when that was written
/// and 64 when it was caught, and the number had been wrong at every one of the 29 commits that touched
/// the file. It was quoted rather than checked, which is how <c>docs/lsp-client.md</c>'s coverage table
/// drifted before <c>ProtocolCoverageDocTests</c>.
/// </para>
/// <para>
/// The count was taken out of the prose rather than kept and pinned: pinning it would make every new tool
/// fail the build over a sentence about something else. What stays guarded is that a count, if one is
/// written again, is the real one. The archive is exempt, because a measurement taken on a given day
/// rightly states that day's number.
/// </para>
/// </remarks>
public class McpGapsDocTests
{
    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepoTree.Root(), .. parts]));

    private static int DeclaredToolCount() =>
        Regex.Matches(ToolSource.WithoutComments(ToolSource.Source), @"\bMcpServerTool(?:Attribute)?\b").Count;

    [Fact]
    public void The_tool_declarations_are_found_at_all()
    {
        DeclaredToolCount().Should().BeGreaterThan(40, "the count this guard compares against has to be real");
    }

    /// <summary>
    /// A number and then "tools", allowing what prose actually does between them: bold or italics around the
    /// number, a line break, and up to two words ("64 automation tools", "**64** MCP tools"). The one spelling
    /// it used to know, a bare number, a space and "tools", passed silently on every other (hexide-io/HexIDE#549).
    /// "of" is not allowed between them, so "3 of the tools" is a part, not a count.
    /// </summary>
    internal static readonly Regex StatedCount = new(
        @"(?<![\w.])[*_]{0,2}(?<n>\d+)[*_]{0,2}\s+(?:(?!of\b)[A-Za-z-]+\s+){0,2}tools\b");

    [Theory]
    [InlineData("its 64 tools")]
    [InlineData("its **64** tools")]
    [InlineData("all _64_ MCP tools")]
    [InlineData("the 64 automation tools")]
    [InlineData("the 64\ntools")]
    public void A_count_is_recognised_however_the_prose_spells_it(string text)
    {
        StatedCount.Match(text).Groups["n"].Value.Should().Be("64");
    }

    [Theory]
    [InlineData("3 of the tools")]
    [InlineData("version 1.2 tools")]
    public void A_number_that_is_not_a_count_of_tools_is_left_alone(string text)
    {
        StatedCount.IsMatch(text).Should().BeFalse();
    }

    [Fact]
    public void Any_tool_count_the_gaps_doc_states_is_the_real_one()
    {
        var actual = DeclaredToolCount();
        var stated = StatedCount.Matches(Read("docs", "mcp-server-gaps.md"))
            .Select(m => int.Parse(m.Groups["n"].Value))
            .ToList();

        stated.Should().AllSatisfy(n => n.Should().Be(actual,
            "HexIdeTools.cs declares {0} tools. Better still, write the sentence without a number", actual));
    }
}
