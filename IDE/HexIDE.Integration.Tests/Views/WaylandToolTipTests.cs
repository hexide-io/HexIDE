using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using HexIDE.Automation;
using HexIDE.Runtime.BuiltinControls;

namespace HexIDE.Integration.Tests.Views;

public class WaylandToolTipTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Default_tip_uses_its_windows_backend(bool wayland, bool runtime)
    {
        Control control;
        Window window;
        if (runtime)
        {
            var form = ContainerRuntimeHarness.Laid("""
                VERSION 5.00
                Begin VB.Form Form1
                   Begin VB.CommandButton Command1
                      Width = 3000
                      Height = 1000
                   End
                End
                Attribute VB_Name = "Form1"
                """);
            window = form.window;
            control = ContainerRuntimeHarness.Child(form.canvas, "Command1");
        }
        else
        {
            control = new Button();
            window = new Window { Content = control, Width = 300, Height = 150 };
        }
        NativeWayland.ForTest = wayland ? top => ReferenceEquals(top, window) : null;
        try
        {
            ToolTip.SetTip(control, "tip");
            ToolTip.SetShowDelay(control, 0);
            window.Show();
            window.MouseMove(control.TranslatePoint(new Point(20, 20), window)!.Value);
            await Task.Delay(100, TestContext.Current.CancellationToken);
            ToolTip.GetIsOpen(control).Should().BeTrue();
            ToolTip.GetPlacement(control).Should().Be(wayland ? PlacementMode.Bottom : PlacementMode.Pointer);
            ToolTip.SetIsOpen(control, false);
            ToolTip.GetPlacement(control).Should().Be(PlacementMode.Pointer);
        }
        finally { window.Close(); NativeWayland.ForTest = null; }
    }

    [AvaloniaFact]
    public void Sliders_own_placement_wins()
    {
        var slider = new VBSlider { Text = "tip" };
        var window = new Window { Content = slider, Width = 300, Height = 150 };
        NativeWayland.ForTest = _ => true;
        try
        {
            window.Show();
            window.MouseDown(new Point(10, slider.Bounds.Height / 2), Avalonia.Input.MouseButton.Left);
            ToolTip.GetIsOpen(slider).Should().BeTrue();
            ToolTip.GetPlacement(slider).Should().Be(PlacementMode.Top);
            window.MouseUp(new Point(10, slider.Bounds.Height / 2), Avalonia.Input.MouseButton.Left);
        }
        finally { window.Close(); NativeWayland.ForTest = null; }
    }
}
