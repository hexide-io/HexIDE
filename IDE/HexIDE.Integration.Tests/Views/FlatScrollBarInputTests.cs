using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using HexIDE.Runtime.BuiltinTypes;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

public class FlatScrollBarInputTests
{
    private static void Set(VBFlatScrollBar bar, string name, int value) => bar.TrySetProperty(name, new Vb6Value(value));
    private static (Window Window, VBFlatScrollBar Bar, ContainerRuntimeHarness.RecordingRoot Root, TextBox Text) Host()
    {
        var bar = new VBFlatScrollBar { Width = 160, Height = 17 };
        VBProps.SetName(bar, "Flat1");
        Set(bar, "Max", 100);
        Set(bar, "LargeChange", 10);
        Set(bar, "Value", 50);
        var text = new TextBox { Width = 80, Height = 24 };
        var canvas = new Canvas();
        canvas.Children.Add(bar);
        canvas.Children.Add(text);
        Canvas.SetTop(text, 30);
        var root = new ContainerRuntimeHarness.RecordingRoot { Child = canvas };
        var window = new Window { Width = 240, Height = 210, Content = root };
        window.Show();
        window.UpdateLayout();
        text.Focus();
        root.Calls.Clear();
        return (window, bar, root, text);
    }
    private static Point At(VBFlatScrollBar bar, Window window, double x, double y = 8) => bar.TranslatePoint(new Point(x, y), window)!.Value;

