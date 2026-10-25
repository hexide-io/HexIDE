using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Threading;

namespace HexIDE.Integration.Tests.Controls;

public class DirectionalIconTests
{
    private static byte[] Render(Control icon, FlowDirection direction)
    {
        var window = new Window
        {
            Width = 32, Height = 32, Background = Brushes.White,
            FlowDirection = direction, Content = icon,
        };
        window.Styles.Add(new StyleInclude(new Uri("avares://HexIDE/"))
        {
            Source = new Uri("avares://HexIDE/Themes/IconDirection.axaml"),
        });
        window.Show();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        frame.Should().NotBeNull();
        var bytes = new byte[32 * 32 * 4];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { frame!.CopyPixels(new PixelRect(0, 0, 32, 32), handle.AddrOfPinnedObject(), bytes.Length, 128); }
        finally { handle.Free(); window.Close(); }
        // Compare painted silhouettes rather than Skia's subpixel antialiasing at mirrored edges.
        for (var i = 0; i < bytes.Length; i += 4)
        {
            var ink = bytes[i] < 128 ? (byte)0 : (byte)255;
            bytes[i] = bytes[i + 1] = bytes[i + 2] = ink;
        }
        bytes.Distinct().Count().Should().BeGreaterThan(1, "the icon must actually paint");
        return bytes;
    }

    private static Path Arrow(string classes = "directional") => new()
    {
        Data = Geometry.Parse("M2,2 L14,8 L2,14 Z"), Fill = Brushes.Black,
        Width = 16, Height = 16, Stretch = Stretch.Uniform,
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        Classes = { classes },
    };

    private static byte[] Mirror(byte[] source)
    {
        var result = new byte[source.Length];
        for (var y = 0; y < 32; y++)
        for (var x = 0; x < 32; x++)
            Array.Copy(source, (y * 32 + x) * 4, result, (y * 32 + 31 - x) * 4, 4);
        return result;
    }

    [AvaloniaFact]
    public void DrawnArrow_FollowsWindowDirection()
    {
        Render(Arrow(), FlowDirection.RightToLeft).Should().Equal(Mirror(Render(Arrow(), FlowDirection.LeftToRight)));
    }

    [AvaloniaFact]
    public void Play_KeepsItsDirection()
    {
        Render(Arrow("play"), FlowDirection.RightToLeft).Should().Equal(Render(Arrow("play"), FlowDirection.LeftToRight));
    }

    // Ignore position changes from font bearings or menu layout; compare the painted shapes.
    private static byte[] InkShape(byte[] pixels)
    {
        var points = Enumerable.Range(0, 32 * 32).Where(i => pixels[i * 4] == 0).ToArray();
        points.Should().NotBeEmpty();
        var left = points.Min(i => i % 32);
        var right = points.Max(i => i % 32);
        var top = points.Min(i => i / 32);
        var bottom = points.Max(i => i / 32);
        var width = right - left + 1;
        var height = bottom - top + 1;
        var result = new byte[2 + width * height];
        result[0] = (byte)width;
        result[1] = (byte)height;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            result[2 + y * width + x] = pixels[((top + y) * 32 + left + x) * 4];
        return result;
    }

    [AvaloniaFact]
    public void MenuPlay_KeepsItsDirection()
    {
        static MenuItem Item() => new() { Icon = Arrow(), Classes = { "play" } };
        InkShape(Render(Item(), FlowDirection.RightToLeft)).Should()
            .Equal(InkShape(Render(Item(), FlowDirection.LeftToRight)));
    }

    [AvaloniaFact]
    public void TextArrow_MirrorsInRtl()
    {
        static TextBlock Glyph(string text) => new()
        {
            Text = text, Classes = { "directional" }, FontSize = 16,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        InkShape(Render(Glyph("▶"), FlowDirection.RightToLeft)).Should().Equal(InkShape(Render(Glyph("◀"), FlowDirection.LeftToRight)));
    }
}
