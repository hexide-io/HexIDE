using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

public class AnimationDrawingTests : WindowTestFixture
{
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           ClientHeight = 3015
           ClientWidth = 4560
           Begin MSComCtl2.Animation Animation1
              Left = 120
              Top = 120
              Width = 2400
              Height = 1200
           End
           Begin MSComCtl2.Animation Animation2
              Left = 1800
              Top = 120
              Width = 2400
              Height = 1200
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private static string Clip(int width = 260)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".avi");
        // Bottom-up 24-bit rows: magenta background, red foreground at x=1.
        int stride = (width * 3 + 3) & ~3;
        var frame = new byte[stride * 2];
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < width; x++)
            {
                frame[y * stride + x * 3] = (byte)(x == 1 ? 0 : 255);
                frame[y * stride + x * 3 + 2] = 255;
            }
        File.WriteAllBytes(path, AnimationRuntimeTests.Avi(24, 0, [frame], width));
        return path;
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Open_changes_only_the_window_size_and_Close_collapses_it(bool center)
    {
        var (canvas, _, _, _) = ContainerRuntimeHarness.Laid(Form);
        var animation = (VBAnimation)ContainerRuntimeHarness.Child(canvas, "Animation1");
        animation.LoadProperties([("Center", new Vb6Value(center))]);
        var path = Clip();
        try
        {
            animation.Call("Open", [new Vb6Value(path)]);
            canvas.UpdateLayout();
            animation.Bounds.Should().Be(new Rect(8, 8, center ? 160 : 260, center ? 80 : 2));
            animation.Width.Should().Be(160);
            animation.Height.Should().Be(80);
            Draw(animation, 160, 80).Where((_, i) => i % 4 == 3).Should().Contain(alpha => alpha != 0);
            animation.Call("Close", []);
            canvas.UpdateLayout();
            animation.Bounds.Size.Should().Be(new Size(0, 0));
            animation.Width.Should().Be(0);
            animation.Height.Should().Be(0);
        }
        finally { animation.Call("Close", []); File.Delete(path); }
    }

    [AvaloniaTheory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    public void Draws_background_foreground_and_centered_bands(bool center, int backStyle)
    {
        var animation = new VBAnimation { Width = 6, Height = 4 };
        animation.LoadProperties([("Center", new Vb6Value(center)), ("BackStyle", new Vb6Value((long)backStyle)),
            ("BackColor", new Vb6Value(65280L))]);
        var path = Clip(4);
        try
        {
            animation.Call("Open", [new Vb6Value(path)]);
            animation.Call("Play", [new Vb6Value(1L), new Vb6Value(0L), new Vb6Value(0L)]);
            animation.Measure(new Size(6, 4));
            animation.Arrange(new Rect(0, 0, 6, 4));
            var pixels = Draw(animation, 6, 4);
            byte[] Pixel(int x, int y) => pixels.Skip((y * 6 + x) * 4).Take(4).ToArray();
            int left = center ? 1 : 0, top = center ? 1 : 0;
            Pixel(left, top).Should().Equal(backStyle == 0 ? new byte[] { 0, 255, 0, 255 } : new byte[] { 255, 0, 255, 255 });
            Pixel(left + 1, top).Should().Equal(0, 0, 255, 255);
            if (center) Pixel(0, 0).Should().Equal(0, 255, 0, 255);
        }
        finally { animation.Call("Close", []); File.Delete(path); }
    }

    [AvaloniaFact]
    public void Center_clips_a_larger_frame_without_scaling_it()
    {
        var animation = new VBAnimation { Width = 2, Height = 2 };
        animation.LoadProperties([("Center", new Vb6Value(true)), ("BackStyle", new Vb6Value(1L))]);
        var path = Clip(4);
        try
        {
            animation.Call("Open", [new Vb6Value(path)]);
            animation.Call("Play", [new Vb6Value(1L), new Vb6Value(0L), new Vb6Value(0L)]);
            animation.Measure(new Size(2, 2));
            animation.Arrange(new Rect(0, 0, 2, 2));
            Draw(animation, 4, 2).Take(16).Should().Equal(0, 0, 255, 255, 255, 0, 255, 255,
                0, 0, 0, 0, 0, 0, 0, 0);
        }
        finally { animation.Call("Close", []); File.Delete(path); }
    }

    [AvaloniaFact]
    public void Rendered_frame_follows_Play_ranges_ticks_and_Stop()
    {
        var animation = new VBAnimation { Width = 1, Height = 2 };
        animation.LoadProperties([("BackStyle", new Vb6Value(1L))]);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".avi");
        File.WriteAllBytes(path, AnimationRuntimeTests.Avi(24, 0,
            Enumerable.Range(0, 10).Select(n => new byte[] { (byte)(n * 20), 0, 255, 0, (byte)(n * 20), 0, 255, 0 }).ToArray()));
        try
        {
            animation.Call("Open", [new Vb6Value(path)]);
            animation.Measure(new Size(1, 2));
            animation.Arrange(new Rect(0, 0, 1, 2));
            void Play(long count, long start, long end) => animation.Call("Play",
                [new Vb6Value(count), new Vb6Value(start), new Vb6Value(end)]);
            void Frame(int index) => Draw(animation, 1, 2).Take(4).Should().Equal((byte)(index * 20), 0, 255, 255);
            Play(1, 3, 3); Frame(3);
            Play(1, 9, 9); Frame(9);
            Play(1, 20, 20); Frame(9);
            Play(1, 5, 2); animation.AdvanceFrame(); Frame(5);
            Play(1, 0, -1);
            for (int i = 0; i < 10; i++) animation.AdvanceFrame();
            Frame(9);
            Play(2, 7, 9);
            for (int i = 0; i < 6; i++) animation.AdvanceFrame();
            Frame(9);
            Play(-1, 0, -1); animation.AdvanceFrame(); Frame(1);
            animation.Call("Stop", []); animation.AdvanceFrame(); Frame(1);
        }
        finally { animation.Call("Close", []); File.Delete(path); }
    }

    private static byte[] Draw(VBAnimation animation, int width, int height)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        bitmap.Render(animation);
        var pixels = new byte[width * height * 4];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(0, 0, width, height), pin.AddrOfPinnedObject(), pixels.Length, width * 4); }
        finally { pin.Free(); }
        return pixels;
    }

    [AvaloniaTheory]
    [InlineData("MSComCtl2.Animation")]
    [InlineData("VB.TextBox")]
    public void Earlier_control_covers_neighbour_and_hits_only_its_resized_window(string type)
    {
        var (canvas, recorder, _, _) = ContainerRuntimeHarness.Recorded(Form.Replace("Begin MSComCtl2.Animation Animation2", $"Begin {type} Animation2"));
        var animation = (VBAnimation)ContainerRuntimeHarness.Child(canvas, "Animation1");
        var neighbour = ContainerRuntimeHarness.Child(canvas, "Animation2");
        var window = (Window)TopLevel.GetTopLevel(animation)!;
        var path = Clip();
        try
        {
            animation.Call("Open", [new Vb6Value(path)]);
            canvas.UpdateLayout();
            animation.ZIndex.Should().BeGreaterThan(neighbour.ZIndex);
            recorder.Calls.Clear();
            var point = animation.TranslatePoint(new Point(122, 1), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            recorder.Calls.Should().Contain(c => c.Name == "Animation1_Click");
            recorder.Calls.Should().NotContain(c => c.Name == "Animation2_Click");
            recorder.Calls.Clear();
            point = animation.TranslatePoint(new Point(122, 20), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            recorder.Calls.Should().Contain(c => c.Name == "Animation2_Click");
            animation.Call("Close", []);
            canvas.UpdateLayout();
            recorder.Calls.Clear();
            point = animation.TranslatePoint(new Point(122, 1), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            recorder.Calls.Should().Contain(c => c.Name == "Animation2_Click");
            recorder.Calls.Should().NotContain(c => c.Name == "Animation1_Click");
        }
        finally { animation.Call("Close", []); File.Delete(path); }
    }
}
