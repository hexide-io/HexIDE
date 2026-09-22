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
        Regex.Matches(Read("IDE", "HexIDE.Desktop", "Server", "HexIdeTools.cs"), @"\[McpServerTool\(").Count;

    [Fact]
    public void The_tool_declarations_are_found_at_all()
    {
        DeclaredToolCount().Should().BeGreaterThan(40, "the count this guard compares against has to be real");
    }

    [Fact]
    public void Any_tool_count_the_gaps_doc_states_is_the_real_one()
    {
        var actual = DeclaredToolCount();
        var stated = Regex.Matches(Read("docs", "mcp-server-gaps.md"), @"\b(\d+) (?:MCP )?tools\b")
            .Select(m => int.Parse(m.Groups[1].Value))
            .ToList();

        stated.Should().AllSatisfy(n => n.Should().Be(actual,
            "HexIdeTools.cs declares {0} tools. Better still, write the sentence without a number", actual));
    }
}
