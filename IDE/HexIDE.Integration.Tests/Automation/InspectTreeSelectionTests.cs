using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// inspect_element's selectionItems says what a tree has selected (#2288). A TreeView is no SelectingItemsControl, and its
/// peer offers no selection provider, so the field came back [] whatever was selected, which reads as nothing selected:
/// a live check of the Watches window could not tell a selected row from a cleared one.
/// </summary>
public class InspectTreeSelectionTests
{
    private sealed record Row(string Expression);

    [AvaloniaFact]
    public void A_tree_of_rows_reports_the_selected_row_and_nothing_once_it_is_cleared()
    {
        var first = new TreeViewItem { Header = "one" };
        var second = new TreeViewItem { Header = "two" };
        AutomationProperties.SetName(first, "one");
        AutomationProperties.SetName(second, "two");
        var tree = new TreeView { ItemsSource = new[] { first, second } };
        var window = new Window { Width = 320, Height = 240, Content = tree };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            tree.SelectedItem = second;
            UiAutomationDriver.Inspect(tree, "Window/Tree", window).SelectionItems.Should().Equal("two");

            UiAutomationDriver.Interact(second, "remove_from_selection", null).Success.Should().BeTrue();
            UiAutomationDriver.Inspect(tree, "Window/Tree", window).SelectionItems.Should().BeEmpty();
        }
        finally { window.Close(); }
    }

    // As the IDE's four trees are: rows bound to view models, shown through a template, and named by a binding to the
    // view model, which the Watches window takes from the expression.
    [AvaloniaFact]
    public void A_bound_tree_names_its_selection_as_the_row_reads()
    {
        var rows = new[] { new Row("x"), new Row("y") };
        var tree = new TreeView
        {
            ItemsSource = rows,
            ItemTemplate = new FuncTreeDataTemplate<Row>((row, _) => new TextBlock { Text = row.Expression }, _ => System.Array.Empty<Row>()),
        };
        tree.ContainerPrepared += (_, e) =>
        {
            if (tree.TreeItemFromContainer(e.Container) is Row row)
                AutomationProperties.SetName(e.Container, row.Expression);
        };
        var window = new Window { Width = 320, Height = 240, Content = tree };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            UiAutomationDriver.Inspect(tree, "Window/Tree", window).SelectionItems.Should().BeEmpty("nothing is selected yet");

            tree.SelectedItem = rows[1];
            Dispatcher.UIThread.RunJobs();

            UiAutomationDriver.Inspect(tree, "Window/Tree", window).SelectionItems.Should().Equal("y");
        }
        finally { window.Close(); }
    }
}
