using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Automation.Peers;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// Guards hexide-io/HexIDE#361: the provider tokens <c>dump_visual_tree</c> reports and the actions
/// <c>interact</c> accepts are two lists, and nothing related them. <c>scroll</c> and <c>rangeValue</c> were
/// reported on 40 of the 256 nodes in the default tree and reached no action at all. The first test is the
/// one that stops the two lists drifting apart again; the rest pin what the two new actions do.
/// </summary>
public class InteractVocabularyTests
{
    private static Window Show(Control content, double height = 240)
    {
        var window = new Window { Content = content, Width = 320, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>One control per provider token, so every token is emitted at least once.</summary>
    private static Window ShowOneOfEverything()
    {
        var panel = new StackPanel
        {
            Children =
            {
                new Button { Content = "Click" },
                new ListBox { ItemsSource = new[] { "one", "two" } },
                new ComboBox { ItemsSource = new[] { "one", "two" } },
                new TabControl { Items = { new TabItem { Header = "A" }, new TabItem { Header = "B" } } },
                new TextBox { Text = "hi" },
                new CheckBox(),
                new Expander { Header = "More", Content = new TextBlock { Text = "inside" } },
                new Slider { Minimum = 0, Maximum = 100 },
                new ScrollViewer { Height = 50, Content = new Border { Height = 500 } },
            },
        };
        return Show(panel, height: 600);
    }

    private static string? ErrorOf(Control control, string verb, string? value = null) =>
        UiAutomationDriver.Interact(control, verb, value).Error;

    [AvaloniaFact]
    public void Every_reported_provider_token_reaches_an_action_interact_accepts()
    {
        var window = ShowOneOfEverything();
        try
        {
            var emitted = new Dictionary<string, Control>();
            foreach (var control in window.GetVisualDescendants().OfType<Control>())
            {
                var peer = ControlAutomationPeer.CreatePeerForElement(control);
                foreach (var token in UiAutomationDriver.DescribeProviders(peer, control))
                    emitted.TryAdd(token, control);
            }

            emitted.Keys.Should().BeEquivalentTo(UiAutomationDriver.VerbsByProvider.Keys,
                "the fixture exists to make every token appear, so a token it cannot produce is untested");

            foreach (var (token, control) in emitted)
                foreach (var verb in UiAutomationDriver.VerbsByProvider[token])
                    (ErrorOf(control, verb) ?? string.Empty).Should().NotStartWith("unknown action",
                        $"'{token}' is reported on {control.GetType().Name} and promises '{verb}'");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Every_listed_action_is_one_the_switch_handles()
    {
        var window = Show(new Button { Content = "Click" });
        try
        {
            UiAutomationDriver.VerbsByProvider.Values.SelectMany(v => v)
                .Should().BeSubsetOf(UiAutomationDriver.Verbs);

            foreach (var verb in UiAutomationDriver.Verbs.Where(v => v != "invoke"))
                (ErrorOf((Control)window.Content!, verb) ?? string.Empty).Should().NotStartWith("unknown action", verb);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void An_unknown_action_lists_the_new_actions()
    {
        var window = Show(new Button());
        try
        {
            ErrorOf((Control)window.Content!, "wiggle").Should()
                .Contain("set_range_value").And.Contain("scroll").And.Contain("double_click");
        }
        finally { window.Close(); }
    }

    // ── scroll ────────────────────────────────────────────────────────────────────────────────────

    private static (Window window, ScrollViewer viewer, Border content) ShowOverflowing()
    {
        var content = new Border { Height = 1000 };
        var viewer = new ScrollViewer { Content = content };
        return (Show(viewer, height: 200), viewer, content);
    }

    [AvaloniaFact]
    public void Scroll_on_content_moves_the_nearest_container_that_scrolls_and_says_where_it_is()
    {
        var (window, viewer, content) = ShowOverflowing();
        try
        {
            var outcome = UiAutomationDriver.Interact(content, "scroll", "down");

            outcome.Success.Should().BeTrue(outcome.Error);
            viewer.Offset.Y.Should().BeGreaterThan(0);
            outcome.Detail.Should().Contain("ScrollViewer").And.Contain("nearest container").And.Contain("% of the way along");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Scroll_end_reaches_the_bottom_and_a_further_page_down_says_it_cannot_move()
    {
        var (window, viewer, _) = ShowOverflowing();
        try
        {
            UiAutomationDriver.Interact(viewer, "scroll", "end").Success.Should().BeTrue();
            viewer.Offset.Y.Should().BeApproximately(viewer.Extent.Height - viewer.Viewport.Height, 0.5);

            var again = UiAutomationDriver.Interact(viewer, "scroll", "down");
            again.Success.Should().BeFalse();
            again.Error.Should().Contain("already at that end").And.Contain("100% of the way along");

            UiAutomationDriver.Interact(viewer, "scroll", "home").Success.Should().BeTrue();
            viewer.Offset.Y.Should().Be(0);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Scroll_where_nothing_overflows_says_so_rather_than_succeeding()
    {
        var content = new Border { Height = 20 };
        var window = Show(new ScrollViewer { Content = content });
        try
        {
            ErrorOf(content, "scroll", "down").Should().StartWith("nothing to scroll vertically");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Scroll_without_a_direction_lists_the_directions()
    {
        var (window, viewer, _) = ShowOverflowing();
        try
        {
            ErrorOf(viewer, "scroll", "sideways").Should().Contain("line_up").And.Contain("home|end");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_list_that_fits_and_a_tree_node_do_not_advertise_scroll()
    {
        var list = new ListBox { ItemsSource = new[] { "one" } };
        var node = new TreeViewItem { Header = "node" };
        var window = Show(new StackPanel { Children = { list, new TreeView { Items = { node } } } });
        try
        {
            foreach (Control control in new Control[] { list, node })
                UiAutomationDriver.DescribeProviders(ControlAutomationPeer.CreatePeerForElement(control), control)
                    .Should().NotContain("scroll", $"{control.GetType().Name} has nothing to scroll");
        }
        finally { window.Close(); }
    }

    // ── set_range_value ───────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Set_range_value_sets_a_slider_and_reports_the_value_it_took()
    {
        var slider = new Slider { Minimum = 0, Maximum = 100, Orientation = Orientation.Horizontal };
        var window = Show(slider);
        try
        {
            var outcome = UiAutomationDriver.Interact(slider, "set_range_value", "37.5");

            outcome.Success.Should().BeTrue(outcome.Error);
            slider.Value.Should().Be(37.5);
            outcome.Detail.Should().Contain("37.5").And.Contain("0..100");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Set_range_value_refuses_out_of_range_and_non_numbers_and_leaves_the_value_alone()
    {
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = 10 };
        var window = Show(slider);
        try
        {
            ErrorOf(slider, "set_range_value", "150").Should().Contain("outside").And.Contain("0..100");
            ErrorOf(slider, "set_range_value", "fifty").Should().Contain("as a number");
            ErrorOf(slider, "set_range_value").Should().Contain("as a number");
            slider.Value.Should().Be(10);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Set_range_value_on_a_range_with_no_width_refuses_rather_than_succeeding()
    {
        // Measured on the running IDE: a scroll bar whose content had come to fit reported 0..0, and setting
        // it to 0 answered success for a change that could not happen.
        var slider = new Slider { Minimum = 0, Maximum = 0 };
        var window = Show(slider);
        try
        {
            UiAutomationDriver.DescribeProviders(ControlAutomationPeer.CreatePeerForElement(slider), slider)
                .Should().NotContain("rangeValue");
            ErrorOf(slider, "set_range_value", "0").Should().Contain("nothing to set").And.Contain("0..0");
        }
        finally { window.Close(); }
    }

    // #550: inspect_element reported a range control's provider but not its state, so the bounds could only be
    // learned from set_range_value's refusal and a value only confirmed from a snapshot.
    [AvaloniaFact]
    public void Inspecting_a_range_control_reports_its_value_and_bounds()
    {
        var slider = new Slider { Minimum = 10, Maximum = 90, Value = 25 };
        var window = Show(slider);
        try
        {
            UiAutomationDriver.Inspect(slider, "Window/Slider").Range
                .Should().Be(new RangeState(25, 10, 90, IsReadOnly: false));

            UiAutomationDriver.Interact(slider, "set_range_value", "60").Error.Should().BeNull();
            UiAutomationDriver.Inspect(slider, "Window/Slider").Range!.Value
                .Should().Be(60, "reading back what set_range_value set is the point of reporting it");

            UiAutomationDriver.Inspect(new Button(), "Window/Button").Range
                .Should().BeNull("a control with no range reports none rather than zeros");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_progress_bar_is_not_advertised_as_settable_and_says_why_when_asked()
    {
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = 40 };
        var window = Show(bar);
        try
        {
            UiAutomationDriver.DescribeProviders(ControlAutomationPeer.CreatePeerForElement(bar), bar)
                .Should().NotContain("rangeValue");
            ErrorOf(bar, "set_range_value", "50").Should().Contain("read-only");
        }
        finally { window.Close(); }
    }
}
