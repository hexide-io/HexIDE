namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// Guards hexide-io/HexIDE#638: no description a caller reads may be longer than a client will deliver.
/// </summary>
/// <remarks>
/// <para>
/// Claude Code cuts an MCP tool description at <see cref="Limit"/> characters and appends "… [truncated]".
/// Measured on 2026-09-22 from the <c>interact</c> schema it delivered: the text before the marker was 2048
/// characters exactly. <c>interact</c> was 3093 and <c>dump_visual_tree</c> 2266, so their last paragraphs never
/// reached a model, and both tails held the only mention of passing <c>window</c> "ide" to reach the IDE while a
/// program runs.
/// </para>
/// <para>
/// What describes one parameter belongs in that parameter's own <c>[Description]</c>, which travels in the input
/// schema. Those are held to the same limit, since nothing is known about how a client treats a long one.
/// Other clients may cut at a different length; this is the tightest known, not a budget to fill.
/// </para>
/// </remarks>
public class ToolDescriptionLengthTests
{
    private const int Limit = 2048;

    [Fact]
    public void No_tool_description_is_longer_than_a_client_delivers()
    {
        ToolSource.Tools.Where(t => t.Description.Length > Limit)
            .Select(t => $"{t.Name}: {t.Description.Length} characters")
            .Should().BeEmpty($"Claude Code delivers the first {Limit} characters of a tool description and drops the rest; "
                              + "move what describes a single parameter into that parameter's [Description]");
    }

    [Fact]
    public void No_parameter_description_is_longer_than_a_client_delivers()
    {
        ToolSource.Tools.SelectMany(t => t.Parameters
                .Where(p => p.Description.Length > Limit)
                .Select(p => $"{t.Name}.{p.Name}: {p.Description.Length} characters"))
            .Should().BeEmpty($"a description over {Limit} characters is at risk of being cut wherever it is sent");
    }

    [Fact]
    public void The_parser_reads_parameter_descriptions_including_a_shared_constant()
    {
        // Without this, a parser that read no parameter descriptions would pass the check above vacuously.
        var interact = ToolSource.Tools.Single(t => t.Name == "interact");
        interact.Parameters.Single(p => p.Name == "action").Description.Should().StartWith("One of: invoke");
        interact.Parameters.Single(p => p.Name == "window").Description.Should().Contain("\"ide\"",
            "the window parameter's description is a const shared by the tree tools, and must be read through it");
    }
}
