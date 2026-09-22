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
            foreach (var tool in ToolSource.Tools.Where(t => Regex.IsMatch(t.Description, $@"\b{snake}\b")))
                wrong.Add($"{tool.Name} says '{snake}'; the parameter is '{name}'");
        }

        string.Join(Environment.NewLine, wrong).Should().BeEmpty(
            "the MCP SDK sends a parameter under its C# name, so a caller who copies the other spelling passes nothing");
    }

    [Fact]
    public void Every_optional_parameter_is_named_in_its_own_description()
    {
        var unnamed = ToolSource.Tools
            .SelectMany(t => t.Parameters.Where(p => p.Optional && !Regex.IsMatch(t.Description, $@"\b{p.Name}\b"))
                .Select(p => $"{t.Name}: {p.Name}"))
            .ToList();

        string.Join(Environment.NewLine, unnamed).Should().BeEmpty(
            "an optional parameter the description never names is one a first-time caller never uses");
    }

    // Only a quoted camelCase name is checked: a single word in quotes is as often a value ('select', 'en') as
    // a field, and a guard that has to be told which is which is one people learn to excuse.
    private static readonly Regex QuotedField = new(@"'(?<name>[a-z]+[A-Z][A-Za-z]*)'");

    [Fact]
    public void Every_quoted_field_name_is_a_parameter_or_a_reply_field()
    {
        var unknown = ToolSource.Tools
            .SelectMany(t => QuotedField.Matches(t.Description)
                .Select(m => m.Groups["name"].Value)
                .Where(n => !t.Parameters.Any(p => p.Name == n) && !ToolSource.ReplyFields.Contains(n))
                .Select(n => $"{t.Name} quotes '{n}', which no parameter or reply field is called"))
            .Distinct()
            .ToList();

        string.Join(Environment.NewLine, unknown).Should().BeEmpty(
            "a caller looks for the field a description names, and finds nothing when it has been renamed");
    }
}
