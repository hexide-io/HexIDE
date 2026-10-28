using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// Guards <c>docs/mcp-server-gaps.md</c> against the one kind of drift a test can see (hexide-io/HexIDE#362),
/// and the one-file-per-entry layout under <c>docs/mcp-gaps/</c> that replaced its list (hexide-io/HexIDE#2510).
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
        var stated = new[] { Read("docs", "mcp-server-gaps.md") }.Concat(Entries("open").Select(File.ReadAllText))
            .SelectMany(text => StatedCount.Matches(text))
            .Select(m => int.Parse(m.Groups["n"].Value))
            .ToList();

        stated.Should().AllSatisfy(n => n.Should().Be(actual,
            "HexIdeTools.cs declares {0} tools. Better still, write the sentence without a number", actual));
    }

    // ── One file per entry (hexide-io/HexIDE#2510) ───────────────────────────────────────────────────────────
    //
    // Entries used to share two files, and every pull request that recorded or closed a gap edited the top of
    // the same one, so any two conflicted. Three entries had also been copied into the archive without leaving
    // the live file, and read as open for weeks after they closed. A file that moves cannot be in both places.

    private static readonly Regex EntryName = new(@"^[a-z0-9]+(-[a-z0-9]+)*\.md$", RegexOptions.Compiled);

    /// <summary>The index's own sections. Anything else under a level-two heading there is an entry in the wrong place.</summary>
    private static readonly string[] IndexSections =
    [
        "Where the entries are",
        "Writing an entry",
        "Numbered entries",
        "Not an MCP gap (recorded to avoid confusion)",
        "An empty reply that does not say why is a defect, not a null result",
    ];

    private static string Folder(string status) => Path.Combine(RepoTree.Root(), "docs", "mcp-gaps", status);

    private static IEnumerable<string> Entries(string status) => Directory.EnumerateFiles(Folder(status), "*.md");

    /// <summary>What a heading says goes wrong, without its old number or the status written after a dash.</summary>
    private static string TitleOf(string entry)
    {
        var heading = File.ReadLines(entry).First()[2..];
        heading = Regex.Replace(heading, @"^\d+\.\s+", "");
        return heading.Split(" — ")[0].Trim();
    }

    [Fact]
    public void Both_folders_exist_and_hold_entries()
    {
        // A guard over folders that have moved checks nothing and passes.
        Entries("open").Should().NotBeEmpty("docs/mcp-gaps/open/ holds the gaps still open");
        Entries("closed").Should().NotBeEmpty("docs/mcp-gaps/closed/ holds the gaps retired once fixed");
    }

    [Fact]
    public void Every_entry_is_named_in_lower_case_and_starts_with_its_heading()
    {
        foreach (var entry in Entries("open").Concat(Entries("closed")))
        {
            var name = Path.GetFileName(entry);
            EntryName.IsMatch(name).Should().BeTrue($"{name} must be named <issue>.md or <issue>-<word>.md, in lower case");
            File.ReadLines(entry).FirstOrDefault().Should().StartWith("# ",
                $"{name} must start with a level-one heading saying what goes wrong");
        }
    }

    [Fact]
    public void No_gap_is_both_open_and_closed()
    {
        var open = Entries("open").ToDictionary(e => TitleOf(e), Path.GetFileName);
        var both = Entries("closed").Select(TitleOf).Where(open.ContainsKey).Select(t => open[t]).ToList();
        both.Should().BeEmpty("closing a gap moves its file from open/ to closed/ with git mv; a copy leaves it reading as open");

        Entries("open").Select(Path.GetFileName).Intersect(Entries("closed").Select(Path.GetFileName))
            .Should().BeEmpty("one name in both folders is one gap recorded twice");
    }

    [Fact]
    public void No_entry_is_written_into_the_index_or_the_old_archive()
    {
        var headings = File.ReadLines(Path.Combine(RepoTree.Root(), "docs", "mcp-server-gaps.md"))
            .Where(l => l.StartsWith("## ", StringComparison.Ordinal))
            .Select(l => l[3..]);

        headings.Except(IndexSections).Should().BeEmpty(
            "a new gap goes in docs/mcp-gaps/open/<issue>.md, not in docs/mcp-server-gaps.md, which is the index");
        File.Exists(Path.Combine(RepoTree.Root(), "docs", "archive", "mcp-server-gaps-closed.md")).Should().BeFalse(
            "a closed gap is its own file in docs/mcp-gaps/closed/");
    }
}
