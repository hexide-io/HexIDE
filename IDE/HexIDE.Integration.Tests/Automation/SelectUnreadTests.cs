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
    private static (ListBox List, Control Row) Shown()
    {
        var list = new ListBox { ItemsSource = new[] { "Alpha", "Beta" } };
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
    public void An_unread_selection_the_list_did_not_announce_is_reported_as_unknown()
    {
        var (list, row) = Shown();

        var refused = UiAutomationDriver.SelectVerified(row, Throwing(() => { }), "Beta");

        refused.Should().NotBeNull();
        refused!.Success.Should().BeFalse();
        refused.Error.Should().Be("'Beta': select was sent, but its selection could not be read back and its list did not "
                                  + "announce it, so whether it took is not known; nothing is selected now. Dump the list "
                                  + "to see what is selected");
        list.SelectedIndex.Should().Be(-1);
    }

    [AvaloniaFact]
    public void An_unread_selection_the_list_announced_is_taken()
    {
        var (list, row) = Shown();

        var refused = UiAutomationDriver.SelectVerified(row, Throwing(() => list.SelectedIndex = 1), "Beta");

        refused.Should().BeNull();
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