    [AvaloniaFact]
    public void Arrow_track_and_drag_dispatch_measured_events_without_taking_focus()
    {
        var (window, bar, root, text) = Host();
        try
        {
            void Click(double x) { window.MouseDown(At(bar, window, x), MouseButton.Left); window.MouseUp(At(bar, window, x), MouseButton.Left); }
            Click(152);
            bar.Value.Should().Be(51);
            Click(8);
            bar.Value.Should().Be(50);
            Click(125);
            bar.Value.Should().Be(60);
            Set(bar, "Value", 50);
            Click(30);
            bar.Value.Should().Be(40);
            root.Calls.Select(c => c.Name).Should().OnlyContain(n => n == "Flat1_Change");
            text.IsFocused.Should().BeTrue();
            Set(bar, "Value", 50);
            root.Calls.Clear();
            window.MouseDown(At(bar, window, 80), MouseButton.Left);
            root.Calls.Select(c => c.Name).Should().Equal("Flat1_Scroll");
            bar.Value.Should().Be(50);
            foreach (var x in new[] { 90, 100, 110, 120 })
            {
                window.MouseMove(At(bar, window, x));
                bar.Value.Should().Be(new[] { 59, 68, 77, 86 }[(x - 90) / 10]);
            }
            window.MouseUp(At(bar, window, 120), MouseButton.Left);
            root.Calls.Select(c => c.Name).Should().Equal("Flat1_Scroll", "Flat1_Scroll", "Flat1_Scroll", "Flat1_Scroll", "Flat1_Scroll", "Flat1_Change");
            text.IsFocused.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1, 152, 8, 49)]
    [InlineData(2, 8, 152, 51)]
    public void Arrows_disables_one_button(int arrows, int blocked, int allowed, int expected)
    {
        var (window, bar, root, _) = Host();
        try
        {
            Set(bar, "Arrows", arrows);
            window.MouseDown(At(bar, window, blocked), MouseButton.Left);
            window.MouseUp(At(bar, window, blocked), MouseButton.Left);
            bar.Value.Should().Be(50);
            root.Calls.Should().BeEmpty();
            window.MouseDown(At(bar, window, allowed), MouseButton.Left);
            window.MouseUp(At(bar, window, allowed), MouseButton.Left);
            bar.Value.Should().Be(expected);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Tab_and_keyboard_leave_the_bar_out_of_the_focus_order()
    {
        var (window, bar, _, text) = Host();
        try
        {
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            bar.IsFocused.Should().BeFalse();
            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.PageDown, RawInputModifiers.None);
            bar.Value.Should().Be(50);
            text.Focus();
            window.MouseDown(At(bar, window, 152), MouseButton.Left);
            window.MouseUp(At(bar, window, 152), MouseButton.Left);
            text.IsFocused.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Vertical_arrows_step_and_disabled_input_does_nothing()
    {
        var (window, bar, root, _) = Host();
        try
        {
            Set(bar, "Orientation", 0);
            window.UpdateLayout();
            window.MouseDown(At(bar, window, 8, 152), MouseButton.Left);
            window.MouseUp(At(bar, window, 8, 152), MouseButton.Left);
            bar.Value.Should().Be(51);
            bar.IsEnabled = false;
            root.Calls.Clear();
            window.MouseDown(At(bar, window, 8, 8), MouseButton.Left);
            window.MouseUp(At(bar, window, 8, 8), MouseButton.Left);
            bar.Value.Should().Be(51);
            root.Calls.Should().BeEmpty();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Held_arrow_repeats_and_release_stops_it()
    {
        var (window, bar, root, _) = Host();
        try
        {
            window.MouseDown(At(bar, window, 152), MouseButton.Left);
            var context = System.Threading.ExecutionContext.Capture();
            var frame = new DispatcherFrame();
            DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(1050));
            Dispatcher.UIThread.PushFrame(frame);
            System.Threading.ExecutionContext.Restore(context!);
            window.MouseUp(At(bar, window, 152), MouseButton.Left);
            bar.Value.Should().Be(61);
            root.Calls.Should().HaveCount(11);
            frame = new DispatcherFrame();
            DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(200));
            Dispatcher.UIThread.PushFrame(frame);
            System.Threading.ExecutionContext.Restore(context!);
            bar.Value.Should().Be(61);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Drawing_has_checkerboard_outline_hover_drag_and_disabled_states(int appearance)
    {
        var (window, bar, _, _) = Host();
        try
        {
            Set(bar, "Appearance", appearance);
            int Pixel(int x, int y)
            {
                window.UpdateLayout();
                window.CaptureRenderedFrame()?.Dispose();
                using var bitmap = window.CaptureRenderedFrame();
                bitmap.Should().NotBeNull();
                using var buffer = bitmap!.Lock();
                var point = At(bar, window, x, y);
                return Marshal.ReadInt32(buffer.Address, (int)point.Y * buffer.RowBytes + (int)point.X * 4) & 0xffffff;
            }
            Pixel(25, 5).Should().NotBe(Pixel(26, 5));
            Pixel(0, 5).Should().Be(appearance == 0 ? 0xe3e3e3 : 0xa0a0a0);
            Pixel(8, 8).Should().Be(0x000000);
            Set(bar, "Arrows", 2);
            Pixel(8, 8).Should().Be(0xa0a0a0);
            Set(bar, "Arrows", 1);
            Pixel(152, 8).Should().Be(0xa0a0a0);
            Pixel(8, 8).Should().Be(0x000000);
            Set(bar, "Arrows", 0);
            window.MouseMove(At(bar, window, 8));
            if (appearance == 2) Pixel(0, 5).Should().Be(0xffffff);
            if (appearance == 1)
            {
                var face = ((ISolidColorBrush)VBColor.FromSystemColor(VbSystemColor.Btnface).ToBrush()).Color;
                Pixel(1, 5).Should().Be((face.R << 16) | (face.G << 8) | face.B);
            }
            window.MouseDown(At(bar, window, 80), MouseButton.Left);
            Pixel(80, 5).Should().Be(0xa0a0a0);
            window.MouseUp(At(bar, window, 80), MouseButton.Left);
            bar.IsEnabled = false;
            Pixel(80, 5).Should().NotBe(Pixel(81, 5));
        }
        finally { window.Close(); }
    }
}
