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
public class SnapshotComposerTests : WindowTestFixture
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

    [AvaloniaTheory]
    [InlineData(80, 100, 20, 0, 1.0)]
    [InlineData(100, 80, 0, 20, 1.0)]
    [InlineData(80, 80, 20, 20, 1.5)]
    public void Overhanging_popup_shifts_the_window(int popupX, int popupY, int offsetX, int offsetY,
        double scaling)
    {
        var window = new Window { Width = 100, Height = 100, Background = Brushes.Blue };
        var popup = new Window { Width = 10, Height = 10, Background = Brushes.Magenta };
        window.Show();
        popup.Show();
        try
        {
            using var bitmap = SnapshotComposer.Compose(window,
                new PixelRect(100, 100, (int)(100 * scaling), (int)(100 * scaling)),
                new() { (popup, new PixelRect(popupX, popupY, (int)(10 * scaling), (int)(10 * scaling))) },
                scaling, out var offset);

            offset.Should().Be(new PixelPoint(offsetX, offsetY));
            // A strip before the shifted window must stay transparent, beyond the popup's own edge.
            var x = offsetX > 0 ? 0 : 50;
            var y = offsetY > 0 && offsetX == 0 ? 0 : 50;
            var pixel = new byte[4];
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixel,
                System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                bitmap.CopyPixels(new PixelRect(x, y, 1, 1), handle.AddrOfPinnedObject(), 4, 4);
                pixel[3].Should().Be(0, "the window must leave room for the popup overhang");
                bitmap.CopyPixels(new PixelRect(offsetX + 50, offsetY + 50, 1, 1),
                    handle.AddrOfPinnedObject(), 4, 4);
                // Blue, in the bitmap's own byte order: BGRA on Windows and Linux, RGBA on macOS. (Magenta, below,
                // reads the same either way.)
                var blue = bitmap.Format == Avalonia.Platform.PixelFormat.Rgba8888
                    ? new byte[] { 0, 0, 255, 255 }
                    : new byte[] { 255, 0, 0, 255 };
                pixel.Should().Equal(blue, "the shifted window must still be rendered");
                bitmap.CopyPixels(new PixelRect(popupX - 100 + offsetX + 5, popupY - 100 + offsetY + 5, 1, 1),
                    handle.AddrOfPinnedObject(), 4, 4);
                pixel.Should().Equal(new byte[] { 255, 0, 255, 255 }, "the popup must retain its relative position");
            }
            finally
            {
                handle.Free();
            }
        }
        finally
        {
            popup.Close();
            window.Close();
        }
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

    // A popup reported far from the window, at (-32000, -32000) for example, made the canvas the union of it and the
    // window, 32,000 pixels square, whose bytes overflow an Int32: "Positive number required" (#1960).
    [AvaloniaFact]
    public void A_popup_too_far_away_to_compose_is_left_out_and_named()
    {
        var window = new Window { Width = 100, Height = 100, Background = Brushes.Blue };
        var popup = new Window { Width = 10, Height = 10, Background = Brushes.Magenta };
        window.Show();
        popup.Show();
        try
        {
            var far = new PixelRect(-32000, -32000, 10, 10);
            using var bitmap = SnapshotComposer.Compose(window, new PixelRect(100, 100, 100, 100),
                new() { (popup, far) }, 1.0, screens: null, out var offset, out var leftOut);

            bitmap.PixelSize.Should().Be(new PixelSize(100, 100), "the window is captured on its own");
            offset.Should().Be(default(PixelPoint));
            var only = leftOut.Should().ContainSingle().Which;
            only.Rect.Should().Be(far);
            only.Reason.Should().StartWith("which would make the image 32200 × 32200 pixels");
        }
        finally
        {
            popup.Close();
            window.Close();
        }
    }

    [AvaloniaFact]
    public void A_popup_on_no_screen_is_left_out_and_one_on_a_screen_is_drawn()
    {
        var window = new Window { Width = 100, Height = 100, Background = Brushes.Blue };
        var popup = new Window { Width = 10, Height = 10, Background = Brushes.Magenta };
        window.Show();
        popup.Show();
        try
        {
            var screens = new[] { new PixelRect(0, 0, 400, 300) };
            var offScreen = new PixelRect(500, 100, 10, 10);
            var overhang = new PixelRect(80, 100, 10, 10);
            using var bitmap = SnapshotComposer.Compose(window, new PixelRect(100, 100, 100, 100),
                new() { (popup, offScreen), (popup, overhang) }, 1.0, screens, out var offset, out var leftOut);

            var only = leftOut.Should().ContainSingle().Which;
            only.Rect.Should().Be(offScreen);
            only.Reason.Should().Be("which is on none of the screens");
            bitmap.PixelSize.Should().Be(new PixelSize(120, 100), "the overhanging popup still widens the canvas");
            offset.Should().Be(new PixelPoint(20, 0));
        }
        finally
        {
            popup.Close();
            window.Close();
        }
    }

    // A menu hanging off one screen's edge, and a popup on a second screen, are on screen: only one on none is left out.
    [AvaloniaFact]
    public void A_popup_across_a_screen_edge_or_on_another_screen_is_drawn()
    {
        var window = new Window { Width = 100, Height = 100, Background = Brushes.Blue };
        var popup = new Window { Width = 10, Height = 10, Background = Brushes.Magenta };
        window.Show();
        popup.Show();
        try
        {
            var screens = new[] { new PixelRect(0, 0, 400, 300), new PixelRect(400, 0, 400, 300) };
            using var bitmap = SnapshotComposer.Compose(window, new PixelRect(100, 100, 100, 100),
                new() { (popup, new PixelRect(390, 100, 20, 10)), (popup, new PixelRect(500, 100, 10, 10)) }, 1.0, screens,
                out _, out var leftOut);

            leftOut.Should().BeEmpty();
            bitmap.PixelSize.Should().Be(new PixelSize(410, 100));
        }
        finally
        {
            popup.Close();
            window.Close();
        }
    }

    // A size cast from infinity is the largest Int32, and its right edge wrapped negative, so the union looked small and
    // the piece's own render target threw the same exception. Reckoned in longs, it is too large and left out.
    [AvaloniaFact]
    public void A_popup_of_the_largest_size_is_left_out_rather_than_wrapping()
    {
        var window = new Window { Width = 100, Height = 100, Background = Brushes.Blue };
        var popup = new Window { Width = 10, Height = 10, Background = Brushes.Magenta };
        window.Show();
        popup.Show();
        try
        {
            using var bitmap = SnapshotComposer.Compose(window, new PixelRect(100, 100, 100, 100),
                new() { (popup, new PixelRect(100, 100, int.MaxValue, 10)) }, 1.0, screens: null, out _, out var leftOut);

            leftOut.Should().ContainSingle().Which.Reason.Should().StartWith("which would make the image");
            bitmap.PixelSize.Should().Be(new PixelSize(100, 100));
        }
        finally
        {
            popup.Close();
            window.Close();
        }
    }
}
