using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Tests.Components;

public class VBCaptionTextTests
{
    // The measured rows already pinned by CaptionAccessKeyTests; moved to Core by #5464.
    [Theory]
    [InlineData("Tom && Jerry", "Tom & Jerry", -1)]
    [InlineData("File_Name", "File_Name", -1)]
    [InlineData("Trail&", "Trail", -1)]
    [InlineData("A&&&Bc", "A&Bc", 2)]
    [InlineData("Hex_&Kit", "Hex_Kit", 4)]
    [InlineData("&Mute", "Mute", 0)]
    [InlineData("&One &Wide", "One Wide", 4)]
    public void A_control_reads_its_caption_as_vb6_does(string caption, string shown, int key)
    {
        var parsed = VBCaptionText.Parse(caption);

        parsed.Shown.Should().Be(shown);
        parsed.AccessKey.Should().Be(key < 0 ? null : key);
        VBCaptionText.Shown(caption).Should().Be(shown);
    }

    [Fact]
    public void A_menu_takes_the_first_marked_character_as_its_key()
    {
        VBCaptionText.Parse("&Go &Upward", firstKey: true).Should().Be(("Go Upward", (int?)0));
    }
}
