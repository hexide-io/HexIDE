using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// interact's wheel turns the mouse wheel over its target through the window's input, so the control's own wheel handling
/// runs, and scroll names a scroll bar the control draws itself when it finds no scroller (#2576). A running form's
/// TreeView draws its own bar, so scroll answered that nothing was taller than its viewport, with 26 rows out of view.
/// </summary>
public class InteractWheelTests
{
    private static Window Show(Control content, double width = 320, double height = 200)
    {
        var window = new Window { Content = content, Width = width, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        // A wheel is hit-tested against what was drawn, so a frame must exist first, as click's tests need (#1065).
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>A control that draws its own vertical bar and moves it three rows a notch, as a TreeView does.</summary>
    private sealed class SelfScrolling : Panel
    {
        public ScrollBar Bar { get; } = new()
        {
            Orientation = Orientation.Vertical, Minimum = 0, Maximum = 26, ViewportSize = 4,
            HorizontalAlignment = HorizontalAlignment.Right, Width = 16,
        };

        public SelfScrolling()
        {
            Background = Avalonia.Media.Brushes.White;
            Children.Add(Bar);
            PointerWheelChanged += (_, e) =>
            {
                Bar.Value = Math.Clamp(Bar.Value - e.Delta.Y * 3, Bar.Minimum, Bar.Maximum);
                e.Handled = true;
            };
        }
    }

    [AvaloniaFact]
    public void A_wheel_down_moves_a_scroll_viewer_through_its_own_wheel_handling_and_says_how_far()
    {
        var content = new Border { Height = 1000, Background = Avalonia.Media.Brushes.White };
        var viewer = new ScrollViewer { Content = content };
        var window = Show(viewer);
        try
        {
            var outcome = UiAutomationDriver.Interact(content, "wheel", "down 3");

            outcome.Success.Should().BeTrue(outcome.Error);
            viewer.Offset.Y.Should().BeGreaterThan(0);
            outcome.Detail.Should().StartWith("turned the wheel 3 notches down at (")
                .And.Contain("went from 0% to").And.Contain("% of the way along");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_wheel_right_moves_a_wide_scroll_viewer_right()
    {
        var content = new Border { Width = 2000, Height = 100, Background = Avalonia.Media.Brushes.White };
        var viewer = new ScrollViewer
        {
            Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        var window = Show(viewer);
        try
        {
            var outcome = UiAutomationDriver.Interact(content, "wheel", "right");

            outcome.Success.Should().BeTrue(outcome.Error);
            viewer.Offset.X.Should().BeGreaterThan(0, outcome.Detail);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_wheel_at_the_top_says_nothing_moved_and_why_it_might_not()
    {
        var content = new Border { Height = 1000, Background = Avalonia.Media.Brushes.White };
        var window = Show(new ScrollViewer { Content = content });
        try
        {
            var outcome = UiAutomationDriver.Interact(content, "wheel", "up");

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Detail.Should().Contain("nothing that scrolls moved: it may already be at that end");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_wheel_over_a_control_that_draws_its_own_bar_reports_the_bar()
    {
        var control = new SelfScrolling();
        var window = Show(control);
        try
        {
            var outcome = UiAutomationDriver.Interact(control, "wheel", "down 2 40,40");

            outcome.Success.Should().BeTrue(outcome.Error);
            control.Bar.Value.Should().Be(6, "three rows a notch, two notches");
            outcome.Detail.Should().StartWith("turned the wheel 2 notches down at (40, 40)")
                .And.Contain("its scroll bar 'ScrollBar").And.Contain("went from 0 to 6, in a range of 0 to 26")
                .And.NotContain("as far as it goes");
        }
        finally { window.Close(); }
    }

    // Five notches where the bar has two to give: it stops, and the reply says it went as far as it goes.
    [AvaloniaFact]
    public void A_wheel_that_runs_into_the_end_of_a_bar_says_so()
    {
        var control = new SelfScrolling();
        var window = Show(control);
        try
        {
            control.Bar.Value = 6;

            var outcome = UiAutomationDriver.Interact(control, "wheel", "up 5 40,40");

            control.Bar.Value.Should().Be(0);
            outcome.Detail.Should().EndWith("went from 6 to 0, in a range of 0 to 26, which is as far as it goes");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Scroll_on_a_control_that_draws_its_own_bar_names_the_bar_and_the_routes_that_move_it()
    {
        var control = new SelfScrolling();
        var window = Show(control);
        try
        {
            var outcome = UiAutomationDriver.Interact(control, "scroll", "line_down");

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().StartWith("found nothing that scrolls vertically for '")
                .And.Contain("It draws a vertical scroll bar of its own, 'ScrollBar")
                .And.Contain("at 0, in a range of 0 to 26: set_range_value on that bar moves it")
                .And.Contain("wheel turns the mouse wheel over the control");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_wheel_value_that_is_not_a_direction_is_refused_with_what_wheel_takes()
    {
        var control = new SelfScrolling();
        var window = Show(control);
        try
        {
            var outcome = UiAutomationDriver.Interact(control, "wheel", "sideways");

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("'sideways' is not a direction").And.Contain("as 'down 3'");
            control.Bar.Value.Should().Be(0);
        }
        finally { window.Close(); }
    }
}
