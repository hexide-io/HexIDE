namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// Every tool that takes or returns a line number counts from 1, as the gutter does (hexide-io/HexIDE#571), read
/// headlessly from the tools' declarations.
/// </summary>
/// <remarks>
/// The bookmark tools counted from 0 and said so, and every other line tool from 1, so a caller who had just set a
/// breakpoint put a bookmark one line low. A tool that numbers lines another way would bring that back.
/// </remarks>
public class LineNumberingDescriptionTests
{
    [Fact]
    public void No_tool_counts_lines_from_zero()
    {
        ToolSource.Tools.Where(t => t.AllText.Contains("0-based", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Name).Should().BeEmpty();
    }

    [Theory]
    [InlineData("get_bookmarks")]
    [InlineData("set_bookmarks")]
    [InlineData("get_breakpoints")]
    [InlineData("set_breakpoints")]
    public void The_mark_tools_say_they_count_from_one(string tool)
    {
        ToolSource.Tools.Single(t => t.Name == tool).Description.Should().Contain("1-based");
    }
}
