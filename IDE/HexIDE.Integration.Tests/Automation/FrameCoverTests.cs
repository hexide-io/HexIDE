using Avalonia;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// A frame capture Windows rendered nothing for is copied from the screen, and then says how much of it other windows
/// cover (#2182). A copy of a covered window used to come back with the reply of a clean capture.
/// </summary>
public class FrameCoverTests
{
    private static readonly PixelRect Frame = new(100, 100, 200, 100);

    [Fact]
    public void Coverage_counts_overlapping_windows_once_and_ignores_what_lies_outside()
    {
        NativeWindowFrame.CoveredFraction(Frame, []).Should().Be(0);
        NativeWindowFrame.CoveredFraction(Frame, [new PixelRect(0, 0, 50, 50)]).Should().Be(0);
        NativeWindowFrame.CoveredFraction(Frame, [new PixelRect(0, 0, 1000, 1000)]).Should().Be(1);
        // The left half, twice over, and a quarter that overlaps it by half.
        NativeWindowFrame.CoveredFraction(Frame,
        [
            new PixelRect(100, 100, 100, 100),
            new PixelRect(50, 100, 150, 100),
            new PixelRect(150, 150, 100, 50),
        ]).Should().Be(0.625);
        // Two windows side by side with a gap between, each a quarter.
        NativeWindowFrame.CoveredFraction(Frame, [new PixelRect(100, 100, 50, 100), new PixelRect(250, 100, 50, 100)])
            .Should().Be(0.5);
    }

    // A minimized form can be restored through set_window_state since #2357, so the refusal says how; a hidden one only
    // its program can show. The two used to share one message that offered both.
    [Fact]
    public void A_minimized_window_is_told_how_to_restore_and_a_hidden_one_how_to_show()
    {
        NativeWindowFrame.HiddenOrMinimized(minimized: true).Should().Be(
            "The window is minimized, so Windows draws no frame for it. Restore it with set_window_state, state Normal and "
            + "its name as 'window', then retry includeFrame true.");
        NativeWindowFrame.HiddenOrMinimized(minimized: false).Should().Be(
            "The window is hidden, so it has no frame on the screen. Show it, then retry includeFrame true.");
    }

    // The default capture refuses a minimized window too: the compositor still held the frame it drew before it was
    // minimized, which came back as if current (#2416). Headless can't minimize a window, so the wording is pinned here.
    [Fact]
    public void The_client_capture_refuses_a_minimized_window_and_says_how_to_restore_it()
    {
        SnapshotComposer.MinimizedRefusal("Form1").Should().Be(
            "'Form1' is minimized, so nothing of it is on the screen, and the last frame it drew before it was minimized "
            + "would read as current; nothing was captured. Restore it with set_window_state "
            + "{\"state\":\"Normal\",\"window\":\"Form1\"}, then capture it.");
    }

    [Fact]
    public void A_copy_says_how_much_other_windows_cover_and_which_is_on_top()
    {
        NativeWindowFrame.CoverNote(Frame, [(new PixelRect(0, 0, 50, 50), "Elsewhere")])
            .Should().Be("Windows rendered nothing for this window, so its frame rectangle was copied from the screen; "
                + "no other window covers it.");
        NativeWindowFrame.CoverNote(Frame, [(new PixelRect(0, 0, 50, 50), "Elsewhere"), (new PixelRect(0, 0, 200, 1000), "Notepad"),
                (new PixelRect(0, 0, 1000, 1000), "")])
            .Should().Be("Windows rendered nothing for this window, so its frame rectangle was copied from the screen, and "
                + "other windows cover 100% of it, 'Notepad' on top: that part of the image shows them, not this window.");
        NativeWindowFrame.CoverNote(Frame, [(new PixelRect(299, 199, 50, 50), "")])
            .Should().Contain("cover 1% of it, an untitled window on top");
    }
}
