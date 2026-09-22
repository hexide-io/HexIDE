using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// Guards hexide-io/HexIDE#396, option 1: the structural half of whether a tool description can be followed.
/// </summary>
/// <remarks>
/// <para>
/// A description that misleads a caller produces a wrong call and a green build. These checks cannot say
/// whether the prose helps; they catch the part of it that can drift mechanically.
/// </para>
/// <para>
/// The first was found by building it: <c>list_lsp_messages</c> and <c>arm_lsp_capture</c> told callers to pass
/// <c>connection_id</c>, <c>failures_only</c> and <c>after_sequence</c>, while the SDK sends the C# names,
/// <c>connectionId</c> and the rest. A caller who followed the description passed arguments that do not exist.
/// </para>
/// </remarks>
public class ToolDescriptionParameterTests
{
    private static string Snake(string camel) => Regex.Replace(camel, "(?<=[a-z0-9])([A-Z])", "_$1").ToLowerInvariant();

    [Fact]
    public void The_parser_sees_the_tools_and_their_parameters()
    {
        ToolSource.Tools.Should().HaveCountGreaterThan(40);
        ToolSource.Tools.Single(t => t.Name == "list_lsp_messages").Parameters.Select(p => p.Name)
            .Should().Contain(["connectionId", "failuresOnly", "afterSequence"]);
    }

    [Fact]
    public void No_description_spells_a_parameter_in_a_form_the_wire_does_not_accept()
    {
        var wrong = new List<string>();
        var names = ToolSource.Tools.SelectMany(t => t.Parameters).Select(p => p.Name).Distinct();
        foreach (var name in names)
        {
            var snake = Snake(name);
            if (snake == name) continue;
            foreach (var tool in ToolSource.Tools.Where(t => Regex.IsMatch(t.AllText, $@"\b{snake}\b")))
                wrong.Add($"{tool.Name} says '{snake}'; the parameter is '{name}'");
        }

        string.Join(Environment.NewLine, wrong).Should().BeEmpty(
            "the MCP SDK sends a parameter under its C# name, so a caller who copies the other spelling passes nothing");
    }

    /// <remarks>
    /// The name has to be quoted or code-spanned, <c>'project'</c> or <c>`project`</c>. A bare word was found in
    /// ordinary prose: <c>run_to_cursor</c> could lose the sentence telling a caller about its <c>project</c>
    /// parameter and stay green, because it still "starts the project" (hexide-io/HexIDE#549).
    /// </remarks>
    [Fact]
    public void Every_optional_parameter_is_named_in_its_own_description()
    {
        var unnamed = ToolSource.Tools
            .SelectMany(t => t.Parameters.Where(p => p.Optional && !Regex.IsMatch(t.Description, $@"['`]{p.Name}['`]"))
                .Select(p => $"{t.Name}: {p.Name}"))
            .ToList();

        string.Join(Environment.NewLine, unnamed).Should().BeEmpty(
            "an optional parameter the description never names is one a first-time caller never uses");
    }

    /// <remarks>
    /// A nullable C# parameter with no default value is still <c>required</c> in the generated schema, so a
    /// caller who reads "optional" in the description and leaves it out is refused before the tool runs.
    /// <c>set_window_state</c> said "optional x/y/width/height" and required all four, and <c>move_control</c>'s
    /// description told a caller to omit what its schema required. CLAUDE.md warns about exactly this, and a
    /// warning is not a check.
    /// </remarks>
    [Fact]
    public void Every_nullable_parameter_is_optional_on_the_wire()
    {
        var required = ToolSource.Tools.SelectMany(t => t.Parameters
            .Where(p => p.Type.EndsWith('?') && !p.Optional)
            .Select(p => $"{t.Name}: {p.Type} {p.Name} has no default, so the schema requires it"));

        string.Join(Environment.NewLine, required).Should().BeEmpty(
            "give each one a default, usually = null, or make its type non-nullable if it is required");
    }

    private static readonly Regex SnakeWord = new(@"\b[a-z]+(?:_[a-z]+)+\b");

    private static string Camel(string snake) =>
        Regex.Replace(snake, "_([a-z])", m => m.Groups[1].Value.ToUpperInvariant());

    /// <remarks>
    /// The reply-side twin of <see cref="No_description_spells_a_parameter_in_a_form_the_wire_does_not_accept"/>.
    /// Replies are serialized camelCase, so <c>get_locals</c> promising rows with <c>has_children</c> and
    /// <c>take_snapshot</c> reporting a title "in 'active_dialog'" sent a caller looking for fields that are
    /// spelled <c>hasChildren</c> and <c>activeDialog</c> on the wire. A snake_case word is checked only when its
    /// camelCase form is a real reply field, so tool names and prose are left alone.
    /// </remarks>
    [Fact]
    public void No_description_spells_a_reply_field_in_a_form_the_wire_does_not_use()
    {
        var wrong = ToolSource.Tools
            .SelectMany(t => SnakeWord.Matches(t.AllText)
                .Select(m => m.Value)
                .Where(w => ToolSource.ReplyFields.Contains(Camel(w)))
                .Select(w => $"{t.Name} says '{w}'; the reply field is '{Camel(w)}'"))
            .Distinct()
            .ToList();

        string.Join(Environment.NewLine, wrong).Should().BeEmpty(
            "replies are serialized camelCase, so a caller who looks for the snake_case spelling finds nothing");
    }

    // Only a quoted camelCase name is checked: a single word in quotes is as often a value ('select', 'en') as
    // a field, and a guard that has to be told which is which is one people learn to excuse.
    private static readonly Regex QuotedField = new(@"'(?<name>[a-z]+[A-Z][A-Za-z]*)'");

    [Fact]
    public void Every_quoted_field_name_is_a_parameter_or_a_reply_field()
    {
        var unknown = ToolSource.Tools
            .SelectMany(t => QuotedField.Matches(t.AllText)
                .Select(m => m.Groups["name"].Value)
                .Where(n => !t.Parameters.Any(p => p.Name == n) && !ToolSource.ReplyFields.Contains(n))
                .Select(n => $"{t.Name} quotes '{n}', which no parameter or reply field is called"))
            .Distinct()
            .ToList();

        string.Join(Environment.NewLine, unknown).Should().BeEmpty(
            "a caller looks for the field a description names, and finds nothing when it has been renamed");
    }
}
