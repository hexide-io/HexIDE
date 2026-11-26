using HexIDE.Runtime.BuiltinControls;

namespace HexIDE.Runtime.Integration.Tests.Automation;

/// <summary>
/// A running form's controls carry the names their code uses, and are named as their captions read (#954, #1128). No node
/// used to carry <c>Text1</c> or <c>Command1</c>, so a caller holding names from the code had no bridge to the running
/// form, and a button captioned <c>&amp;OK</c> was <c>Button[_OK]</c>, a menu <c>MenuItem[&amp;Game]</c>.
/// </summary>
public class RunningFormNamesTests
{
    [AvaloniaTheory]
    [InlineData("&OK", "OK")]
    [InlineData("E&xit", "Exit")]
    [InlineData("Tom && Jerry", "Tom & Jerry")]
    [InlineData("Plain", "Plain")]
    public void A_caption_reads_without_its_access_key_marker(string caption, string shown) =>
        VBCaption.AsShown(caption).Should().Be(shown);

    // Measured on vb6.exe (#920, the oracle's "A caption's access key"): A&&&Bc draws A&Bc, and a trailing & is not drawn
    // at all. The guess pinned here before the measurement kept it.
    [AvaloniaTheory]
    [InlineData("&&&Save", "&Save")]
    [InlineData("Ends &", "Ends ")]
    public void Captions_vb6_reads_left_to_right(string caption, string shown) =>
        VBCaption.AsShown(caption).Should().Be(shown);
}
