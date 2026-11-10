using Avalonia;
using HexIDE.Automation;
using HexIDE.Desktop.Server;

namespace HexIDE.Tests.Infrastructure;

/// <summary>What take_snapshot says of a popup it left out (#1960).</summary>
public class SnapshotLeftOutNoteTests
{
    [Fact]
    public void Nothing_left_out_gives_no_note() =>
        HexIdeTools.LeftOutNote([]).Should().BeNull();

    [Fact]
    public void A_left_out_popup_is_named_with_where_it_was_and_why()
    {
        var note = HexIdeTools.LeftOutNote([
            new SnapshotComposer.LeftOutPopup("MenuFlyoutPresenter", new PixelRect(-32000, -32000, 120, 90),
                "which is on none of the screens")]);

        note.Should().Be("Left out of this image: a MenuFlyoutPresenter popup reported at screen pixels (-32000, -32000), "
                         + "120 × 90, which is on none of the screens. The window and every other open popup are drawn, "
                         + "and dump_visual_tree still lists a left-out popup's controls.");
    }

    // A position the platform can't know is not quoted as if it had been measured (#3575).
    [Fact]
    public void A_popup_the_platform_cannot_place_gives_its_size_and_no_position()
    {
        var note = HexIdeTools.LeftOutNote([
            new SnapshotComposer.LeftOutPopup("Border", new PixelRect(0, 0, 237, 402), "which can't be placed", Located: false)]);

        note.Should().StartWith("Left out of this image: a Border popup 237 × 402, which can't be placed.");
    }
}
