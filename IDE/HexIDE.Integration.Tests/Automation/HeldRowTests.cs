using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// A virtualizing list keeps one row it has scrolled away from: its tab-once row, the one a Tab into the list lands on,
/// which selecting a row makes it. The row stays realized, arranged where it would be, outside the view, so the dump
/// listed it with <c>isOffscreen: true</c> after #1292 had left rows out of view out (#1981).
/// </summary>
public class HeldRowTests
{
    private static (Window Window, ListBox List) SelectedThenScrolledAway()
    {
        var items = new ObservableCollection<string>(Enumerable.Range(0, 30).Select(i => $"Row{i}"));
        var list = new ListBox
        {
            Height = 40,
            ItemsSource = items,
            ItemTemplate = new FuncDataTemplate<string>((s, _) => new Border { Height = 16, Child = new TextBlock { Text = s } }),
        };
        var window = new Window { Content = list, Width = 300, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        list.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        return (window, list);
    }

    private static void ScrollAway(ListBox list)
    {
        list.ScrollIntoView(15);
        list.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        list.ContainerFromIndex(0).Should().NotBeNull("the list must still hold the selected row for this to test anything");
        list.ContainerFromIndex(1).Should().BeNull("the rows around it must be scrolled out of view");
    }

    private static List<string?> Rows(ListBox list)
    {
        static IEnumerable<UiNode> Flat(UiNode n) => new[] { n }.Concat(n.Children.SelectMany(Flat));
        return Flat(UiAutomationDriver.Dump(list, "List", 8, interactiveOnly: false))
            .Where(n => n.ClassName == nameof(ListBoxItem)).Select(n => n.Name).ToList();
    }

    [AvaloniaFact]
    public void A_selected_row_scrolled_out_of_view_is_not_listed()
    {
        var (window, list) = SelectedThenScrolledAway();
        try
        {
            ScrollAway(list);

            var rows = Rows(list);
            rows.Should().NotContain("Row0");
            rows.Should().Contain("Row15");
        }
        finally { window.Close(); }
    }

    // A person's keys go to the row that has focus, wherever the list is scrolled, and the dump names it as focused.
    [AvaloniaFact]
    public void A_row_scrolled_out_of_view_with_focus_in_it_is_still_listed()
    {
        var (window, list) = SelectedThenScrolledAway();
        try
        {
            list.ContainerFromIndex(0)!.Focus();
            Dispatcher.UIThread.RunJobs();
            ScrollAway(list);
            list.ContainerFromIndex(0)!.IsKeyboardFocusWithin.Should().BeTrue();

            Rows(list).Should().Contain("Row0");
        }
        finally { window.Close(); }
    }

    // Selecting it by value scrolls to it and says so, as for any row out of view, rather than selecting it unseen.
    [AvaloniaFact]
    public void Selecting_the_held_row_by_value_scrolls_to_it_first()
    {
        var (window, list) = SelectedThenScrolledAway();
        try
        {
            list.SelectedIndex = -1;
            Dispatcher.UIThread.RunJobs();
            ScrollAway(list);

            var outcome = UiAutomationDriver.Interact(list, "select", "Row0");

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Detail.Should().Be("selected 'Row0' (it was scrolled out of view, so the list was scrolled to it first)");
            list.SelectedIndex.Should().Be(0);
        }
        finally { window.Close(); }
    }
}
