using System.Linq;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// Guards hexide-io/HexIDE#661: <c>select</c> replaces a selection, so a list that holds several at once, the
/// designer canvas among them, could be driven to one item or, through Ctrl+A, to all of them and nothing in
/// between. <c>add_to_selection</c> and <c>remove_from_selection</c> are what reach a chosen group.
/// </summary>
public class AddToSelectionTests
{
    private static (Window window, ListBox list) Show(SelectionMode mode)
    {
        var list = new ListBox { ItemsSource = new[] { "one", "two", "three" }, SelectionMode = mode };
        var window = new Window { Content = list, Width = 320, Height = 240 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, list);
    }

    private static Control Row(ListBox list, int index) => (Control)list.ContainerFromIndex(index)!;

    [AvaloniaFact]
    public void Adding_keeps_what_was_selected_and_the_reply_lists_the_selection()
    {
        var (window, list) = Show(SelectionMode.Multiple);
        try
        {
            UiAutomationDriver.Interact(Row(list, 0), "select", null).Success.Should().BeTrue();

            var outcome = UiAutomationDriver.Interact(Row(list, 2), "add_to_selection", null);

            outcome.Success.Should().BeTrue(outcome.Error);
            list.Selection.SelectedIndexes.Should().BeEquivalentTo([0, 2]);
            outcome.Detail.Should().Be("added 'three' to the selection; 2 items are selected now: one, three");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Removing_takes_out_only_the_target()
    {
        var (window, list) = Show(SelectionMode.Multiple);
        try
        {
            list.Selection.Select(0);
            list.Selection.Select(1);

            var outcome = UiAutomationDriver.Interact(Row(list, 0), "remove_from_selection", null);

            outcome.Success.Should().BeTrue(outcome.Error);
            list.Selection.SelectedIndexes.Should().BeEquivalentTo([1]);
            outcome.Detail.Should().Be("removed 'one' from the selection; 1 item is selected now: two");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_change_that_changes_nothing_says_so()
    {
        var (window, list) = Show(SelectionMode.Multiple);
        try
        {
            UiAutomationDriver.Interact(Row(list, 1), "remove_from_selection", null).Detail
                .Should().Be("'two' was not selected; nothing is selected now");

            list.Selection.Select(1);
            UiAutomationDriver.Interact(Row(list, 1), "add_to_selection", null).Detail
                .Should().Be("'two' was already selected; 1 item is selected now: two");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_single_selection_list_refuses_and_points_at_select_rather_than_replacing()
    {
        var (window, list) = Show(SelectionMode.Single);
        try
        {
            list.Selection.Select(0);

            var outcome = UiAutomationDriver.Interact(Row(list, 2), "add_to_selection", null);

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("one selection at a time").And.Contain("use select");
            list.Selection.SelectedIndexes.Should().BeEquivalentTo([0], "a refusal must leave the selection alone");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_token_is_reported_only_in_a_list_that_holds_several()
    {
        var (multiWindow, multi) = Show(SelectionMode.Multiple);
        var (singleWindow, single) = Show(SelectionMode.Single);
        try
        {
            string[] TokensOf(Control c) =>
                UiAutomationDriver.DescribeProviders(ControlAutomationPeer.CreatePeerForElement(c), c);

            TokensOf(Row(multi, 0)).Should().Contain("multiSelectItem");
            TokensOf(Row(single, 0)).Should().NotContain("multiSelectItem").And.Contain("selectionItem");
            TokensOf(multi).Should().NotContain("multiSelectItem", "the list itself is not an item in it");
        }
        finally
        {
            multiWindow.Close();
            singleWindow.Close();
        }
    }

    [AvaloniaFact]
    public void A_value_is_refused_rather_than_ignored()
    {
        var (window, list) = Show(SelectionMode.Multiple);
        try
        {
            var outcome = UiAutomationDriver.Interact(list, "add_to_selection", "two");

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("takes no value");
            list.Selection.SelectedIndexes.Should().BeEmpty();
        }
        finally { window.Close(); }
    }
}
