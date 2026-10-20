using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// select's read-back of a provider that throws (#1259). It fell back to selected, so a throwing peer was replied to
/// as chosen though nothing had been read, which undid what reading it back was for (#1182).
/// </summary>
public class SelectUnreadTests
{
    private static (ListBox List, Control Row) Shown(params string[] items)
    {
        var list = new ListBox { ItemsSource = items.Length > 0 ? items : new[] { "Alpha", "Beta" } };
        var window = new Window { Width = 300, Height = 300, Content = list };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (list, list.ContainerFromIndex(1)!);
    }

    private static ISelectionItemProvider Throwing(Action select)
    {
        var sip = Substitute.For<ISelectionItemProvider>();
        sip.When(p => p.Select()).Do(_ => select());
        sip.IsSelected.Returns(_ => throw new InvalidOperationException("peer gone"));
        return sip;
    }

    [AvaloniaFact]
    public void An_unread_selection_of_a_row_its_list_holds_is_read_from_the_list()
    {
        var (list, row) = Shown();

        UiAutomationDriver.SelectVerified(row, Throwing(() => list.SelectedIndex = 1), "Beta").Should().BeNull();
    }

    [AvaloniaFact]
    public void An_unread_selection_the_list_announced_and_put_back_is_refused()
    {
        // The announcement alone would have counted it, though the list holds Alpha.
        var (list, row) = Shown();
        list.SelectionChanged += (_, _) => { if (list.SelectedIndex == 1) list.SelectedIndex = 0; };

        var refused = UiAutomationDriver.SelectVerified(row, Throwing(() => list.SelectedIndex = 1), "Beta");

        refused!.Error.Should().StartWith("'Beta' was not selected: its list did not take the selection; 1 item is "
                                          + "selected now: ");
        list.SelectedIndex.Should().Be(0);
    }

    [AvaloniaFact]
    public void An_unread_selection_of_a_row_already_selected_is_taken()
    {
        // Select raises no SelectionChanged for a row that is already selected, and the refusal then said it did not
        // know, while naming the row as the one selected.
        var (list, row) = Shown();
        list.SelectedIndex = 1;

        UiAutomationDriver.SelectVerified(row, Throwing(() => list.SelectedIndex = 1), "Beta").Should().BeNull();
    }

    [AvaloniaFact]
    public void An_unread_selection_of_one_of_two_rows_of_one_value_is_read_by_row()
    {
        // The announcement compares values, so the list's choosing the other Beta read as this one's.
        var (list, row) = Shown("Beta", "Beta");

        var refused = UiAutomationDriver.SelectVerified(row, Throwing(() => list.SelectedIndex = 0), "Beta");

        refused!.Error.Should().StartWith("'Beta' was not selected");
    }

    [AvaloniaFact]
    public void An_unread_selection_of_a_row_its_list_let_go_unannounced_is_not_known()
    {
        var (list, row) = Shown();

        var refused = UiAutomationDriver.SelectVerified(row, Throwing(() => list.ItemsSource = new[] { "Gamma" }), "Beta");

        refused!.Success.Should().BeFalse();
        refused.Error.Should().Be("'Beta': select was sent, but its selection could not be read back, and its list no "
                                  + "longer holds it and did not announce it, so whether it took is not known; nothing is "
                                  + "selected now. Dump the list to see what is selected");
    }

    [AvaloniaFact]
    public void An_unread_selection_with_no_list_is_not_known_and_blames_no_list()
    {
        var item = new ListBoxItem { Content = "Loose" };
        var window = new Window { Width = 300, Height = 300, Content = item };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var refused = UiAutomationDriver.SelectVerified(item, Throwing(() => item.IsSelected = true), "Loose");

        refused!.Error.Should().Be("'Loose': select was sent, but its selection could not be read back, and it has no "
                                   + "list that reports its selection, so whether it took is not known. Dump it to see");
    }

    [AvaloniaFact]
    public void A_selection_read_back_as_not_taken_is_still_refused_as_such()
    {
        var (_, row) = Shown();
        var sip = Substitute.For<ISelectionItemProvider>();
        sip.IsSelected.Returns(false);

        var refused = UiAutomationDriver.SelectVerified(row, sip, "Beta");

        refused!.Error.Should().StartWith("'Beta' was not selected: its list did not take the selection");
    }
}
