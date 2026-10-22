using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// Every tool that takes or returns a line number counts from 1, as the gutter does (hexide-io/HexIDE#571), read
/// headlessly from the tools' declarations.
/// </summary>
/// <remarks>
/// The bookmark tools counted from 0 and said so, and every other line tool from 1, so a caller who had just set a
/// breakpoint put a bookmark one line low. A tool that numbers lines another way, or that tells a caller another tool
/// does, would bring that back: get_file_content's description still said the bookmark tools counted from 0.
/// </remarks>
public class LineNumberingDescriptionTests
{
    // "from 0 to 10000" is a range, as hover's wait is given, not a numbering.
    private static readonly Regex FromZero =
        new(@"\b0-based\b|\bzero-based\b|\b0-indexed\b|\bzero-indexed\b|\bfrom (?:0|zero)\b(?! to\b)", RegexOptions.IgnoreCase);

    [Fact]
    public void No_tool_says_any_tool_counts_lines_from_zero()
    {
        ToolSource.Tools.Where(t => FromZero.IsMatch(t.AllText))
            .Select(t => $"{t.Name}: {FromZero.Match(t.AllText).Value}").Should().BeEmpty();
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

    [Fact]
    public void The_bookmark_tools_convert_at_their_edge()
    {
        // The store counts from 0; no test reaches a tool body, so this reads that each still converts (#571 review).
        ToolSource.MethodBodies("GetBookmarksAsync").Should().ContainSingle().Which.Should().Contain("MarkLineRules.BookmarksShown(");
        ToolSource.MethodBodies("SetBookmarksAsync").Should().ContainSingle().Which.Should()
            .Contain("MarkLineRules.BookmarkRequest(").And.Contain("MarkLineRules.BookmarksShown(");
    }

    [Fact]
    public void Get_bookmarks_says_its_numbering_in_the_reply_as_set_bookmarks_does()
    {
        // A session holding get_bookmarks' description from before #1245 reads its lines counted from 0, and the reply is
        // all that can set it right (#1261). The note is MarkLineRules', which MarkLineRulesTests read; here, that the tool
        // passes it as the reply's note, of the lines it returns, and that set_bookmarks words its lines the same way.
        ToolSource.MethodBodies("GetBookmarksAsync").Should().ContainSingle().Which.Should()
            .MatchRegex(@"new BookmarksResult\([^;]*, lines, null,\s*HexIDE\.IDE\.MarkLineRules\.BookmarksNote\(document\.Display, lines\)\);");
        ToolSource.MethodBodies("Held").Should().ContainSingle().Which.Should().Contain("MarkLineRules.Marks(what, lines)");
    }
}
