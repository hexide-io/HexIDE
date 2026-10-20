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

    internal static bool HasStrayControl(string text) =>
        text.Replace("\n\n", "", StringComparison.Ordinal).Any(char.IsControl);

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
    [InlineData("per character, \\n as Enter", false)]
    public void Only_a_blank_line_is_allowed(string text, bool stray)
    {
        HasStrayControl(text).Should().Be(stray);
    }

    [Fact]
    public void Type_texts_mode_names_the_characters_it_presses_as_Enter_and_Tab()
    {
        ToolSource.Tools.Single(t => t.Name == "type_text").Parameters.Single(p => p.Name == "mode").Description
            .Should().Contain(@"per character, \n as Enter and \t as Tab");
    }

    [Fact]
    public void Close_says_which_message_boxes_refuse_it()
    {
        // An OK-only box has no Cancel, yet its close box answers vbOK; only Yes/No and Abort/Retry/Ignore refuse.
        ToolSource.Tools.Single(t => t.Name == "interact").Parameters.Single(p => p.Name == "action").Description
            .Should().Contain("a Yes/No or Abort/Retry/Ignore MsgBox refuses").And.NotContain("MsgBox with no Cancel");
    }
}
