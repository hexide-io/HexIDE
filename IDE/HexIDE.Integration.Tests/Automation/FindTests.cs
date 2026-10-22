using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// dump_visual_tree's 'match' finds nodes without walking the whole tree back (#699). A first call has no path to scope
/// a dump with, and the whole IDE ran to 58,264 characters, over the client's limit, to answer "where is the editor?".
/// </summary>
public class FindTests
{
    private static Window Shown(params Control[] controls)
    {
        var panel = new StackPanel();
        foreach (var control in controls)
            panel.Children.Add(control);
        var window = new Window { Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Button Named(string name, string id)
    {
        var button = new Button { Content = name };
        AutomationProperties.SetAutomationId(button, id);
        return button;
    }

    [AvaloniaFact]
    public void A_node_answers_by_its_name_id_or_class_ignoring_case_and_comes_back_without_its_subtree()
    {
        var window = Shown(Named("Save", "SaveButton"), Named("Cancel", "CancelButton"), new TextBox { Text = "x" });
        try
        {
            var tree = UiAutomationDriver.Dump(window, "Window", 20, interactiveOnly: false);

            var (byName, total, unsearched) = UiAutomationDriver.Find(tree, "save", 50);
            byName.Select(n => n.Path).Should().Equal("Window/Button[SaveButton]");
            byName[0].Children.Should().BeEmpty("a match comes back without its subtree, the caption Text below it");
            (total, unsearched).Should().Be((1, 0));

            UiAutomationDriver.Find(tree, "cancelbutton", 50).Matches.Should().ContainSingle(n => n.Name == "Cancel");
            UiAutomationDriver.Find(tree, "TextBox", 50).Matches.Should().ContainSingle(n => n.ClassName == "TextBox");
            UiAutomationDriver.Find(tree, "Button", 50).Total.Should().Be(2);
            UiAutomationDriver.Find(tree, "nothing here", 50).Total.Should().Be(0);
        }
        finally { window.Close(); }
    }

    private sealed class PropertiesViewModel;

    // Every node below a view inherits its DataContext: the Properties window's scroll bar parts all answered to it.
    [AvaloniaFact]
    public void A_node_answers_by_its_DataContext_type_only_where_that_starts()
    {
        var view = new StackPanel { DataContext = new PropertiesViewModel() };
        AutomationProperties.SetAutomationId(view, "View");
        view.Children.Add(Named("Apply", "ApplyButton"));
        var window = Shown(view);
        try
        {
            var tree = UiAutomationDriver.Dump(window, "Window", 20, interactiveOnly: false);

            UiAutomationDriver.Find(tree, "PropertiesViewModel", 50).Matches.Select(n => n.AutomationId)
                .Should().Equal("View");
        }
        finally { window.Close(); }
    }

    // A tree item's own context, of the type its parent item's has: Locals and Options nest them so.
    [AvaloniaFact]
    public void A_context_of_the_same_type_that_starts_again_below_answers_and_a_subtree_dumped_below_one_does_not()
    {
        var outer = new StackPanel { DataContext = new PropertiesViewModel() };
        AutomationProperties.SetAutomationId(outer, "Outer");
        var inner = new StackPanel { DataContext = new PropertiesViewModel() };
        AutomationProperties.SetAutomationId(inner, "Inner");
        var apply = Named("Apply", "ApplyButton");
        inner.Children.Add(apply);
        outer.Children.Add(inner);
        var window = Shown(outer);
        try
        {
            var tree = UiAutomationDriver.Dump(window, "Window", 20, interactiveOnly: false);
            UiAutomationDriver.Find(tree, "PropertiesViewModel", 50).Matches.Select(n => n.AutomationId)
                .Should().Equal("Outer", "Inner");

            var subtree = UiAutomationDriver.Dump(apply, "Window/#ApplyButton", 20, interactiveOnly: false);
            UiAutomationDriver.Find(subtree, "PropertiesViewModel", 50).Total
                .Should().Be(0, "the button inherits the context it was handed, though the dump starts there");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Matches_come_in_tree_order_a_nested_one_before_a_later_sibling()
    {
        var group = new StackPanel();
        AutomationProperties.SetAutomationId(group, "Group");
        group.Children.Add(Named("Item nested", "ItemNested"));
        var window = Shown(group, Named("Item after", "ItemAfter"));
        try
        {
            var tree = UiAutomationDriver.Dump(window, "Window", 20, interactiveOnly: false);

            UiAutomationDriver.Find(tree, "Item", 50).Matches.Select(n => n.AutomationId)
                .Should().Equal("ItemNested", "ItemAfter");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Past_the_limit_the_first_matches_in_tree_order_are_listed_and_all_are_counted()
    {
        var window = Shown([.. Enumerable.Range(1, 5).Select(i => Named($"Item {i}", $"Item{i}"))]);
        try
        {
            var tree = UiAutomationDriver.Dump(window, "Window", 20, interactiveOnly: false);

            var (matches, total, _) = UiAutomationDriver.Find(tree, "item", 3);

            matches.Select(n => n.Name).Should().Equal("Item 1", "Item 2", "Item 3");
            total.Should().Be(5);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_branch_cut_off_at_maxDepth_is_counted_as_not_searched()
    {
        var inner = new StackPanel();
        AutomationProperties.SetAutomationId(inner, "Inner");
        inner.Children.Add(Named("Deep", "DeepButton"));
        var window = Shown(inner);
        try
        {
            var shallow = UiAutomationDriver.Dump(window, "Window", 1, interactiveOnly: false);

            var (matches, _, unsearched) = UiAutomationDriver.Find(shallow, "Deep", 50);

            matches.Should().BeEmpty();
            unsearched.Should().Be(1);

            // The cut-off node itself is searched, and comes back as every match does, without its subtree or the mark.
            var cut = UiAutomationDriver.Find(shallow, "Inner", 50).Matches.Should().ContainSingle().Subject;
            cut.Truncated.Should().BeNull();
        }
        finally { window.Close(); }
    }
}
