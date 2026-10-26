using System.Globalization;
using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// No description a caller reads carries a control character it was not meant to (#1258).
/// </summary>
/// <remarks>
/// A regular C# literal turns <c>\n</c> into a line feed, so <c>type_text</c>'s <c>mode</c>, meant to name the two
/// characters keyboard mode presses as Enter and Tab, instead read "per character, (a line break) as Enter and (a gap)
/// as Tab". <see cref="ToolSource"/> decodes each literal as the compiler does, so a description here is the text a
/// client is sent. A blank line, <c>\n\n</c>, is how five descriptions break a paragraph, and is the one use allowed.
/// </remarks>
public class ToolDescriptionTextTests
{
    private static IEnumerable<(string Where, string Text)> Descriptions() =>
        ToolSource.Tools.SelectMany(t => t.Parameters.Select(p => ($"{t.Name}.{p.Name}", p.Description))
            .Prepend((t.Name, t.Description)));

    private static readonly Regex ParagraphBreak = new(@"(?<=\S)\n\n(?=\S)");

    // Control, and also what is as unseen and as disruptive: a format character such as a zero-width space or a
    // direction override, and a line or paragraph separator, which a renderer breaks the line on.
    internal static bool HasStrayControl(string text) =>
        ParagraphBreak.Replace(text, " ").Any(c => char.GetUnicodeCategory(c) is UnicodeCategory.Control
            or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator);

    [Fact]
    public void No_description_holds_a_control_character_but_a_paragraph_break()
    {
        Descriptions().Where(d => HasStrayControl(d.Text)).Select(d => d.Where)
            .Should().BeEmpty("a \\n or \\t in a regular literal reaches the caller as a line feed or a tab, not as the two "
                              + "characters it names; write \\\\n, or a blank line \\n\\n for a paragraph break");
    }

    [Theory]
    [InlineData("per character, \n as Enter", true)]
    [InlineData("a gap\tthere", true)]
    [InlineData("one paragraph.\n\nThe next.", false)]
    [InlineData("three\n\n\nbreaks", true)]
    [InlineData("two\n\n\n\nblank lines", true)]
    [InlineData("\n\nleading", true)]
    [InlineData("trailing\n\n", true)]
    [InlineData("one\r\nline", true)]
    [InlineData("per character, \\n as Enter", false)]
    public void Only_a_blank_line_between_paragraphs_is_allowed(string text, bool stray)
    {
        HasStrayControl(text).Should().Be(stray);
    }

    [Theory]
    [InlineData(0x2028)] // line separator
    [InlineData(0x200B)] // zero-width space
    [InlineData(0x202E)] // right-to-left override
    [InlineData(0xFEFF)] // byte order mark
    public void A_character_a_reader_cannot_see_is_stray(int unseen)
    {
        HasStrayControl("a" + (char)unseen + "b").Should().BeTrue();
    }

    [Fact]
    public void A_description_is_read_past_a_constant_it_appends()
    {
        // interact, type_text and press_key end with + ProgramWaitNote, which was neither measured nor checked.
        ToolSource.Tools.Where(t => t.Name is "interact" or "type_text" or "press_key")
            .Should().HaveCount(3).And.OnlyContain(t => t.Description.EndsWith("A form the program opened is named.", StringComparison.Ordinal));
        ToolSource.Unparsed.Should().BeEmpty();
    }

    [Fact]
    public void Type_texts_mode_names_the_characters_it_presses_as_Enter_and_Tab()
    {
        ToolSource.Tools.Single(t => t.Name == "type_text").Parameters.Single(p => p.Name == "mode").Description
            .Should().Contain(@"per character, \n as Enter and \t as Tab");
    }

    [Fact]
    public void Type_text_explains_descendant_surface_refusals()
    {
        ToolSource.Tools.Single(t => t.Name == "type_text").AllText
            .Should().Contain("more than one showing surface of the chosen kind is refused")
            .And.Contain("with their paths listed")
            .And.Contain("dropdown that is not editable is refused with a pointer to select");
    }

    [Fact]
    public void Interact_explains_the_primary_and_how_to_choose_it()
    {
        ToolSource.Tools.Single(t => t.Name == "interact").Parameters.Single(p => p.Name == "action").Description
            .Should().Contain("names the primary").And.Contain("The primary is the last control added");
    }

    [Fact]
    public void Close_says_which_message_boxes_refuse_it()
    {
        // An OK-only box has no Cancel, yet its close box answers vbOK; only Yes/No and Abort/Retry/Ignore refuse.
        ToolSource.Tools.Single(t => t.Name == "interact").Parameters.Single(p => p.Name == "action").Description
            .Should().Contain("a Yes/No or Abort/Retry/Ignore MsgBox refuses").And.NotContain("MsgBox with no Cancel");
    }
}
