using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// <c>select</c> with a value finds a list row that is scrolled out of view (#627). A list creates rows only for
/// what is on screen, so such a row had no container and nothing to match, and the refusal counted only the rows
/// on screen, as if the row did not exist.
/// </summary>
public class SelectScrolledOutRowTests
{
    private static (Window Window, ListBox List) ScrolledToTheMiddle()
    {
        var items = new ObservableCollection<string>(Enumerable.Range(0, 30).Select(i => $"Row{i}"));
        items[0] = "(Name)";
        var list = new ListBox
        {
            Height = 40,
            ItemsSource = items,
            ItemTemplate = new FuncDataTemplate<string>((s, _) => new Border { Height = 16, Child = new TextBlock { Text = s } }),
        };
        var window = new Window { Content = list, Width = 300, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        // The middle rather than the end: a search that checks every row ends near the end, so a list that
        // started there would look put back whether or not it was.
        list.ScrollIntoView(15);
        list.UpdateLayout();
        list.ContainerFromIndex(0).Should().BeNull("the first row must be scrolled out of view for this to test anything");
        return (window, list);
    }

    [AvaloniaFact]
    public void A_row_scrolled_out_of_view_is_scrolled_to_and_selected()
    {
        var (window, list) = ScrolledToTheMiddle();
        try
        {
            var outcome = UiAutomationDriver.Interact(list, "select", "(Name)");

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Detail.Should().Be("selected '(Name)' (it was scrolled out of view, so the list was scrolled to it first)");
            list.SelectedItem.Should().Be("(Name)");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_row_on_screen_is_selected_without_saying_it_was_scrolled_to()
    {
        var (window, list) = ScrolledToTheMiddle();
        try
        {
            var outcome = UiAutomationDriver.Interact(list, "select", "Row15");

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Detail.Should().Be("selected 'Row15'");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_miss_counts_every_item_and_leaves_the_list_where_it_was()
    {
        var (window, list) = ScrolledToTheMiddle();
        try
        {
            var before = list.Scroll!.Offset;

            var outcome = UiAutomationDriver.Interact(list, "select", "Nowhere");

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().StartWith("no selectable item matching 'Nowhere' among the 30 item(s)");
            list.Scroll!.Offset.Should().Be(before, "a search that found nothing must not leave the list scrolled somewhere else");
            list.SelectedItem.Should().BeNull();
        }
        finally { window.Close(); }
    }
}
