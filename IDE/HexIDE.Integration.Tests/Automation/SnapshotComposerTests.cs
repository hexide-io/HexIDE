using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// Guards that a snapshot shows what is actually on screen (the remaining half of gap 12).
///
/// <para>A popup is not drawn into its parent window — it is realised in its own top-level root — so
/// rendering the window alone produced a picture of a menu bar with no menu. The structural half of gap 12
/// made a menu's contents readable; this makes them visible.</para>
///
/// <para><b>What these can and cannot prove.</b> Avalonia hosts a popup either as its own top-level window
/// or as an overlay inside its parent, and <b>headless always chooses the overlay</b> — measured:
/// <c>TopLevel.GetTopLevel(popup.Child)</c> returns the window itself. The defect therefore cannot occur
/// here at all, and a naive "the popup's colour appears in the picture" test passes with the fix removed,
/// which is exactly the vacuous guard worth not shipping.</para>
///
/// <para>So these cover the overlay path — where the composer's job is to stay out of the way and NOT draw
/// an already-rendered popup a second time — and the desktop path is verified against the running IDE
/// instead. They render for real under Skia (<c>UseHeadlessDrawing = false</c> in <c>TestApp</c>), as
/// <c>ClassicRenderTests</c> does.</para>
/// </summary>
public class SnapshotComposerTests
{
    // A colour no chrome uses, so finding it proves the popup's own content was drawn rather than
    // something underneath happening to be that shade.
    private static readonly Color PopupMarker = Color.FromRgb(255, 0, 255);

    private static (Window window, Popup popup) WindowWithPopup(bool open)
    {
        var popup = new Popup
        {
            Placement = PlacementMode.Bottom,
            Child = new Border { Width = 80, Height = 40, Background = new SolidColorBrush(PopupMarker) },
        };
        var anchor = new Border { Width = 120, Height = 30, Background = Brushes.White, Child = popup };
        var window = new Window { Width = 300, Height = 200, Content = anchor, Background = Brushes.White };
        window.Show();
        popup.PlacementTarget = anchor;
        popup.IsOpen = open;
        return (window, popup);
    }

    /// <summary>
    /// Is <paramref name="colour"/> anywhere in the composed image?
    /// </summary>
    /// <remarks>
    /// Pixels come back via <c>CopyPixels</c> into managed memory — no unsafe block, and no assumption
    /// about what the bitmap is backed by. BGRA byte order, so red is at +2.
    /// </remarks>
    private static bool Contains(Avalonia.Media.Imaging.RenderTargetBitmap bitmap, Color colour)
    {
        var size = bitmap.PixelSize;
        var stride = size.Width * 4;
        var pixels = new byte[stride * size.Height];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels,
            System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(default, size), handle.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            if (pixels[i + 2] == colour.R && pixels[i + 1] == colour.G && pixels[i] == colour.B)
                return true;
        }
        return false;
    }

    [AvaloniaFact]
    public void A_window_with_nothing_open_still_captures()
    {
        var (window, _) = WindowWithPopup(open: false);

        using var bitmap = SnapshotComposer.Capture(window);

        bitmap.Should().NotBeNull();
        bitmap!.PixelSize.Width.Should().BeGreaterThan(0);
        bitmap.PixelSize.Height.Should().BeGreaterThan(0);
    }

    [AvaloniaFact]
    public void A_closed_popup_does_not_appear()
    {
        var (window, _) = WindowWithPopup(open: false);

        using var bitmap = SnapshotComposer.Capture(window);

        Contains(bitmap!, PopupMarker).Should().BeFalse("nothing is open, so nothing should be drawn over the window");
    }

    [AvaloniaFact]
    public void An_overlay_popup_is_already_in_the_window_and_still_appears()
    {
        // Headless hosts popups as overlays, so this passes with or without the composer. Kept as a
        // statement of the invariant that matters on such platforms — the picture must show the popup —
        // and NOT as evidence the composer works; that is the live check's job.
        var (window, _) = WindowWithPopup(open: true);

        using var bitmap = SnapshotComposer.Capture(window);

        Contains(bitmap!, PopupMarker).Should().BeTrue("an open popup is on screen and belongs in the picture");
    }

    [AvaloniaFact]
    public void An_overlay_popup_does_not_widen_the_canvas()
    {
        // The discriminating one on this platform. An overlay popup is already inside the window's own
        // render; composing it again would draw it twice and grow the canvas for content that never left
        // the window. Remove the "skip anything whose top-level is this window" guard and this fails.
        var (window, _) = WindowWithPopup(open: false);
        using var windowOnly = SnapshotComposer.Capture(window);

        var (window2, popup2) = WindowWithPopup(open: false);
        popup2.Child = new Border { Width = 400, Height = 600, Background = new SolidColorBrush(PopupMarker) };
        popup2.IsOpen = true;
        using var withPopup = SnapshotComposer.Capture(window2);

        withPopup!.PixelSize.Should().Be(windowOnly!.PixelSize,
            "an overlay popup is part of the window already, however big it is");
    }
}
