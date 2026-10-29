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

    // ── The budget (hexide-io/HexIDE#2511) ─────────────────────────────────────────────────────────────────────────
    //
    // A description at the limit has room for nothing, and a merge that adds both sides' text takes it over unannounced:
    // nine had reached it, and each new field was being paid for by deleting guidance a first-time caller needed. So a
    // description stays within a budget below the limit, and what explains a reply field goes in the reply, beside the
    // field, where a caller reads it. The tools over budget when the rule came in are listed, each with what takes the
    // room and the issue that brings it within; a tool leaves the list in the change that does, or the second test fails.

    private const int Budget = 1900;

    /// <summary>Descriptions over <see cref="Budget"/> when the rule came in, keyed "tool" or "tool.parameter".</summary>
    private static readonly Dictionary<string, (string Why, int Issue)> OverBudget = new()
    {
        ["take_snapshot"] = ("the coordinate mapping and the capture file's lifetime, which are the reply's to explain", 2565),
        ["dump_visual_tree"] = ("the control-view rules and the reply fields isHidden, isOffscreen and focusedPath", 2566),
        ["press_key"] = ("focus resolution and the shared ProgramWaitNote", 2567),
        ["interact"] = ("click's reply detail and the shared ProgramWaitNote", 2568),
        ["interact.action"] = ("every verb's semantics, the held button's among them", 2568),
        ["interact.value"] = ("click's arguments and the held button's lifecycle", 2568),
        ["inspect_element"] = ("per-field explanations, with target's own description empty", 2569),
    };

    private static IEnumerable<(string Key, int Length)> AllDescriptions() =>
        ToolSource.Tools.SelectMany(t => new[] { (t.Name, t.Description.Length) }
            .Concat(t.Parameters.Select(p => ($"{t.Name}.{p.Name}", p.Description.Length))));

    [Fact]
    public void No_description_grows_past_the_budget_unless_it_is_listed_as_not_yet_within_it()
    {
        AllDescriptions().Where(d => d.Length > Budget && !OverBudget.ContainsKey(d.Key))
            .Select(d => $"{d.Key}: {d.Length} characters")
            .Should().BeEmpty($"a description stays within {Budget} characters, below the {Limit} a client delivers, so a merge "
                              + "cannot take it over; explain a reply field in the reply where it appears, and keep the "
                              + "description to what a caller needs before the first call");
    }

    [Fact]
    public void A_description_brought_within_the_budget_leaves_the_list()
    {
        var lengths = AllDescriptions().ToDictionary(d => d.Key, d => d.Length);
        OverBudget.Where(o => !lengths.TryGetValue(o.Key, out var length) || length <= Budget)
            .Select(o => $"{o.Key} (#{o.Value.Issue}) is {(lengths.TryGetValue(o.Key, out var l) ? $"{l} characters" : "gone")}")
            .Should().BeEmpty($"a description within {Budget} characters, or one that no longer exists, is taken off OverBudget "
                              + "in the change that does it, so the list only ever shrinks");
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
