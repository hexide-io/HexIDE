using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using HexIDE.Runtime.BuiltinControls;

namespace HexIDE.Integration.Tests.Views;

public class SliderInputTests
{
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin MSComctlLib.Slider Slider1
              Width = 3000
              Height = 675
           End
        End
        Attribute VB_Name = "Form1"
        """;

    [AvaloniaTheory]
    [InlineData(Key.Right, 1, "Scroll")]
    [InlineData(Key.Down, 1, "Scroll")]
    [InlineData(Key.Left, 0, "Scroll")]
    [InlineData(Key.Up, 0, "Scroll")]
    [InlineData(Key.PageDown, 5, "Scroll")]
    [InlineData(Key.PageUp, 0, "Scroll")]
    [InlineData(Key.Home, 0, "Change")]
    [InlineData(Key.End, 10, "Change")]
    public async Task Keys_raise_measured_events_even_at_the_endpoint(Key key, int value, string during)
    {
        var (canvas, recorder, ctx, env) = ContainerRuntimeHarness.Recorded(Form);
        var slider = (VBSlider)ContainerRuntimeHarness.Child(canvas, "Slider1");
        var window = (Window)TopLevel.GetTopLevel(slider)!;
        try
        {
            slider.Focus();
            recorder.Calls.Clear();
            window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
            recorder.Calls.Select(c => c.Name).Should().Equal("Slider1_KeyDown", "Slider1_" + during,
                "Slider1_KeyUp", "Slider1_Change");
            (await ContainerRuntimeHarness.Run(ctx, env, "Debug.Print Slider1.Value")).Single().Value.Should().Be((long)value);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Custom_steps_and_wheel_use_SmallChange_and_LargeChange()
    {
        var (canvas, recorder, ctx, env) = ContainerRuntimeHarness.Recorded(Form);
        var slider = (VBSlider)ContainerRuntimeHarness.Child(canvas, "Slider1");
        var window = (Window)TopLevel.GetTopLevel(slider)!;
        try
        {
            await ContainerRuntimeHarness.Run(ctx, env, "Slider1.SmallChange = 2\r\nSlider1.LargeChange = 7");
            slider.Focus();
            window.KeyPress(Key.PageDown, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.PageDown, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Left, RawInputModifiers.None, PhysicalKey.None, null);
            (await ContainerRuntimeHarness.Run(ctx, env, "Debug.Print Slider1.Value")).Single().Value.Should().Be(5L);
            recorder.Calls.Clear();
            window.MouseWheel(slider.TranslatePoint(new Point(100, 20), window)!.Value, new Vector(0, 1));
            recorder.Calls.Select(c => c.Name).Should().Equal("Slider1_Scroll");
            (await ContainerRuntimeHarness.Run(ctx, env, "Debug.Print Slider1.Value")).Single().Value.Should().Be(3L);
            recorder.Calls.Clear();
            window.MouseWheel(slider.TranslatePoint(new Point(100, 20), window)!.Value, new Vector(0, -1));
            recorder.Calls.Select(c => c.Name).Should().Equal("Slider1_Scroll");
            slider.Value.Should().Be(5);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Mouse_release_finishes_the_gesture(bool drag, bool outside)
    {
        var (canvas, recorder, _, _) = ContainerRuntimeHarness.Recorded(Form);
        var slider = (VBSlider)ContainerRuntimeHarness.Child(canvas, "Slider1");
        var window = (Window)TopLevel.GetTopLevel(slider)!;
        try
        {
            var start = slider.TranslatePoint(new Point(10, slider.Bounds.Height / 2), window)!.Value;
            slider.Text = "the tip";
            window.MouseDown(start, MouseButton.Left);
            ToolTip.GetIsOpen(slider).Should().BeTrue();
            if (drag)
            {
                window.MouseMove(start + new Vector(130, 0));
                slider.Value.Should().Be(7);
                recorder.Calls.Count(c => c.Name == "Slider1_Scroll").Should().Be(8);
                window.MouseMove(start);
                slider.Value.Should().Be(0);
            }
            window.MouseUp(outside ? start + new Vector(220, 0) : start, MouseButton.Left);
            ToolTip.GetIsOpen(slider).Should().BeFalse();
            ToolTip.GetTip(slider).Should().BeNull();
            var calls = recorder.Calls.Select(c => c.Name).Where(n => !n.EndsWith("MouseMove", StringComparison.Ordinal)).ToArray();
            calls[0].Should().Be("Slider1_MouseDown");
            calls[1].Should().Be("Slider1_Scroll");
            calls.Skip(2).TakeWhile(n => n != "Slider1_MouseUp").Should().OnlyContain(n => n == "Slider1_Scroll");
            calls.Skip(Array.IndexOf(calls, "Slider1_MouseUp")).Should().Equal(outside
                ? ["Slider1_MouseUp", "Slider1_Scroll", "Slider1_Change"]
                : ["Slider1_MouseUp", "Slider1_Click", "Slider1_Scroll", "Slider1_Change"]);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Drawing_has_ticks_selection_and_disabled_thumb_without_pinning_pixels()
    {
        var slider = new VBSlider { Width = 200, Height = 45 };
        slider.Measure(new Size(200, 45));
        slider.Arrange(new Rect(0, 0, 200, 45));
        IEnumerable<GeometryDrawing> Flatten(Drawing drawing)
        {
            if (drawing is GeometryDrawing geometry) yield return geometry;
            if (drawing is DrawingGroup group)
                foreach (var child in group.Children)
                    foreach (var piece in Flatten(child)) yield return piece;
        }
        GeometryDrawing[] Draw()
        {
            var group = new DrawingGroup();
            using (var context = group.Open()) slider.Render(context);
            return Flatten(group).ToArray();
        }
        slider.Max = 20;
        slider.TickFrequency = 3;
        var ticks = Draw().Count(d => d.Brush == null && d.Pen != null);
        ticks.Should().Be(8);
        Draw().Where(d => d.Brush == null).Should().OnlyContain(d => d.Geometry!.Bounds.Top > slider.Bounds.Height / 2);
        slider.TickStyle = 1;
        Draw().Where(d => d.Brush == null).Should().OnlyContain(d => d.Geometry!.Bounds.Bottom < slider.Bounds.Height / 2);
        slider.TickStyle = 2;
        Draw().Count(d => d.Brush == null && d.Pen != null).Should().Be(16);
        slider.TickStyle = 3;
        Draw().Should().NotContain(d => d.Brush == null && d.Pen != null);
        slider.SelectRange = true;
        slider.SelectionStart = 3;
        slider.SelectionLength = 6;
        Draw().Should().Contain(d => d.Brush == Brushes.Blue);
        var enabled = Draw().Length;
        slider.IsEnabled = false;
        Draw().Length.Should().BeGreaterThan(enabled);
        slider.BorderStyle = 1;
        Draw().Count(d => d.Brush == null).Should().Be(1);
        slider.BorderStyle = 0;
        slider.TickStyle = 0;
        slider.Orientation = 1;
        slider.Arrange(new Rect(0, 0, 45, 200));
        Draw().Where(d => d.Brush == null).Should().OnlyContain(d => d.Geometry!.Bounds.Left > slider.Bounds.Width / 2);
        slider.TickStyle = 1;
        Draw().Where(d => d.Brush == null).Should().OnlyContain(d => d.Geometry!.Bounds.Right < slider.Bounds.Width / 2);
    }

    [AvaloniaTheory]
    [InlineData(MouseButton.Left, 10)]
    [InlineData(MouseButton.Right, 0)]
    public async Task Channel_pages_and_right_click_does_not_move_the_selection(MouseButton button, long expected)
    {
        var (canvas, recorder, ctx, env) = ContainerRuntimeHarness.Recorded(Form);
        var slider = (VBSlider)ContainerRuntimeHarness.Child(canvas, "Slider1");
        var window = (Window)TopLevel.GetTopLevel(slider)!;
        try
        {
            await ContainerRuntimeHarness.Run(ctx, env,
                "Slider1.SelectRange = True\r\nSlider1.SelStart = 2\r\nSlider1.SelLength = 3");
            var point = slider.TranslatePoint(new Point(slider.Bounds.Width * .8, slider.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, button);
            if (button == MouseButton.Left)
            {
                slider.Value.Should().Be(5, "a channel press pages rather than jumping to the pointer");
                var deadline = System.Diagnostics.Stopwatch.StartNew();
                while (slider.Value != 10 && deadline.Elapsed < TimeSpan.FromSeconds(2))
                    await Task.Delay(20, TestContext.Current.CancellationToken);
                slider.Value.Should().Be(10);
                await Task.Delay(200, TestContext.Current.CancellationToken);
                slider.Value.Should().Be(10, "holding beyond the pointer must not page back and forth");
            }
            window.MouseUp(point, button);
            recorder.Calls.Select(c => c.Name).Should().Equal(button == MouseButton.Left
                ? ["Slider1_MouseDown", "Slider1_Scroll", "Slider1_Scroll", "Slider1_MouseUp", "Slider1_Click", "Slider1_Change"]
                : ["Slider1_MouseDown", "Slider1_MouseUp", "Slider1_Click"]);
            var values = await ContainerRuntimeHarness.Run(ctx, env, "Debug.Print Slider1.Value, Slider1.SelStart, Slider1.SelLength");
            values.Select(v => v.Value).Should().Equal(expected, 2L, 3L);
        }
        finally { window.Close(); }
    }

}
