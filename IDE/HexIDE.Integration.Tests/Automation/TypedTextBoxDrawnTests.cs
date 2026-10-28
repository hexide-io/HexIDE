using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// What type_text writes into a focused TextBox is drawn at once (#2737). The box held the new text, and its caret moved,
/// but it drew the previous text's glyphs until its text next changed: the Find dialog's box looked empty after a write.
/// These render for real under Skia (<c>UseHeadlessDrawing = false</c> in <c>TestApp</c>).
/// </summary>
public class TypedTextBoxDrawnTests
{
    [AvaloniaFact]
    public void A_write_into_a_focused_box_draws_its_text_as_a_box_given_that_text_does()
    {
        // The reference is given its text before it is shown, and is never focused, so it has no caret.
        var reference = new TextBox { Text = "WWWWWW", Width = 300 };
        var typed = new TextBox { Text = "", Width = 300 };
        var window = new Window { Content = new StackPanel { Children = { reference, typed } }, Width = 400, Height = 120 };
        window.Show();
        try
        {
            typed.Focus();
            Dispatcher.UIThread.RunJobs();

            UiAutomationDriver.TypeText(typed, "WWWWWW").Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame()!;
            var drawn = Ink(frame, window, typed);
            var expected = Ink(frame, window, reference);

            expected.Should().BeGreaterThan(100, "the reference draws six glyphs, and a caret alone is about 15 pixels");
            // The typed box may also draw its caret, which only adds ink; with the old text's glyphs it drew only that.
            drawn.Should().BeGreaterThanOrEqualTo(expected, "the typed box draws what it holds, as the reference does");
        }
        finally { window.Close(); }
    }

    /// <summary>The darker-than-mid-grey pixels inside <paramref name="box"/>'s border: its glyphs and any caret.</summary>
    private static int Ink(WriteableBitmap frame, Window window, TextBox box)
    {
        var size = frame.PixelSize;
        var stride = size.Width * 4;
        var pixels = new byte[stride * size.Height];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { frame.CopyPixels(new PixelRect(default, size), handle.AddrOfPinnedObject(), pixels.Length, stride); }
        finally { handle.Free(); }

        var scale = size.Width / window.Bounds.Width;
        var origin = box.TranslatePoint(new Point(0, 0), window)!.Value;
        var (x0, x1) = ((int)(origin.X * scale) + 4, (int)((origin.X + box.Bounds.Width) * scale) - 4);
        var (y0, y1) = ((int)(origin.Y * scale) + 4, (int)((origin.Y + box.Bounds.Height) * scale) - 4);
        var ink = 0;
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
            {
                var i = y * stride + x * 4;
                if (pixels[i] + pixels[i + 1] + pixels[i + 2] < 3 * 160)
                    ink++;
            }
        return ink;
    }
}
