using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using HexIDE.Automation;
using HexIDE.Desktop.Server;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// hover reports where an opened tip is, in the window, beside the point it hovered (#1303). The tip closes when the hover
/// ends, before any snapshot could show it, so placement could only be guessed.
/// </summary>
public class TipPlacementTests
{
    [AvaloniaFact]
    public void An_open_tip_is_placed_in_the_window_and_a_closed_one_is_not()
    {
        var button = new Button
        {
            Content = "Go", Width = 80, Height = 30, Margin = new Thickness(40, 30, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        };
        ToolTip.SetTip(button, "Add Form");
        ToolTip.SetPlacement(button, PlacementMode.Bottom);
        ToolTip.SetVerticalOffset(button, 0);
        var window = new Window { Width = 300, Height = 200, Content = button };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            UiAutomationDriver.OpenTipBounds(button, window).Should().BeNull();

            ToolTip.SetIsOpen(button, true);
            Dispatcher.UIThread.RunJobs();

            var bounds = UiAutomationDriver.OpenTipBounds(button, window);
            bounds.Should().NotBeNull();
            var tip = bounds!.Value;
            tip.Width.Should().BeGreaterThan(0);
            tip.Height.Should().BeGreaterThan(0);
            // Just below the button and within its width, where Bottom placement puts it. Headless, the box measured 8 in from
            // the button's left edge; the reply reports where it is, not where placement aimed it.
            tip.Y.Should().BeApproximately(60, 1);
            tip.X.Should().BeInRange(40, 120);
        }
        finally
        {
            ToolTip.SetIsOpen(button, false);
            window.Close();
        }
    }

    [Theory]
    [InlineData(104, 85, 100, 64, "its top-left 4 right of and 21 below the point hovered")]
    [InlineData(90.25, 40, 100, 64, "its top-left 9.8 left of and 24 above the point hovered")]
    public void The_offset_from_the_point_hovered_reads_as_directions(double x, double y, double px, double py, string expected)
    {
        HexIdeTools.TipPlacement(new Rect(x, y, 60, 20), new Point(px, py)).Should().Be(expected);
    }
}
