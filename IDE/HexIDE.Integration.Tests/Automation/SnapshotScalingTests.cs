using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// A snapshot above 100% scaling shows the text a control draws after popping a clip (#2293). It was taken with a
/// RenderTargetBitmap at 96 × scaling DPI, and Avalonia.Skia scales the first transform pushed after a popped clip twice
/// there, so the text landed outside the bitmap: a running StatusBar's second panel came out blank while the screen
/// showed it, and a live check reported a fault that was not there.
/// </summary>
public class SnapshotScalingTests : WindowTestFixture
{
    // Two pieces of text, each in a clip of its own, as the StatusBar draws its panels.
    private sealed class TwoClippedTexts : Control
    {
        public override void Render(DrawingContext context)
        {
            context.FillRectangle(Brushes.White, new Rect(Bounds.Size));
            Draw(context, "AAAA", new Rect(4, 3, 120, 19));
            Draw(context, "BBBB", new Rect(190, 3, 90, 19));
        }

        private static void Draw(DrawingContext context, string text, Rect rect)
        {
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                Typeface.Default, 12, Brushes.Black);
            using var clip = context.PushClip(rect);
            context.DrawText(formatted, new Point(rect.X, rect.Y + 2));
        }
    }

    // How many dark pixels lie in a box given in device-independent units.
    private static int Dark(Bitmap image, Rect box, double scaling)
    {
        using var bitmap = SnapshotComposerTests.Readable(image);
        var stride = bitmap.PixelSize.Width * 4;
        var pixels = new byte[stride * bitmap.PixelSize.Height];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), pin.AddrOfPinnedObject(), pixels.Length, stride); }
        finally { pin.Free(); }
        var dark = 0;
        for (var y = (int)(box.Top * scaling); y < (int)(box.Bottom * scaling); y++)
            for (var x = (int)(box.Left * scaling); x < (int)(box.Right * scaling); x++)
            {
                var i = y * stride + x * 4;
                if (pixels[i] + pixels[i + 1] + pixels[i + 2] < 300)
                    dark++;
            }
        return dark;
    }

    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public async Task Text_drawn_after_a_popped_clip_is_in_the_snapshot(double scaling)
    {
        var window = new Window
        {
            Width = 400, Height = 300,
            Content = new Canvas { Children = { new TwoClippedTexts { Width = 400, Height = 25 } } },
        };
        window.Show();
        try
        {
            using var snapshot = await SnapshotComposerTests.Drawn(SnapshotComposer.ComposeAsync(window,
                new PixelRect(0, 0, (int)(400 * scaling), (int)(300 * scaling)), [], scaling));

            snapshot.Fallback.Should().BeNull();
            snapshot.Image.PixelSize.Should().Be(new PixelSize((int)(400 * scaling), (int)(300 * scaling)));
            var first = Dark(snapshot.Image, new Rect(4, 0, 120, 25), scaling);
            var second = Dark(snapshot.Image, new Rect(190, 0, 90, 25), scaling);
            first.Should().BeGreaterThan(0);
            second.Should().BeGreaterThan(0, "the text after the first clip is popped is drawn where the screen draws it");
        }
        finally { window.Close(); }
    }

    // A compositor that gives no picture in time costs the snapshot nothing but its fidelity, and the note says which.
    [AvaloniaFact]
    public async Task With_no_picture_from_the_compositor_a_render_target_draws_it_and_the_note_says_so()
    {
        var wait = SnapshotComposer.CompositorWait;
        SnapshotComposer.CompositorWait = TimeSpan.Zero;
        var window = new Window { Width = 200, Height = 100, Content = new Border { Background = Brushes.White } };
        window.Show();
        try
        {
            using var snapshot = await SnapshotComposerTests.Drawn(SnapshotComposer.ComposeAsync(window,
                new PixelRect(0, 0, 300, 150), [], 1.5));

            snapshot.Image.PixelSize.Should().Be(new PixelSize(300, 150));
            snapshot.Fallback.Should().StartWith("The compositor gave no picture of 'Border' within 0 s, so a render target drew it instead.")
                .And.Contain("can leave out text a control draws after popping a clip");
        }
        finally
        {
            SnapshotComposer.CompositorWait = wait;
            window.Close();
        }
    }
}
