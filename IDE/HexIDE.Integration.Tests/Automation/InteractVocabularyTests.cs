using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

    // #545: an editor's scroller is a template part, below the control a caller aims at, and walking only
    // upward never found it: "nothing to scroll" about a document showing a fraction of itself.
    [AvaloniaFact]
    public void Scroll_on_a_control_finds_the_scroller_in_its_own_template_and_names_it()
    {
        var box = new TextBox
        {
            AcceptsReturn = true,
            Height = 80,
            Text = string.Join("\n", Enumerable.Range(1, 200).Select(i => $"line {i}")),
        };
        var window = Show(box);
        try
        {
            var inner = box.GetVisualDescendants().OfType<ScrollViewer>().Single(v => v.TemplatedParent == box);

            var outcome = UiAutomationDriver.Interact(box, "scroll", "down");

            outcome.Success.Should().BeTrue(outcome.Error);
            inner.Offset.Y.Should().BeGreaterThan(0);
            outcome.Detail.Should().Contain("the target's own scroller");
            UiAutomationDriver.DescribeProviders(ControlAutomationPeer.CreatePeerForElement(box), box)
                .Should().Contain("scroll", "scroll works on it, so the tree has to say so for a caller to try");
        }
        finally { window.Close(); }
    }

    public sealed record GridRow(string Name);

    private static (Window window, DataGrid grid, ScrollBar bar) ShowLongGrid()
    {
        var grid = new DataGrid
        {
            ItemsSource = Enumerable.Range(1, 200).Select(i => new GridRow("row " + i)).ToList(),
            AutoGenerateColumns = true,
            Height = 150,
        };
        var window = Show(grid);
        var bar = grid.GetVisualDescendants().OfType<ScrollBar>()
            .Single(b => b.TemplatedParent == grid && b.Orientation == Orientation.Vertical);
        return (window, grid, bar);
    }

    private static int FirstShownRow(DataGrid grid) =>
        grid.GetVisualDescendants().OfType<DataGridRow>().Where(r => r.IsVisible).Min(r => r.Index);

    // #545: a DataGrid's peer offers no scroll provider and its template has no ScrollViewer, so scroll said a
    // grid of two hundred rows had nothing taller than its viewport.
    [AvaloniaFact]
    public void Scroll_on_a_data_grid_pages_its_rows()
    {
        var (window, grid, bar) = ShowLongGrid();
        try
        {
            UiAutomationDriver.DescribeProviders(ControlAutomationPeer.CreatePeerForElement(grid), grid)
                .Should().Contain("scroll");

            var outcome = UiAutomationDriver.Interact(grid, "scroll", "down");
            Dispatcher.UIThread.RunJobs();

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Detail.Should().StartWith("scrolled 'DataGrid' down");
            FirstShownRow(grid).Should().BeGreaterThan(1, "a page is the rows in view, not the ten pixels of the bar's own step");

            var row = FirstShownRow(grid);
            var before = bar.Value;
            var line = UiAutomationDriver.Interact(grid, "scroll", "line_down");
            line.Success.Should().BeTrue(line.Error);
            (bar.Value - before).Should().BeGreaterThan(bar.SmallChange,
                "the reply reads the position straight away, so the grid's row step has to have landed by then");
            line.Detail.Should().Contain("line_down");
            Dispatcher.UIThread.RunJobs();
            FirstShownRow(grid).Should().Be(row + 1, "a line on a grid is a row");
        }
        finally { window.Close(); }
    }

    // #545: setting a DataGrid's bar moved the bar and answered success, and the rows stayed where they were,
    // because the grid acts on the bar's Scroll event rather than its value.
    [AvaloniaFact]
    public void Set_range_value_on_a_data_grid_scroll_bar_moves_the_rows()
    {
        var (window, grid, bar) = ShowLongGrid();
        try
        {
            var outcome = UiAutomationDriver.Interact(bar, "set_range_value", "500");
            Dispatcher.UIThread.RunJobs();

            outcome.Success.Should().BeTrue(outcome.Error);
            FirstShownRow(grid).Should().BeGreaterThan(0, "the rows have to move, not only the bar");
            // A grid moves by whole rows, so the reply has to say where it landed as well as what was asked.
            outcome.Detail.Should().Contain($"the bar is now at {bar.Value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}")
                .And.Contain("asked for 500").And.Contain($"row {FirstShownRow(grid)} at the top");

            UiAutomationDriver.Interact(bar, "set_range_value", "0").Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            FirstShownRow(grid).Should().Be(0, "going back up is the other direction ScrollIntoView has to serve");
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
            // "NaN" parses as a double and fails both bound comparisons, so it used to reach the control. (#545)
            ErrorOf(slider, "set_range_value", "NaN").Should().Contain("as a number");
            ErrorOf(slider, "set_range_value", "Infinity").Should().Contain("as a number");
            slider.Value.Should().Be(10);
        }
        finally { window.Close(); }
    }

    // #545: the peer's SetValue wrote a local value over the binding that keeps a scroll bar in step with its
    // viewer. The view scrolled once, and the bar never followed it again.
    [AvaloniaFact]
    public void Set_range_value_on_a_scroll_bar_moves_its_viewer_and_the_bar_keeps_following_it()
    {
        var (window, viewer, _) = ShowOverflowing();
        try
        {
            var bar = viewer.GetVisualDescendants().OfType<ScrollBar>()
                .Single(b => b.TemplatedParent == viewer && b.Orientation == Orientation.Vertical);

            var outcome = UiAutomationDriver.Interact(bar, "set_range_value", "300");

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Detail.Should().Contain("scrolled").And.Contain("through its scroll bar");
            Dispatcher.UIThread.RunJobs();
            viewer.Offset.Y.Should().Be(300);

            viewer.Offset = new Avalonia.Vector(0, 100);
            Dispatcher.UIThread.RunJobs();
            bar.Value.Should().Be(100, "the bar is still bound to its viewer after being set");
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
