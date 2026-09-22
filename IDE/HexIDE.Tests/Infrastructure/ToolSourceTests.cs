using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// Guards the parser every tool-description guard reads (hexide-io/HexIDE#549). A declaration it cannot read
/// must fail here rather than drop silently out of all of them.
/// </summary>
/// <remarks>
/// The counts are taken from the source by a plain search that knows nothing about the parser's patterns, so a
/// declaration written in a shape the parser does not expect makes the two disagree. Before this, a tool whose
/// attribute carried any other property was never found, and its <c>[DescribesEnum]</c> fell into the previous
/// tool's block after that tool's method, where nothing read it.
/// </remarks>
public class ToolSourceTests
{
    private static int Count(string pattern) =>
        Regex.Matches(ToolSource.WithoutComments(ToolSource.Source), pattern).Count;

    [Fact]
    public void Nothing_in_the_tool_source_went_unread()
    {
        ToolSource.Unparsed.Should().BeEmpty();
    }

    [Fact]
    public void Every_tool_attribute_in_the_source_is_a_parsed_tool()
    {
        var declared = Count(@"\bMcpServerTool(?:Attribute)?\b");
        declared.Should().BeGreaterThan(40, "the count this is compared against has to be real");
        ToolSource.Tools.Should().HaveCount(declared,
            "a tool the parser cannot read is otherwise skipped by every guard built on it");
    }

    [Fact]
    public void Every_describes_enum_attribute_is_credited_to_a_tool()
    {
        ToolSource.Tools.Sum(t => t.Enums.Count).Should().Be(Count(@"\bDescribesEnum(?:Attribute)?\b"),
            "an attribute the parser cannot credit to its tool is one no guard checks");
    }

    [Fact]
    public void Every_description_attribute_is_credited_to_a_tool_or_a_parameter()
    {
        // A parameter's description travels in the input schema, and is where text too long for the tool's own
        // description goes (#638), so one the parser cannot read would escape every guard on what a caller reads.
        var credited = ToolSource.Tools.Count + ToolSource.Tools.Sum(t => t.Parameters.Count(p => p.Description.Length > 0));
        credited.Should().Be(Count(@"\[\s*(?:[\w.]+\.)?Description(?:Attribute)?\s*\("),
            "a description the parser cannot credit is one no guard checks");
    }

    [Fact]
    public void Every_tool_replies_with_a_record_the_parser_can_read()
    {
        ToolSource.Tools.Where(t => ToolSource.FieldsOf(t.ReplyType) is null)
            .Select(t => $"{t.Name} replies with {t.ReplyType}")
            .Should().BeEmpty("a reply the guards cannot read is one whose fields nothing checks");
    }

    [Fact]
    public void A_comment_inside_a_record_is_not_a_field()
    {
        // LspMessagesResult's parameter list is interleaved with comments. Their last words, 'total' and
        // 'remove' among them, used to be read as fields.
        ToolSource.FieldsOf("LspMessagesResult")!.Select(f => f.Name)
            .Should().Equal("messages", "matched", "truncated", "framesDropped", "note");
    }

    [Fact]
    public void A_field_is_named_as_the_wire_names_it()
    {
        ToolSource.FieldOf("string VBTypeName")!.Name.Should().Be("vbTypeName",
            "System.Text.Json lower-cases a whole leading acronym, not just its first letter");
        ToolSource.FieldOf("""[property: JsonPropertyName("custom_name")] int Other""")!.Name.Should().Be("custom_name");
        ToolSource.FieldOf("[property: System.Text.Json.Serialization.JsonIgnore] FormDefinition? Form = null")
            .Should().BeNull("an ignored property never reaches a caller");
        ToolSource.FieldsOf("AddControlResult")!.Select(f => f.Name).Should().NotContain("form");
    }
}
