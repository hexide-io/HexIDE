using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.Automation;
using HexIDE.Desktop.Server;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// A dump whose tree comes back empty says why (#2725): a running form with no controls read the same as one whose text
/// interactiveOnly had left out, and a first-time caller couldn't tell which.
/// </summary>
public class EmptyDumpNoteTests
{
    private static Window Show(Control? content = null)
    {
        var window = new Window { Content = content };
        window.Show();
        return window;
    }

    [AvaloniaFact]
    public void A_window_that_holds_nothing_says_so_and_points_at_a_snapshot_for_what_is_drawn()
    {
        var window = Show();
        try
        {
            UiAutomationDriver.Dump(window, "Window", 20, interactiveOnly: true).Children.Should().BeEmpty();

            HexIdeTools.EmptyTreeNote(window, "Window", null, interactiveOnly: true)
                .Should().StartWith("This window holds no controls.").And.Contain("take_snapshot shows it");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_window_of_text_only_says_the_filter_left_it_out_and_how_to_see_it()
    {
        var window = Show(new StackPanel { Children = { new TextBlock { Text = "Score: 0" }, new TextBlock { Text = "Lives: 3" } } });
        try
        {
            UiAutomationDriver.Dump(window, "Window", 20, interactiveOnly: true).Children.Should().BeEmpty(
                "text is not interactive, which is the case this note exists for");

            HexIdeTools.EmptyTreeNote(window, "Window", null, interactiveOnly: true).Should().Be(
                "This window has nothing interactive below it, so interactiveOnly left out 2 parts that show, such as text: "
                + "pass interactiveOnly false to see them.");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_dump_from_a_root_names_the_root_and_a_hidden_part_counts_for_nothing()
    {
        var panel = new StackPanel { Children = { new TextBlock { Text = "hidden", IsVisible = false } } };
        var window = Show(panel);
        try
        {
            HexIdeTools.EmptyTreeNote(window, "Window", "Window/Pane", interactiveOnly: true)
                .Should().StartWith("'Window/Pane' holds no controls.");
        }
        finally { window.Close(); }
    }
}
