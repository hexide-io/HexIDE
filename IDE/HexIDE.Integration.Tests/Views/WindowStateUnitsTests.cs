using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Desktop.Server;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// get_window_state says what its numbers measure (#2454). It gave x and y in screen pixels and width and height in
/// device-independent pixels, with nothing to say so, nor which edge x and y were: on a 150% display a maximized IDE read
/// -11, -11, and a form's place could not be checked against the twips its Left and Top name.
/// </summary>
public class WindowStateUnitsTests
{
    private static Window Shown(PixelPoint at)
    {
        var window = new Window { Width = 300, Height = 200, Position = at, WindowStartupLocation = WindowStartupLocation.Manual };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void Each_edge_is_given_in_device_independent_pixels_beside_the_scale_and_the_units_say_so()
    {
        var window = Shown(new PixelPoint(120, 80));
        try
        {
            var reply = HexIdeTools.WindowStateOf(window, "Form1");

            reply.State.Should().Be("Normal");
            (reply.X, reply.Y, reply.Width, reply.Height).Should().Be((120, 80, 300, 200), "the four kept for set_window_state");
            reply.Scale.Should().Be(window.DesktopScaling);
            var origin = window.PointToScreen(new Point(0, 0));
            reply.Client.Should().Be(new WindowRect(origin.X / window.DesktopScaling, origin.Y / window.DesktopScaling, 300, 200));
            // Windows' own rectangles are read from the window's HWND, which the headless platform has none of, nor a frame
            // size: so here, as on any platform that reports no frame, the two are left out, and the units say so.
            window.FrameSize.Should().BeNull("the headless platform reports no frame");
            reply.Outer.Should().BeNull();
            reply.Frame.Should().BeNull();
            reply.Units.Should().Be(HexIdeTools.WindowStateUnits).And.Contain("where the platform reports no frame");
            reply.Units.Should().Contain("screen pixels").And.Contain("device-independent pixels").And.Contain("twips divided by 15");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_minimized_window_has_no_edges_on_the_screen_to_give()
    {
        var window = Shown(new PixelPoint(120, 80));
        try
        {
            window.WindowState = WindowState.Minimized;
            Dispatcher.UIThread.RunJobs();

            var reply = HexIdeTools.WindowStateOf(window, "Form1");

            reply.State.Should().Be("Minimized");
            reply.Outer.Should().BeNull();
            reply.Frame.Should().BeNull();
            reply.Client.Should().BeNull();
            reply.Units.Should().Contain("None is given for a window that is hidden or minimized").And.Contain("-32000");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_hidden_window_has_none_either()
    {
        var window = Shown(new PixelPoint(120, 80));
        try
        {
            window.Hide();
            Dispatcher.UIThread.RunJobs();

            var reply = HexIdeTools.WindowStateOf(window, "Form1");

            reply.Outer.Should().BeNull();
            reply.Frame.Should().BeNull();
            reply.Client.Should().BeNull();
        }
        finally { window.Close(); }
    }

    [Fact]
    public void Screen_pixels_become_device_independent_ones_to_a_tenth()
    {
        // What a 150% display gave live: the form's outer rectangle at 720, 360 screen pixels is 480, 240.
        WindowRect.Of(new PixelRect(720, 360, 478, 357), 1.5).Should().Be(new WindowRect(480, 240, 318.7, 238));
        WindowRect.Of(new PixelPoint(-11, 34), new Size(1280, 649.333), 1.5).Should().Be(new WindowRect(-7.3, 22.7, 1280, 649.3));
    }
}
