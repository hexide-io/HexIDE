using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

// Headless coverage for the Phase-5 read surface of the MCP automation tools (dump_visual_tree /
// inspect_element). The driver takes a root Control (not the live window) precisely so it can be
// exercised here without a running IDE or the HTTP loop. The thin active-window/dialog glue lives in
// HexIdeTools and is covered by the live MCP loop.
public class UiAutomationDriverTests
{
    private sealed class SampleVm
    {
        public ICommand DoThing { get; } = new NoopCommand();
        public string Caption { get; set; } = "hello";
        public int ReadOnlyNumber { get; } = 42;

        private sealed class NoopCommand : ICommand
        {
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) { }
            public event EventHandler? CanExecuteChanged { add { } remove { } }
        }
    }

    private static (Window window, Button button, TextBox textbox, CheckBox check, SampleVm vm) BuildShownWindow()
    {
        var button = new Button { Name = "TheButton", Content = "Click" };
        AutomationProperties.SetAutomationId(button, "btnId");
        var textbox = new TextBox { Name = "TheText", Text = "hi" };
        var check = new CheckBox { Name = "TheCheck" };

        var panel = new StackPanel();
        panel.Children.Add(button);
        panel.Children.Add(textbox);
        panel.Children.Add(check);

        var vm = new SampleVm();
        var window = new Window { Content = panel, Width = 320, Height = 240, DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, button, textbox, check, vm);
    }

    private static IEnumerable<UiNode> Flatten(UiNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var d in Flatten(child))
                yield return d;
    }

    [AvaloniaFact]
    public void Dump_FindsControls_WithExpectedProviders()
    {
        var (window, _, _, _, _) = BuildShownWindow();
        try
        {
            var tree = UiAutomationDriver.Dump(window, "Window", maxDepth: 20, interactiveOnly: false);
            var all = Flatten(tree).ToList();

            all.First(n => n.Name == "TheButton").Providers.Should().Contain("invoke");
            all.First(n => n.Name == "TheText").Providers.Should().Contain("value");
            all.First(n => n.Name == "TheCheck").Providers.Should().Contain("toggle");
        }
        finally { window.Close(); }
    }

    // #1069: a control nobody can see is a control nobody can press, so a provider or pointer action on it is
    // refused rather than carried out, and the refusal names the flag the dump already shows.
    [AvaloniaTheory]
    [InlineData("invoke", null)]
    [InlineData("click", null)]
    [InlineData("toggle", null)]
    [InlineData("set_value", "x")]
    public void Interact_OnAControlInsideAHiddenContainer_IsRefused_AndActsOnNothing(string action, string? value)
    {
        var clicks = 0;
        var button = new Button { Name = "Approve", Content = "Approve" };
        button.Click += (_, _) => clicks++;
        var check = new CheckBox { Name = "Hold" };
        var text = new TextBox { Name = "Note", Text = "" };
        var hidden = new StackPanel { IsVisible = false, Children = { button, check, text } };
        var window = new Window { Content = new StackPanel { Children = { hidden } }, Width = 200, Height = 150 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Control target = action switch { "toggle" => check, "set_value" => text, _ => button };
            var outcome = UiAutomationDriver.Interact(target, action, value);

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("is not showing").And.Contain("isHidden");
            clicks.Should().Be(0);
            check.IsChecked.Should().BeFalse();
            text.Text.Should().BeEmpty();
        }
        finally { window.Close(); }
    }


    // The refusal quotes the verb in its canonical spelling, whatever the caller typed: "no person could set_value
    // it" is not a sentence, and "DoubleClick" is not the verb's name.
    [AvaloniaFact]
    public void Interact_Refusal_QuotesTheCanonicalVerb()
    {
        var button = new Button { Content = "Approve" };
        var window = new Window
        {
            Content = new StackPanel { Children = { new StackPanel { IsVisible = false, Children = { button } } } },
            Width = 200, Height = 150,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            UiAutomationDriver.Interact(button, "DoubleClick", null).Error.Should().Contain("'double_click' was refused");
        }
        finally { window.Close(); }
    }

    // Through the route a caller takes: a path read from the dump, resolved, then acted on.
    [AvaloniaFact]
    public void Interact_OnAHiddenNodeAddressedByItsDumpedPath_IsRefused()
    {
        var clicks = 0;
        var button = new Button { Name = "Approve", Content = "Approve" };
        button.Click += (_, _) => clicks++;
        var window = new Window
        {
            Content = new StackPanel { Children = { new StackPanel { IsVisible = false, Children = { button } } } },
            Width = 200, Height = 150,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var node = Flatten(UiAutomationDriver.Dump(window, "Window", maxDepth: 20, interactiveOnly: false))
                .First(n => n.Name == "Approve");
            node.IsHidden.Should().BeTrue();

            var (resolved, error) = UiAutomationDriver.Resolve(window, node.Path);
            error.Should().BeNull();
            UiAutomationDriver.Interact(resolved!, "invoke", null).Error.Should().Contain("is not showing");
            clicks.Should().Be(0);
        }
        finally { window.Close(); }
    }

    // A running VB6 form hides a Frame by painting it at opacity zero with hit-testing off, so that the code of the
    // controls inside keeps running; Avalonia still calls them visible, and no person can reach them.
    [AvaloniaFact]
    public void A_control_under_a_container_hidden_as_a_VB6_Frame_is_hidden_is_marked_hidden_and_refused()
    {
        var clicks = 0;
        var command = new Button { Name = "Command1", Content = "Go" };
        command.Click += (_, _) => clicks++;
        var frame = new Border { Opacity = 0, IsHitTestVisible = false, Child = command };
        var window = new Window { Content = new StackPanel { Children = { frame } }, Width = 200, Height = 150 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            command.IsEffectivelyVisible.Should().BeTrue("this is the case effective visibility alone misses");
            Flatten(UiAutomationDriver.Dump(window, "Window", maxDepth: 20, interactiveOnly: false))
                .First(n => n.Name == "Command1").IsHidden.Should().BeTrue();
            UiAutomationDriver.Interact(command, "invoke", null).Success.Should().BeFalse();
            clicks.Should().Be(0);
        }
        finally { window.Close(); }
    }

    // Either property alone is not hiding: the IDE turns hit-testing off on decorations it draws.
    [AvaloniaFact]
    public void A_control_under_a_drawn_container_that_takes_no_pointer_is_not_hidden()
    {
        var button = new Button { Name = "Shown", Content = "Go" };
        var window = new Window
        {
            Content = new StackPanel { Children = { new Border { IsHitTestVisible = false, Child = button } } },
            Width = 200, Height = 150,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Flatten(UiAutomationDriver.Dump(window, "Window", maxDepth: 20, interactiveOnly: false))
                .First(n => n.Name == "Shown").IsHidden.Should().BeNull();
        }
        finally { window.Close(); }
    }

    // A hidden menu item, such as one a personality leaves out, is refused with the generic advice: no menu route
    // reaches it either, so naming one would send the caller round in a circle.
    [AvaloniaFact]
    public void Interact_OnAHiddenItemOfAnOpenMenu_IsRefused_WithoutSendingTheCallerToAMenuRoute()
    {
        var clicks = 0;
        var hidden = new MenuItem { Header = "References", IsVisible = false };
        hidden.Click += (_, _) => clicks++;
        var project = new MenuItem { Header = "Project", Items = { new MenuItem { Header = "Add Module" }, hidden } };
        var window = new Window { Content = new Menu { Items = { project } }, Width = 200, Height = 150 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            project.Open();
            Dispatcher.UIThread.RunJobs();

            var outcome = UiAutomationDriver.Interact(hidden, "invoke", null);
            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("is not showing").And.NotContain("invoke_menu_item");
            clicks.Should().Be(0);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Scroll_aimed_at_a_hidden_target_is_refused_and_points_at_the_container()
    {
        var hidden = new Button { Content = "Approve", IsVisible = false };
        var window = new Window
        {
            Content = new ScrollViewer { Content = new StackPanel { Height = 2000, Children = { hidden } } },
            Width = 200, Height = 150,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            UiAutomationDriver.Interact(hidden, "scroll", "down").Error.Should().Contain("Aim scroll at the container");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Press_key_on_a_hidden_button_is_refused_and_does_not_click_it()
    {
        var clicks = 0;
        var button = new Button { Name = "Approve", Content = "Approve" };
        button.Click += (_, _) => clicks++;
        var window = new Window
        {
            Content = new StackPanel { Children = { new StackPanel { IsVisible = false, Children = { button } } } },
            Width = 200, Height = 150,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var outcome = UiAutomationDriver.PressKey(button, "Enter", null);
            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("is not showing").And.Contain("'press_key' was refused");
            clicks.Should().Be(0);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Type_text_into_a_hidden_text_box_is_refused_and_inserts_nothing()
    {
        var box = new TextBox { Name = "Note", Text = "" };
        var window = new Window
        {
            Content = new StackPanel { Children = { new StackPanel { IsVisible = false, Children = { box } } } },
            Width = 200, Height = 150,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var outcome = UiAutomationDriver.TypeText(box, "abc");
            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("is not showing");
            box.Text.Should().BeEmpty();
        }
        finally { window.Close(); }
    }

    // As the Properties window hides the rows of a collapsed category: select by text matches among the rows that
    // show, so a hidden row is neither selected nor counted against a showing one.
    [AvaloniaFact]
    public void Select_by_text_skips_hidden_rows_and_refuses_a_row_that_only_a_hidden_one_matches()
    {
        var list = new ListBox
        {
            ItemsSource = new[] { "Caption", "Caption", "Name" },
        };
        var window = new Window { Content = list, Width = 200, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            list.ContainerFromIndex(0)!.IsVisible = false;
            list.ContainerFromIndex(2)!.IsVisible = false;
            Dispatcher.UIThread.RunJobs();

            UiAutomationDriver.Interact(list, "select", "Caption").Success.Should().BeTrue("the hidden duplicate is not a rival");
            list.SelectedIndex.Should().Be(1);

            var refused = UiAutomationDriver.Interact(list, "select", "Name");
            refused.Success.Should().BeFalse();
            refused.Error.Should().Contain("is not showing");
            list.SelectedIndex.Should().Be(1);

            // Every row has a container, so a miss scrolls nothing and says so, and counts only the row that shows (#1256).
            var missed = UiAutomationDriver.Interact(list, "select", "Nowhere");
            missed.Success.Should().BeFalse();
            missed.Error.Should().StartWith("no selectable item matching 'Nowhere' among 1 realized item(s); 2 more are not showing")
                .And.NotContain("brought into view");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_OnTheShowingCopy_StillActs()
    {
        var clicks = 0;
        var shown = new Button { Name = "Approve", Content = "Approve" };
        shown.Click += (_, _) => clicks++;
        var window = new Window
        {
            Content = new StackPanel
            {
                Children = { new StackPanel { IsVisible = false, Children = { new Button { Content = "Approve" } } }, shown },
            },
            Width = 200, Height = 150,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            UiAutomationDriver.Interact(shown, "invoke", null).Success.Should().BeTrue();
            clicks.Should().Be(1);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_ReflectionActions_StillReachAHiddenControlsViewModel()
    {
        var vm = new SampleVm();
        var button = new Button { Name = "Approve", Content = "Approve", DataContext = vm };
        var window = new Window
        {
            Content = new StackPanel { Children = { new StackPanel { IsVisible = false, Children = { button } } } },
            Width = 200, Height = 150,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            UiAutomationDriver.Interact(button, "set_property", "Caption=changed").Success.Should().BeTrue();
            vm.Caption.Should().Be("changed");
        }
        finally { window.Close(); }
    }

    // #953: Window -> UserControl -> UserControl -> Button, the intermediates not focusable and with no provider.
    private static (Window window, UserControl outer) BuildDeepWindow()
    {
        var inner = new UserControl { Name = "Inner", Focusable = false, Content = new Button { Name = "Deep" } };
        var outer = new UserControl { Name = "Outer", Focusable = false, Content = inner };
        var window = new Window { Content = outer, Width = 200, Height = 150 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, outer);
    }

    [AvaloniaFact]
    public void Dump_KeepsANodeCutOffByMaxDepth_MarkedTruncated_EvenUnderInteractiveOnly()
    {
        var (window, _) = BuildDeepWindow();
        try
        {
            var tree = UiAutomationDriver.Dump(window, "Window", maxDepth: 1, interactiveOnly: true);
            var outer = Flatten(tree).Single(n => n.Name == "Outer");

            outer.Truncated.Should().BeTrue();
            outer.Children.Should().BeEmpty();
            tree.Truncated.Should().BeNull("the window's own children were walked");

            // Dumping again from the truncated node's path reaches what was below it.
            var (resolved, error) = UiAutomationDriver.Resolve(window, outer.Path);
            error.Should().BeNull();
            Flatten(UiAutomationDriver.Dump(resolved!, outer.Path, maxDepth: 20, interactiveOnly: true))
                .Should().Contain(n => n.Name == "Deep");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Dump_KeepsTheNonInteractiveAncestorsOfATruncatedNode()
    {
        var (window, _) = BuildDeepWindow();
        try
        {
            // Window(0) -> Outer(1) -> Inner(2): Inner is cut off, and Outer, between it and the root, has nothing
            // interactive of its own. Before #953 the prune climbed from Inner through Outer.
            var tree = UiAutomationDriver.Dump(window, "Window", maxDepth: 2, interactiveOnly: true);
            var outer = Flatten(tree).Single(n => n.Name == "Outer");

            outer.Truncated.Should().BeNull("Outer's own children were walked");
            var inner = outer.Children.Should().ContainSingle().Which;
            inner.Name.Should().Be("Inner");
            inner.Truncated.Should().BeTrue();
            inner.Children.Should().BeEmpty();
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public void Dump_DeepEnough_MarksNothingTruncated()
    {
        var (window, _) = BuildDeepWindow();
        try
        {
            var all = Flatten(UiAutomationDriver.Dump(window, "Window", maxDepth: 20, interactiveOnly: false)).ToList();
            all.Should().Contain(n => n.Name == "Deep");
            all.Should().OnlyContain(n => n.Truncated == null);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Dump_ALeafAtTheCutOff_IsNotTruncated()
    {
        var (window, _) = BuildDeepWindow();
        try
        {
            // Window(0) -> Outer(1) -> Inner(2) -> Deep(3): the button sits exactly at the cap and has nothing below.
            var deep = Flatten(UiAutomationDriver.Dump(window, "Window", maxDepth: 3, interactiveOnly: true))
                .Single(n => n.Name == "Deep");
            deep.Truncated.Should().BeNull();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Dump_CollapsesStructuralWrappers_IntoShortPaths()
    {
        // A button buried under Border -> StackPanel -> Border (all structural 'None' wrappers) must
        // surface as a direct control-view child of the window, with no structural segments in its path.
        var button = new Button { Name = "DeepButton", Content = "x" };
        var nested = new Border { Child = new StackPanel { Children = { button } } };
        var window = new Window { Content = new Border { Child = nested }, Width = 200, Height = 150 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var tree = UiAutomationDriver.Dump(window, "Window", maxDepth: 20, interactiveOnly: true);
            var node = Flatten(tree).First(n => n.Name == "DeepButton");

            node.Path.Should().NotContain("None[");                 // structural wrappers collapsed away
            node.Path.Should().Be("Window/Button[DeepButton]");     // re-parented directly under the window

            var (resolved, error) = UiAutomationDriver.Resolve(window, node.Path);
            error.Should().BeNull();
            resolved.Should().BeSameAs(button);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Dump_UniqueTypeWithoutName_EmitsBareSegment()
    {
        // A control that is the sole instance of its type and has no AutomationId/Name/label should get a
        // bare segment (no positional index) — e.g. the IDE's single, unnamed <Menu> becomes "Menu".
        var slider = new Slider();   // unique type, no x:Name, no content -> no discriminator
        var window = new Window { Content = new StackPanel { Children = { slider } }, Width = 200, Height = 120 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var tree = UiAutomationDriver.Dump(window, "Window", maxDepth: 20, interactiveOnly: true);
            var node = Flatten(tree).First(n => n.ClassName == "Slider");

            node.Path.Should().NotContain("[#");                  // no positional index
            node.Path.Should().Be($"Window/{node.ControlType}");  // bare, because unique-by-type and nameless

            var (resolved, error) = UiAutomationDriver.Resolve(window, node.Path);
            error.Should().BeNull();
            resolved.Should().BeSameAs(slider);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Dump_NameCollidesWithSiblingAutomationId_PathsStillRoundTripDistinctly()
    {
        // Regression for the emitter/resolver precedence bug: X's x:Name equals Y's AutomationId. The
        // emitter must not hand X a path that the resolver (AutomationId-first) would route to Y.
        var x = new Button { Name = "Foo", Content = "X" };
        var y = new Button { Content = "contentY" };
        AutomationProperties.SetAutomationId(y, "Foo");
        var window = new Window { Content = new StackPanel { Children = { x, y } }, Width = 240, Height = 160 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var tree = UiAutomationDriver.Dump(window, "Window", maxDepth: 20, interactiveOnly: true);
            var buttonNodes = Flatten(tree).Where(n => n.ClassName == "Button").ToList();
            buttonNodes.Should().HaveCount(2);

            var resolved = buttonNodes.Select(n => UiAutomationDriver.Resolve(window, n.Path).control).ToList();
            resolved.Should().NotContainNulls();
            resolved.Should().OnlyHaveUniqueItems();          // no two emitted paths collide on one control
            resolved.Should().Contain(x).And.Contain(y);      // both controls remain individually addressable
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Dump_StructuralWrapperWithAutomationId_IsKeptAndAddressable()
    {
        // A bare Border is structural (collapsed) — but tagging it with an AutomationId (as the toolbar
        // containers now are) must keep it visible in the dump and addressable via #id.
        var tagged = new Border();
        AutomationProperties.SetAutomationId(tagged, "MyAnchor");
        var window = Show(new Border { Child = tagged });
        try
        {
            var tree = UiAutomationDriver.Dump(window, "Window", maxDepth: 20, interactiveOnly: false);
            Flatten(tree).Any(n => n.AutomationId == "MyAnchor")
                .Should().BeTrue("an AutomationId-tagged structural wrapper must not be collapsed away");

            var (resolved, error) = UiAutomationDriver.Resolve(window, "Window/#MyAnchor");
            error.Should().BeNull();
            resolved.Should().BeSameAs(tagged);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Dump_EmittedPath_RoundTripsThroughResolve()
    {
        var (window, button, _, _, _) = BuildShownWindow();
        try
        {
            var tree = UiAutomationDriver.Dump(window, "Window", maxDepth: 20, interactiveOnly: false);
            var buttonNode = Flatten(tree).First(n => n.Name == "TheButton");

            var (resolved, error) = UiAutomationDriver.Resolve(window, buttonNode.Path);

            error.Should().BeNull();
            resolved.Should().BeSameAs(button);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Resolve_AutomationIdShortcut_FindsDescendant()
    {
        var (window, button, _, _, _) = BuildShownWindow();
        try
        {
            var (resolved, error) = UiAutomationDriver.Resolve(window, "Window/#btnId");

            error.Should().BeNull();
            resolved.Should().BeSameAs(button);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Resolve_RejectsPathNotRootedAtWindow()
    {
        var (window, _, _, _, _) = BuildShownWindow();
        try
        {
            var (resolved, error) = UiAutomationDriver.Resolve(window, "Foo/Bar");

            resolved.Should().BeNull();
            error.Should().Contain("Window");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Resolve_MissingChild_ReturnsError()
    {
        var (window, _, _, _, _) = BuildShownWindow();
        try
        {
            var (resolved, error) = UiAutomationDriver.Resolve(window, "Window/Button[NoSuchName]");

            resolved.Should().BeNull();
            error.Should().NotBeNull();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Inspect_ReportsProvidersValueAndDataContext()
    {
        var (window, _, textbox, check, _) = BuildShownWindow();
        try
        {
            var text = UiAutomationDriver.Inspect(textbox, "Window/Edit[TheText]");
            text.Providers.Should().Contain("value");
            text.Value.Should().Be("hi");
            text.DataContextType.Should().Be(nameof(SampleVm));

            var checkbox = UiAutomationDriver.Inspect(check, "Window/CheckBox[TheCheck]");
            checkbox.Providers.Should().Contain("toggle");
            checkbox.ToggleState.Should().BeFalse();
        }
        finally { window.Close(); }
    }

    // ── Tree nodes and the double-click gesture ──────────────────────────────────────────────────
    //
    // Both were unreachable until 2026-09-14 and are the reason the Project Explorer could be read in
    // full and not driven at all. See docs/archive/mcp-server-gaps-closed.md.

    /// <summary>A TreeView with one selectable root and one child, shown so peers exist.</summary>
    private static (Window Window, TreeView Tree, TreeViewItem Root) BuildTreeWindow()
    {
        var child = new TreeViewItem { Header = "Child" };
        var root = new TreeViewItem { Header = "Root", Name = "TheRoot" };
        root.Items.Add(child);

        var tree = new TreeView { Name = "TheTree" };
        tree.Items.Add(root);

        var window = new Window { Content = tree, Width = 300, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, tree, root);
    }

    [AvaloniaFact]
    public void DescribeProviders_TreeViewItem_AdvertisesSelectionItem()
    {
        var (window, _, root) = BuildTreeWindow();
        try
        {
            // The peer itself offers only scroll. Advertising selection is what makes the node
            // addressable at all; without it a caller reads the tree and cannot act on it.
            var providers = UiAutomationDriver.DescribeProviders(
                ControlAutomationPeer.CreatePeerForElement(root), root);

            providers.Should().Contain("selectionItem");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_Select_OnATreeNode_SetsTheOwningTreesSelectedItem()
    {
        var (window, tree, root) = BuildTreeWindow();
        try
        {
            var outcome = UiAutomationDriver.Interact(root, "select", null);

            outcome.Success.Should().BeTrue(outcome.Error);
            // SelectedItem, not IsSelected, is the assertion that matters: it is what a view model binds
            // to, and therefore what a caller is really trying to change.
            tree.SelectedItem.Should().BeSameAs(root);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_DoubleClick_RaisesDoubleTappedThatBubblesToTheContainer()
    {
        var (window, tree, root) = BuildTreeWindow();
        try
        {
            // Handlers live on the container and read e.Source — the Project Explorer's does — so the
            // event has to bubble and carry the item, not the tree.
            object? source = null;
            var seen = 0;
            tree.DoubleTapped += (_, e) => { seen++; source = e.Source; };

            var outcome = UiAutomationDriver.Interact(root, "double_click", null);

            outcome.Success.Should().BeTrue(outcome.Error);
            seen.Should().Be(1);
            source.Should().BeSameAs(root);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_DoubleClick_SelectsFirstAsARealDoubleClickDoes()
    {
        var (window, tree, root) = BuildTreeWindow();
        try
        {
            tree.SelectedItem.Should().BeNull("nothing is selected before the gesture");

            var outcome = UiAutomationDriver.Interact(root, "double_click", null);
            outcome.Detail.Should().Contain("DoubleTapped").And.Contain("selected it first").And.Contain("no pointer press");

            // Without this a handler would act on whatever was selected before — the wrong document,
            // and silently, which is the failure mode the verb exists to avoid.
            tree.SelectedItem.Should().BeSameAs(root);
        }
        finally { window.Close(); }
    }

    // ── #714: selecting in a tree that already has a selection ───────────────────────────────────────
    // Every test above starts from an empty selection, the one case where setting IsSelected moved
    // SelectedItem. With a selection already there it only added to SelectedItems, so select reported
    // success, two nodes were highlighted, and the bound property stayed where it was.

    private sealed record Page(string Title, List<Page> Children);

    /// <summary>
    /// A tree bound to data, as the Options dialog's is: items named after their title by a style, and
    /// General already selected.
    /// </summary>
    private static (Window Window, TreeView Tree, Page General, Page Theme) BuildBoundTreeWindow()
    {
        var general = new Page("General", []);
        var theme = new Page("Theme", []);
        var environment = new Page("Environment", [general, theme]);

        var tree = new TreeView
        {
            ItemsSource = new[] { environment },
            ItemTemplate = new FuncTreeDataTemplate<Page>(
                (p, _) => new TextBlock { Text = p.Title }, p => p.Children),
        };
        tree.Styles.Add(new Style(x => x.OfType<TreeViewItem>())
        {
            Setters =
            {
                new Setter(TreeViewItem.IsExpandedProperty, true),
                new Setter(AutomationProperties.NameProperty, new Binding(nameof(Page.Title))),
            },
        });

        var window = new Window { Content = tree, Width = 300, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        tree.SelectedItem = general;
        Dispatcher.UIThread.RunJobs();
        return (window, tree, general, theme);
    }

    private static TreeViewItem ContainerOf(TreeView tree, object data) =>
        tree.GetVisualDescendants().OfType<TreeViewItem>().Single(i => ReferenceEquals(i.DataContext, data));

    [AvaloniaFact]
    public void Interact_Select_OnATreeNode_MovesTheSelectionFromTheNodeAlreadySelected()
    {
        var (window, tree, _, theme) = BuildBoundTreeWindow();
        try
        {
            var outcome = UiAutomationDriver.Interact(ContainerOf(tree, theme), "select", null);

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Detail.Should().Be("selected 'Theme'", "the item is named after its text, not called 'TreeItem'");
            tree.SelectedItem.Should().BeSameAs(theme);
            tree.SelectedItems.Count.Should().Be(1, "a click replaces the selection, it does not add to it");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_Select_WithAValue_OnATree_SelectsTheNamedNode()
    {
        var (window, tree, _, theme) = BuildBoundTreeWindow();
        try
        {
            // Refused before as "no selectable items realized": tree items have no ISelectionItemProvider,
            // so the candidate filter dropped every one of them.
            var outcome = UiAutomationDriver.Interact(tree, "select", "Theme");

            outcome.Success.Should().BeTrue(outcome.Error);
            tree.SelectedItem.Should().BeSameAs(theme);
            tree.SelectedItems.Count.Should().Be(1);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_Select_WithAValue_OnATree_NamesWhatItCouldNotFind()
    {
        var (window, tree, general, _) = BuildBoundTreeWindow();
        try
        {
            var outcome = UiAutomationDriver.Interact(tree, "select", "Fonts");

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("no selectable item matching 'Fonts'");
            tree.SelectedItem.Should().BeSameAs(general);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_Select_WithAValue_OnAnEmptyTree_DoesNotTalkAboutDropdowns()
    {
        var tree = new TreeView { Name = "Empty" };
        var window = Show(tree);
        try
        {
            var outcome = UiAutomationDriver.Interact(tree, "select", "Theme");

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("has no items to select").And.NotContain("dropdown");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_DoubleClick_OnATreeNode_WithAnotherSelected_ActsOnTheNodeClicked()
    {
        var (window, tree, _, theme) = BuildBoundTreeWindow();
        try
        {
            // The Project Explorer's handler opens whatever is selected. Before #714 it would have opened
            // the node selected beforehand, since the pre-select never moved SelectedItem.
            object? selectedWhenHandled = null;
            tree.DoubleTapped += (_, _) => selectedWhenHandled = tree.SelectedItem;

            var outcome = UiAutomationDriver.Interact(ContainerOf(tree, theme), "double_click", null);

            outcome.Success.Should().BeTrue(outcome.Error);
            selectedWhenHandled.Should().BeSameAs(theme);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Two roots, Environment (General, Theme) and Editor (General), nodes collapsed as a tree starts, items named
    /// after their title.
    /// </summary>
    private static (Window Window, TreeView Tree, Page Environment, Page Editor) BuildCollapsibleTreeWindow()
    {
        var environment = new Page("Environment", [new Page("General", []), new Page("Theme", [])]);
        var editor = new Page("Editor", [new Page("General", [])]);

        var tree = new TreeView
        {
            ItemsSource = new[] { environment, editor },
            ItemTemplate = new FuncTreeDataTemplate<Page>(
                (p, _) => new TextBlock { Text = p.Title }, p => p.Children),
        };
        tree.Styles.Add(new Style(x => x.OfType<TreeViewItem>())
        {
            Setters = { new Setter(AutomationProperties.NameProperty, new Binding(nameof(Page.Title))) },
        });

        var window = new Window { Content = tree, Width = 300, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, tree, environment, editor);
    }

    [AvaloniaFact]
    public void Interact_ExpandAndCollapse_OnATreeNode_SetItsState()
    {
        var (window, tree, environment, _) = BuildCollapsibleTreeWindow();
        try
        {
            // The node's peer offers no ExpandCollapse provider, so both refused before.
            var node = ContainerOf(tree, environment);

            var expanded = UiAutomationDriver.Interact(node, "expand", null);
            expanded.Success.Should().BeTrue(expanded.Error);
            expanded.Detail.Should().Be("expanded 'Environment'");
            node.IsExpanded.Should().BeTrue();

            UiAutomationDriver.Interact(node, "expand", null).Detail.Should().Be("'Environment' was already expanded");

            var collapsed = UiAutomationDriver.Interact(node, "collapse", null);
            collapsed.Success.Should().BeTrue(collapsed.Error);
            node.IsExpanded.Should().BeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DescribeProviders_TreeNode_AdvertisesExpandCollapse_OnlyWhereItHasChildren()
    {
        var (window, tree, environment, _) = BuildCollapsibleTreeWindow();
        try
        {
            var node = ContainerOf(tree, environment);
            UiAutomationDriver.DescribeProviders(ControlAutomationPeer.CreatePeerForElement(node), node)
                .Should().Contain("expandCollapse");

            node.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            var leaf = ContainerOf(tree, environment.Children[1]);
            UiAutomationDriver.DescribeProviders(ControlAutomationPeer.CreatePeerForElement(leaf), leaf)
                .Should().NotContain("expandCollapse", "a leaf has nothing to expand, and would refuse");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_Select_WithAValue_UnderANodeNeverExpanded_NamesTheNodeToExpand()
    {
        var (window, tree, environment, _) = BuildCollapsibleTreeWindow();
        try
        {
            // Theme has no container until Environment is first expanded, so no dump shows it; telling the caller
            // to check the spelling would send it the wrong way.
            var refused = UiAutomationDriver.Interact(tree, "select", "Theme");

            refused.Success.Should().BeFalse();
            refused.Error.Should().Contain("'Environment'").And.Contain("'expand'").And.NotContain("exact text");

            UiAutomationDriver.Interact(ContainerOf(tree, environment), "expand", null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            var selected = UiAutomationDriver.Interact(tree, "select", "Theme");

            selected.Success.Should().BeTrue(selected.Error);
            tree.SelectedItem.Should().BeSameAs(environment.Children[1]);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_Select_WithAValue_UnderACollapsedNode_NamesTheNodeToExpand()
    {
        var (window, tree, environment, _) = BuildCollapsibleTreeWindow();
        try
        {
            // Expanded once and collapsed again: the children exist, hidden, so this is the not-showing refusal.
            var node = ContainerOf(tree, environment);
            node.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            node.IsExpanded = false;
            Dispatcher.UIThread.RunJobs();

            var refused = UiAutomationDriver.Interact(tree, "select", "Theme");

            refused.Success.Should().BeFalse();
            refused.Error.Should().Contain("is not showing").And.Contain("'expand' 'Environment'");
            tree.SelectedItem.Should().BeNull();
        }
        finally { window.Close(); }
    }

    // The nearest collapsed node alone was named, and expanding it was refused as not showing while the one above it was
    // collapsed too (#1256). Every collapsed node is named, outermost first, and following the advice reaches the item.
    [AvaloniaFact]
    public void Interact_Select_WithAValue_UnderNestedCollapsedNodes_NamesEachToExpandInOrder()
    {
        var leaf = new Page("Leaf", []);
        var inner = new Page("Inner", [leaf]);
        var outer = new Page("Outer", [inner]);
        var tree = new TreeView
        {
            ItemsSource = new[] { outer },
            ItemTemplate = new FuncTreeDataTemplate<Page>((p, _) => new TextBlock { Text = p.Title }, p => p.Children),
        };
        tree.Styles.Add(new Style(x => x.OfType<TreeViewItem>())
        {
            Setters = { new Setter(AutomationProperties.NameProperty, new Binding(nameof(Page.Title))) },
        });
        var window = new Window { Content = tree, Width = 300, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            ContainerOf(tree, outer).IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            ContainerOf(tree, inner).IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            ContainerOf(tree, inner).IsExpanded = false;
            ContainerOf(tree, outer).IsExpanded = false;
            Dispatcher.UIThread.RunJobs();

            var refused = UiAutomationDriver.Interact(tree, "select", "Leaf");
            refused.Success.Should().BeFalse();
            refused.Error.Should().Contain("is not showing").And.EndWith("'expand' 'Outer', then 'Inner', which hold it");

            UiAutomationDriver.Interact(ContainerOf(tree, outer), "expand", null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            UiAutomationDriver.Interact(ContainerOf(tree, inner), "expand", null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            var selected = UiAutomationDriver.Interact(tree, "select", "Leaf");
            selected.Success.Should().BeTrue(selected.Error);
            tree.SelectedItem.Should().BeSameAs(leaf);
        }
        finally { window.Close(); }
    }

    // type_text chose a ComboBox's hidden template TextBox ahead of the TextBox after it, and was refused as aimed at a
    // hidden control, where press_key passed it over (#1256).
    [AvaloniaFact]
    public void Type_text_passes_over_a_dropdown_s_hidden_text_box()
    {
        var combo = new ComboBox { ItemsSource = new[] { "a", "b" }, Width = 100 };
        var box = new TextBox { Width = 100 };
        var panel = new StackPanel { Children = { combo, box } };
        var lone = new ComboBox { ItemsSource = new[] { "a" }, Width = 100 };
        var hiddenBox = new TextBox { Width = 100 };
        var hiddenPanel = new StackPanel { IsVisible = false, Children = { hiddenBox } };
        var window = new Window { Content = new StackPanel { Children = { panel, lone, hiddenPanel } }, Width = 300, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            UiAutomationDriver.TypeText(panel, "abc").Success.Should().BeTrue();
            box.Text.Should().Be("abc");

            var noSurface = UiAutomationDriver.TypeText(lone, "abc");
            noSurface.Success.Should().BeFalse();
            noSurface.Error.Should().Contain("has no text surface").And.Contain("'expand' it, then 'select'").And.NotContain("is not showing");

            var hidden = UiAutomationDriver.TypeText(hiddenPanel, "abc");
            hidden.Success.Should().BeFalse();
            hidden.Error.Should().Contain("is not showing");

            // A hidden box ahead of a showing one gives way to it; only when nothing shows is the hidden one refused.
            hiddenPanel.IsVisible = true;
            hiddenBox.IsVisible = false;
            var after = new TextBox { Width = 100 };
            hiddenPanel.Children.Add(after);
            Dispatcher.UIThread.RunJobs();
            UiAutomationDriver.TypeText(hiddenPanel, "xyz").Success.Should().BeTrue();
            after.Text.Should().Be("xyz");
        }
        finally { window.Close(); }
    }

    // With two boxes that show under the target, the first took the text: aimed at the Properties pane, the form was renamed
    // (#1256 review). Neither is chosen; both are named.
    [AvaloniaFact]
    public void Type_text_refuses_a_target_holding_two_boxes_that_show_and_names_them()
    {
        var first = new TextBox { Width = 100 };
        var second = new TextBox { Width = 100 };
        var panel = new StackPanel { Children = { first, second } };
        var window = new Window { Content = panel, Width = 300, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var outcome = UiAutomationDriver.TypeText(panel, "abc");

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("holds 2 text boxes that show").And.Contain("nothing was typed");
            first.Text.Should().BeNullOrEmpty();
            second.Text.Should().BeNullOrEmpty();
        }
        finally { window.Close(); }
    }

    // A dropdown's own hidden part is left out whatever the dropdown's state, so in a hidden pane the surface found, and
    // refused as not showing, is the pane's box, not the dropdown's part (#1256 review).
    [AvaloniaFact]
    public void In_a_hidden_pane_the_surface_found_is_its_box_not_a_dropdown_s_part()
    {
        var combo = new ComboBox { ItemsSource = new[] { "a" }, Width = 100 };
        var box = new TextBox { Width = 100 };
        var pane = new StackPanel { Children = { combo, box } };
        var window = new Window { Content = pane, Width = 300, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            pane.IsVisible = false;
            Dispatcher.UIThread.RunJobs();
            UiAutomationDriver.FindTextSurface(pane).Should().BeSameAs(box);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Type_text_refuses_hidden_dropdown_targets_before_finding_a_surface(bool keyboard, bool panelTarget)
    {
        var combo = new ComboBox { ItemsSource = new[] { "a" }, Width = 100, IsVisible = !panelTarget };
        var panel = new StackPanel { IsVisible = false, Children = { combo } };
        Control target = panelTarget ? panel : combo;
        AutomationProperties.SetName(target, panelTarget ? "Hidden panel" : "Hidden dropdown");
        var window = new Window { Content = panel, Width = 300, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var outcome = keyboard
                ? await UiAutomationDriver.TypeKeysAsync(target, "x")
                : UiAutomationDriver.TypeText(target, "x");

            outcome.Success.Should().BeFalse();
            outcome.Mechanism.Should().Be(keyboard ? "keyboard" : "document");
            outcome.Error.Should().Contain("is not showing").And.Contain(panelTarget ? "Hidden panel" : "Hidden dropdown")
                .And.NotContain("'expand'").And.NotContain("has no text surface");
            combo.IsDropDownOpen.Should().BeFalse();
            combo.SelectedIndex.Should().Be(-1);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Type_text_by_keys_passes_over_a_dropdown_s_hidden_text_box()
    {
        var combo = new ComboBox { ItemsSource = new[] { "a", "b" }, Width = 100 };
        var box = new TextBox { Width = 100 };
        var panel = new StackPanel { Children = { combo, box } };
        var lone = new ComboBox { ItemsSource = new[] { "a" }, Width = 100 };
        var hiddenBox = new TextBox { Width = 100 };
        var hiddenPanel = new StackPanel { IsVisible = false, Children = { hiddenBox } };
        var window = new Window { Content = new StackPanel { Children = { panel, lone, hiddenPanel } }, Width = 300, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var typed = await UiAutomationDriver.TypeKeysAsync(panel, "abc");
            typed.Success.Should().BeTrue(typed.Error);
            box.Text.Should().Be("abc");

            var noSurface = await UiAutomationDriver.TypeKeysAsync(lone, "abc");
            noSurface.Success.Should().BeFalse();
            noSurface.Error.Should().Contain("has no text surface").And.NotContain("is not showing");

            var hidden = await UiAutomationDriver.TypeKeysAsync(hiddenPanel, "abc");
            hidden.Success.Should().BeFalse();
            hidden.Error.Should().Contain("is not showing");
            hiddenBox.Text.Should().BeNullOrEmpty();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_Select_WithAValue_SaysWhenAHiddenItemOfTheSameNameWasLeftAlone()
    {
        var (window, tree, environment, editor) = BuildCollapsibleTreeWindow();
        try
        {
            // Both Generals realized, then Editor collapsed: a person sees one General and clicks it.
            var environmentNode = ContainerOf(tree, environment);
            var editorNode = ContainerOf(tree, editor);
            environmentNode.IsExpanded = true;
            editorNode.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            editorNode.IsExpanded = false;
            Dispatcher.UIThread.RunJobs();

            var outcome = UiAutomationDriver.Interact(tree, "select", "General");

            outcome.Success.Should().BeTrue(outcome.Error);
            tree.SelectedItem.Should().BeSameAs(environment.Children[0]);
            outcome.Detail.Should().Contain("another 'General' is not showing and was left alone");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ReflectDataContextMembers_ListsCommandsAndProperties()
    {
        var members = UiAutomationDriver.ReflectDataContextMembers(new SampleVm());

        members.Should().Contain(m => m.Name == "DoThing" && m.Kind == "command");
        members.Should().Contain(m => m.Name == "Caption" && m.Kind == "property" && m.CanWrite);
        members.Should().Contain(m => m.Name == "ReadOnlyNumber" && m.Kind == "property" && !m.CanWrite);
    }

    [AvaloniaFact]
    public void ReflectDataContextMembers_NullDataContext_ReturnsEmpty()
    {
        UiAutomationDriver.ReflectDataContextMembers(null).Should().BeEmpty();
    }

    // ── Phase 6: interaction ───────────────────────────────────────────────────────────────────────

    private static Window Show(Control content)
    {
        var window = new Window { Content = content, Width = 400, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void Interact_Invoke_FiresButton()
    {
        var clicked = false;
        var button = new Button { Content = "Go" };
        button.Click += (_, _) => clicked = true;
        var window = Show(button);
        try
        {
            var outcome = UiAutomationDriver.Interact(button, "invoke", null);
            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Mechanism.Should().Be("peer");
            clicked.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_Toggle_FlipsCheckBox()
    {
        var check = new CheckBox { IsChecked = false };
        var window = Show(check);
        try
        {
            UiAutomationDriver.Interact(check, "toggle", null).Success.Should().BeTrue();
            check.IsChecked.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_SetValue_SetsTextBoxText()
    {
        var textbox = new TextBox { Text = "old" };
        var window = Show(textbox);
        try
        {
            UiAutomationDriver.Interact(textbox, "set_value", "hello").Success.Should().BeTrue();
            textbox.Text.Should().Be("hello");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ReadOnlyValue_ComboMatch_SelectsAndReports()
    {
        var combo = new ComboBox { ItemsSource = new[] { "Alpha", "Beta" }, SelectedIndex = 0 };
        var window = Show(combo);
        try
        {
            var outcome = UiAutomationDriver.Interact(combo, "set_value", "beta");
            outcome.Success.Should().BeTrue(outcome.Error);
            combo.SelectedItem.Should().Be("Beta");
            combo.IsDropDownOpen.Should().BeFalse();
            outcome.Detail.Should().Be("selected 'Beta' and closed the dropdown, as a click does");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ReadOnlyValue_ComboMiss_NamesItemsWithoutSelecting()
    {
        var combo = new ComboBox { ItemsSource = new[] { "Alpha", "Beta" }, SelectedIndex = 0 };
        var window = Show(combo);
        try
        {
            var outcome = UiAutomationDriver.Interact(combo, "set_value", "missing");
            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("no selectable item matching 'missing'").And.Contain("'Alpha', 'Beta'");
            combo.SelectedItem.Should().Be("Alpha");
            combo.IsDropDownOpen.Should().BeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ReadOnlyValue_Inspection_ReportsComboTrueAndTextBoxFalse()
    {
        var combo = new ComboBox { ItemsSource = new[] { "Alpha", "Beta" } };
        var text = new TextBox();
        var window = Show(new StackPanel { Children = { combo, text } });
        try
        {
            var options = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
            var comboJson = System.Text.Json.JsonSerializer.SerializeToElement(UiAutomationDriver.Inspect(combo, "Window/ComboBox"), options);
            var textJson = System.Text.Json.JsonSerializer.SerializeToElement(UiAutomationDriver.Inspect(text, "Window/TextBox"), options);
            comboJson.TryGetProperty("isReadOnly", out var comboReadOnly).Should().BeTrue();
            comboReadOnly.GetBoolean().Should().BeTrue();
            textJson.GetProperty("isReadOnly").GetBoolean().Should().BeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ReadOnlyValue_ComboMiss_CapsNamesAndCountsOmitted()
    {
        var combo = new ComboBox { ItemsSource = Enumerable.Range(0, 25).Select(i => $"Item{i}").ToArray() };
        var window = Show(combo);
        try
        {
            var outcome = UiAutomationDriver.Interact(combo, "set_value", "missing");
            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("'Item0'").And.Contain("'Item19'").And.Contain("5 more item(s) omitted")
                .And.NotContain("'Item20'");
            combo.SelectedIndex.Should().Be(-1);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ReadOnlyValue_ComboMatch_SelectsAnOffscreenItem()
    {
        var combo = new ComboBox { ItemsSource = Enumerable.Range(0, 25).Select(i => $"Item{i}").ToArray() };
        var window = Show(combo);
        try
        {
            var outcome = UiAutomationDriver.Interact(combo, "set_value", "Item24");
            outcome.Success.Should().BeTrue(outcome.Error);
            combo.SelectedItem.Should().Be("Item24");
            outcome.Detail.Should().Be("selected 'Item24' and closed the dropdown, as a click does");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ReadOnlyValue_ComboAmbiguity_RefusesWithoutSelecting()
    {
        var combo = new ComboBox { ItemsSource = new[] { "Dup", "dup" } };
        var window = Show(combo);
        try
        {
            var outcome = UiAutomationDriver.Interact(combo, "set_value", "Dup");
            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("ambiguous");
            combo.SelectedIndex.Should().Be(-1);
            combo.IsDropDownOpen.Should().BeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ReadOnlyValue_TextBox_RefusesClearly()
    {
        var text = new TextBox { Text = "original", IsReadOnly = true };
        var window = Show(text);
        try
        {
            var outcome = UiAutomationDriver.Interact(text, "set_value", "changed");
            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("value is read-only").And.NotContain("threw");
            text.Text.Should().Be("original");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_Select_ByValue_SelectsListBoxItem()
    {
        var list = new ListBox { ItemsSource = new[] { "Alpha", "Beta", "Gamma" } };
        var window = Show(list);
        try
        {
            var outcome = UiAutomationDriver.Interact(list, "select", "Beta");
            outcome.Success.Should().BeTrue(outcome.Error);
            list.SelectedItem.Should().Be("Beta");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_Select_AmbiguousValue_ErrorsInsteadOfSilentlyPickingFirst()
    {
        // Two items render the same text. select-by-value must error (like path resolution) rather than
        // silently select whichever realized first and report success — a false-positive in verification.
        var list = new ListBox { ItemsSource = new[] { "Dup", "Dup", "Other" } };
        var window = Show(list);
        try
        {
            var outcome = UiAutomationDriver.Interact(list, "select", "Dup");
            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("ambiguous");
            list.SelectedItem.Should().BeNull();   // nothing was selected
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_ExpandCollapse_TogglesComboBoxDropDown()
    {
        var combo = new ComboBox { ItemsSource = new[] { "A", "B" } };
        var window = Show(combo);
        try
        {
            UiAutomationDriver.Interact(combo, "expand", null).Success.Should().BeTrue();
            combo.IsDropDownOpen.Should().BeTrue();
            UiAutomationDriver.Interact(combo, "collapse", null).Success.Should().BeTrue();
            combo.IsDropDownOpen.Should().BeFalse();
        }
        finally { window.Close(); }
    }

    // #907: the item's peer only set the selection, so the popup stayed open over what lay below it, and the reply
    // said nothing. A click on the item closes it.
    [AvaloniaFact]
    public void Interact_Select_OnAnItemInAnOpenDropDown_ClosesItAsAClickDoes()
    {
        var combo = new ComboBox { ItemsSource = new[] { "A", "B" } };
        var window = Show(combo);
        try
        {
            UiAutomationDriver.Interact(combo, "expand", null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            var item = combo.GetRealizedContainers().OfType<ComboBoxItem>().Last();

            var outcome = UiAutomationDriver.Interact(item, "select", null);

            outcome.Success.Should().BeTrue(outcome.Error);
            combo.SelectedItem.Should().Be("B");
            combo.IsDropDownOpen.Should().BeFalse();
            outcome.Detail.Should().Be("selected 'B' and closed the dropdown, as a click does");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_Select_WithAValue_OnAnOpenDropDown_ClosesIt()
    {
        var combo = new ComboBox { ItemsSource = new[] { "A", "B" } };
        var window = Show(combo);
        try
        {
            UiAutomationDriver.Interact(combo, "expand", null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            combo.IsDropDownOpen.Should().BeTrue();

            var outcome = UiAutomationDriver.Interact(combo, "select", "B");

            outcome.Success.Should().BeTrue(outcome.Error);
            combo.SelectedItem.Should().Be("B");
            combo.IsDropDownOpen.Should().BeFalse();
            outcome.Detail.Should().EndWith("and closed the dropdown, as a click does");
        }
        finally { window.Close(); }
    }

    // A selection handler that replaces the list recycles the item's container and clears its parent, so a dropdown
    // looked for after the selection went unfound and stayed open, with nothing said.
    [AvaloniaFact]
    public void Interact_Select_OnAnOpenDropDownWhoseListIsReplacedOnSelection_StillClosesIt()
    {
        var combo = new ComboBox { ItemsSource = new[] { "A", "B" } };
        var replaced = false;
        combo.SelectionChanged += (_, _) =>
        {
            if (replaced || combo.SelectedItem is not "B") return;
            replaced = true;
            combo.ItemsSource = new[] { "C", "D" };
        };
        var window = Show(combo);
        try
        {
            UiAutomationDriver.Interact(combo, "expand", null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            var item = combo.GetRealizedContainers().OfType<ComboBoxItem>().Last();

            var outcome = UiAutomationDriver.Interact(item, "select", null);

            outcome.Success.Should().BeTrue(outcome.Error);
            combo.IsDropDownOpen.Should().BeFalse();
            outcome.Detail.Should().EndWith("and closed the dropdown, as a click does");
        }
        finally { window.Close(); }
    }

    // A real double-click's first click picks the item and closes the dropdown; its second lands on what lay beneath.
    [AvaloniaFact]
    public void Interact_DoubleClick_OnAnItemInAnOpenDropDown_SelectsItAndClosesItWithoutADoubleTap()
    {
        var combo = new ComboBox { ItemsSource = new[] { "A", "B" } };
        var doubleTapped = false;
        combo.AddHandler(InputElement.DoubleTappedEvent, (_, _) => doubleTapped = true);
        var window = Show(combo);
        try
        {
            UiAutomationDriver.Interact(combo, "expand", null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            var item = combo.GetRealizedContainers().OfType<ComboBoxItem>().Last();

            var outcome = UiAutomationDriver.Interact(item, "double_click", null);

            outcome.Success.Should().BeTrue(outcome.Error);
            combo.SelectedItem.Should().Be("B");
            combo.IsDropDownOpen.Should().BeFalse();
            doubleTapped.Should().BeFalse();
            outcome.Detail.Should().Contain("closed the dropdown").And.Contain("no double-click was raised");
        }
        finally { window.Close(); }
    }

    // The template's TextBox shows on an editable ComboBox, and a person's keys go to it.
    [AvaloniaFact]
    public void PressKey_OnAnEditableComboBox_GoesToItsTextBox()
    {
        var combo = new ComboBox { ItemsSource = new[] { "A", "B" }, IsEditable = true };
        var window = Show(combo);
        try
        {
            var outcome = UiAutomationDriver.PressKey(combo, "A", null);

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Detail.Should().Contain("TextBox");
        }
        finally { window.Close(); }
    }

    // Found on #907's live check: every key on a ComboBox was refused, because the key target chosen was the hidden
    // TextBox in its template rather than the ComboBox.
    [AvaloniaFact]
    public void PressKey_OnAComboBoxThatIsNotEditable_GoesToTheComboBox()
    {
        var combo = new ComboBox { ItemsSource = new[] { "A", "B" }, SelectedIndex = 0 };
        var window = Show(combo);
        try
        {
            var outcome = UiAutomationDriver.PressKey(combo, "Down", null);

            outcome.Success.Should().BeTrue(outcome.Error);
            combo.SelectedIndex.Should().Be(1, "Down on a closed ComboBox selects the next item");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_Select_OnAListItem_SaysNothingAboutADropDown()
    {
        var list = new ListBox { ItemsSource = new[] { "A", "B" } };
        var window = Show(list);
        try
        {
            var outcome = UiAutomationDriver.Interact(list, "select", "B");

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Detail.Should().Be("selected 'B'");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_UnsupportedAction_ReturnsCleanError()
    {
        var textbox = new TextBox();
        var window = Show(textbox);
        try
        {
            var outcome = UiAutomationDriver.Interact(textbox, "invoke", null);
            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("does not support 'invoke'");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_ResolveThenInteract_EndToEnd()
    {
        var check = new CheckBox { Name = "MyCheck", IsChecked = false };
        var window = Show(new StackPanel { Children = { check } });
        try
        {
            var (control, error) = UiAutomationDriver.Resolve(window, "Window/CheckBox[MyCheck]");
            error.Should().BeNull();
            UiAutomationDriver.Interact(control!, "toggle", null).Success.Should().BeTrue();
            check.IsChecked.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    // ── Phase 7: reflection fallback ───────────────────────────────────────────────────────────────

    private sealed class RelayCommand(Action exec, Func<bool>? can = null) : ICommand
    {
        public bool CanExecute(object? p) => can?.Invoke() ?? true;
        public void Execute(object? p) => exec();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }

    private sealed class Phase7Vm
    {
        public bool Fired { get; private set; }
        public bool CanFire { get; set; } = true;
        public ICommand Fire { get; }
        public string Caption { get; set; } = "";
        public int Count { get; set; }
        public bool Flag { get; set; }
        public DayOfWeek Day { get; set; } = DayOfWeek.Monday;
        public string ReadOnlyName => "ro";
        public Phase7Vm() => Fire = new RelayCommand(() => Fired = true, () => CanFire);
    }

    // A Border has a NoneAutomationPeer (no interaction provider) — exactly the gap the reflection
    // fallback fills. DataContext carries the VM the reflection actions target.
    private static (Border control, Phase7Vm vm, Window window) BorderBoundTo()
    {
        var vm = new Phase7Vm();
        var control = new Border { DataContext = vm };
        return (control, vm, Show(control));
    }

    [AvaloniaFact]
    public void Interact_InvokeCommand_ExecutesVmCommand_ViaReflection()
    {
        var (control, vm, window) = BorderBoundTo();
        try
        {
            var outcome = UiAutomationDriver.Interact(control, "invoke_command", "Fire");
            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Mechanism.Should().Be("reflection");
            vm.Fired.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_InvokeCommand_CanExecuteFalse_Errors()
    {
        var (control, vm, window) = BorderBoundTo();
        vm.CanFire = false;
        try
        {
            var outcome = UiAutomationDriver.Interact(control, "invoke_command", "Fire");
            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("CanExecute");
            vm.Fired.Should().BeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_InvokeCommand_NotACommand_Errors()
    {
        var (control, _, window) = BorderBoundTo();
        try
        {
            UiAutomationDriver.Interact(control, "invoke_command", "Caption").Error.Should().Contain("not an ICommand");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_SetProperty_CoercesAndSetsVmProperty()
    {
        var (control, vm, window) = BorderBoundTo();
        try
        {
            UiAutomationDriver.Interact(control, "set_property", "Caption=hello").Success.Should().BeTrue();
            vm.Caption.Should().Be("hello");

            UiAutomationDriver.Interact(control, "set_property", "Count=42").Success.Should().BeTrue();
            vm.Count.Should().Be(42);

            UiAutomationDriver.Interact(control, "set_property", "Flag=true").Success.Should().BeTrue();
            vm.Flag.Should().BeTrue();

            var enumOutcome = UiAutomationDriver.Interact(control, "set_property", "Day=Friday");
            enumOutcome.Success.Should().BeTrue(enumOutcome.Error);
            enumOutcome.Mechanism.Should().Be("reflection");
            vm.Day.Should().Be(DayOfWeek.Friday);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_SetProperty_ReadOnlyOrBadFormat_Errors()
    {
        var (control, _, window) = BorderBoundTo();
        try
        {
            UiAutomationDriver.Interact(control, "set_property", "ReadOnlyName=x").Error.Should().Contain("writable");
            UiAutomationDriver.Interact(control, "set_property", "noequals").Error.Should().NotBeNull();
            UiAutomationDriver.Interact(control, "set_property", "Count=notanumber").Error.Should().Contain("convert");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_NoImplicitFallback_InvokeOnProviderlessControlDoesNotReflect()
    {
        // A Border has no IInvokeProvider; 'invoke' must report unsupported, NOT silently reflect a command.
        var (control, vm, window) = BorderBoundTo();
        try
        {
            var outcome = UiAutomationDriver.Interact(control, "invoke", null);
            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("does not support 'invoke'");
            vm.Fired.Should().BeFalse();
        }
        finally { window.Close(); }
    }

    // ── Phase 10: keyboard input ───────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void TypeText_IntoTextBox_InsertsAtCaret()
    {
        var box = new TextBox { Text = "" };
        var window = Show(box);
        try
        {
            UiAutomationDriver.TypeText(box, "hello").Success.Should().BeTrue();
            UiAutomationDriver.TypeText(box, " world").Success.Should().BeTrue();
            box.Text.Should().Be("hello world");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TypeText_IntoCodeEditor_InsertsMultiLineIntoDocument()
    {
        var editor = new TextEditor();
        var window = Show(editor);
        try
        {
            UiAutomationDriver.TypeText(editor, "Sub Main()\n").Success.Should().BeTrue();
            UiAutomationDriver.TypeText(editor, "    MsgBox \"hi\"\n").Success.Should().BeTrue();
            UiAutomationDriver.TypeText(editor, "End Sub").Success.Should().BeTrue();
            editor.Document.Text.Should().Be("Sub Main()\n    MsgBox \"hi\"\nEnd Sub");
        }
        finally { window.Close(); }
    }

    // #649: the insert is a document edit, so it went straight through a read-only editor and said "keyboard".

    [AvaloniaFact]
    public void TypeText_IntoAReadOnlyCodeEditor_IsRefusedAndChangesNothing()
    {
        var editor = new TextEditor { Text = "Sub Main()\nEnd Sub", IsReadOnly = true };
        var window = Show(editor);
        try
        {
            var outcome = UiAutomationDriver.TypeText(editor, "XXX_SHOULD_NOT_APPEAR");

            outcome.Success.Should().BeFalse();
            outcome.Mechanism.Should().Be(UiAutomationDriver.TypedMechanism);
            outcome.Error.Should().Be("TextEditor is read-only, so a person could not type there either; nothing was inserted");
            editor.Document.Text.Should().Be("Sub Main()\nEnd Sub");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TypeText_IntoAReadOnlyOrDisabledTextBox_IsRefused()
    {
        var readOnly = new TextBox { Text = "kept", IsReadOnly = true };
        var disabled = new TextBox { Text = "kept", IsEnabled = false };
        var window = Show(new StackPanel { Children = { readOnly, disabled } });
        try
        {
            UiAutomationDriver.TypeText(readOnly, "x").Error.Should().StartWith("TextBox is read-only");
            UiAutomationDriver.TypeText(disabled, "x").Error.Should().StartWith("TextBox is disabled");
            readOnly.Text.Should().Be("kept");
            disabled.Text.Should().Be("kept");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TypeText_ReportsItsMechanismAsADocumentEdit_NotAsKeyboardInput()
    {
        var box = new TextBox { Text = "" };
        var window = Show(box);
        try
        {
            UiAutomationDriver.TypeText(box, "hello").Mechanism.Should().Be("document",
                "the text goes in through the control's API; 'keyboard' is reserved for press_key's real key events");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TypeText_FindsNestedTextSurface()
    {
        var box = new TextBox();
        var panel = new StackPanel { Children = { box } };
        var window = Show(panel);
        try
        {
            UiAutomationDriver.TypeText(panel, "nested").Success.Should().BeTrue();
            box.Text.Should().Be("nested");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TypeText_NoTextSurface_Errors()
    {
        var button = new Button { Content = "x" };
        var window = Show(button);
        try
        {
            var outcome = UiAutomationDriver.TypeText(button, "x");
            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("no text surface");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PressKey_RaisesRealKeyEvents()
    {
        Avalonia.Input.Key? got = null;
        var mods = Avalonia.Input.KeyModifiers.None;
        var box = new TextBox();
        box.KeyDown += (_, e) => { got = e.Key; mods = e.KeyModifiers; };
        var window = Show(box);
        try
        {
            UiAutomationDriver.PressKey(box, "Enter", null).Success.Should().BeTrue();
            got.Should().Be(Avalonia.Input.Key.Enter);

            UiAutomationDriver.PressKey(box, "S", "Ctrl").Success.Should().BeTrue();
            got.Should().Be(Avalonia.Input.Key.S);
            mods.Should().Be(Avalonia.Input.KeyModifiers.Control);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PressKey_UnknownKeyOrModifier_Errors()
    {
        var box = new TextBox();
        var window = Show(box);
        try
        {
            UiAutomationDriver.PressKey(box, "NotAKey", null).Error.Should().Contain("unknown key");
            UiAutomationDriver.PressKey(box, "S", "Hyper").Error.Should().Contain("unknown modifier");
        }
        finally { window.Close(); }
    }

    // ── press_key target resolution ─────────────────────────────────────────────────────────────
    // Two recorded MCP gaps, one root cause: the key was raised on a control the caller did not mean,
    // and the reply said "success" either way. A routed event reaches the element it is raised on and
    // its ancestors -- never anything below -- so resolving too SHALLOW makes a handler unreachable,
    // and resolving to a non-focusable container makes the press a no-op. Both looked identical to a
    // feature that was simply broken, and one of them cost a wrongly-filed issue.

    [AvaloniaFact]
    public void PressKey_ResolvesToTheDeepestInputSurface_SoAHandlerBelowTheEditorStillFires()
    {
        // THE case. AvaloniaEdit nests TextEditor -> TextArea, and the code editor attaches its key
        // handling to TextArea. Raising on the TextEditor put TextArea on neither leg of the route.
        var editor = new TextEditor();
        var host = new StackPanel();
        host.Children.Add(editor);
        var window = Show(host);
        try
        {
            var seen = 0;
            editor.TextArea.AddHandler(
                InputElement.KeyDownEvent, (object? _, KeyEventArgs e) => { if (e.Key == Key.F12) seen++; },
                RoutingStrategies.Tunnel);

            var outcome = UiAutomationDriver.PressKey(host, "F12", null);

            outcome.Success.Should().BeTrue(outcome.Error);
            seen.Should().Be(1, "a handler on TextArea must receive a key pressed at the editor's host");
            outcome.Detail.Should().Contain("TextArea",
                "the reply has to name the receiver, or a no-op is unattributable");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PressKey_OnAContainerWithNothingFocusable_FailsRatherThanReportingSuccess()
    {
        // A dock container has no focus and no text surface. Reporting success there is what made
        // "input was blocked" and "the key went nowhere" indistinguishable from the result.
        var label = new TextBlock { Text = "not focusable" };
        var host = new StackPanel { Focusable = false };
        host.Children.Add(label);
        var window = Show(host);
        try
        {
            var outcome = UiAutomationDriver.PressKey(host, "A", null);

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("keyboard focus");
        }
        finally { window.Close(); }
    }

    // A running form hides a Frame this way (VBVisibility). press_key passes over what is hidden, so it once told the
    // caller there was nothing focusable here, while type_text on the same target said it was hidden.
    [AvaloniaFact]
    public void PressKey_OnAHiddenContainer_SaysItIsNotShowing()
    {
        var frame = new Border { Opacity = 0, IsHitTestVisible = false, Child = new TextBox { Name = "Text1" } };
        var window = Show(frame);
        try
        {
            var outcome = UiAutomationDriver.PressKey(frame, "A", null);

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("is not showing").And.NotContain("keyboard focus");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PressKey_OnAContainerWhoseOnlyTextBoxIsHidden_NamesThatTextBox()
    {
        var host = new StackPanel();
        host.Children.Add(new Border { Opacity = 0, IsHitTestVisible = false, Child = new TextBox { Name = "Text1" } });
        var window = Show(host);
        try
        {
            var outcome = UiAutomationDriver.PressKey(host, "A", null);

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("Text1").And.Contain("is not showing");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PressKey_OnAFocusableControlItself_ReachesItAndSaysNothingExtra()
    {
        // The ordinary case, pinned so the resolution above cannot quietly redirect a direct address.
        var box = new TextBox();
        var window = Show(box);
        try
        {
            var seen = false;
            box.AddHandler(InputElement.KeyDownEvent, (object? _, KeyEventArgs e) => seen = true,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

            var outcome = UiAutomationDriver.PressKey(box, "Enter", null);

            outcome.Success.Should().BeTrue(outcome.Error);
            seen.Should().BeTrue();

            // Asserted as an absence rather than an exact string: Avalonia's `Key.Enter` is an alias for
            // `Key.Return`, so the reply echoes the CANONICAL name rather than the one the caller typed.
            // That is useful — it says what the key resolved to — and it is not what this test is about.
            outcome.Detail.Should().NotContain(" on ",
                "naming the receiver is noise when it is the control that was addressed");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PressKey_OnAContainer_NamesWhichOfTwoEditorsReceivedTheKey_AsAPathThatResolvesBack()
    {
        // Two editors, as the IDE has an Immediate window and a code window. "pressed F12 on TextArea" was true
        // of either, so it identified neither (#611). The reply's path must pick out the one that got the key,
        // and resolving that path must lead press_key back to the same TextArea.
        var first = new TextEditor { Name = "First" };
        var second = new TextEditor { Name = "Second" };
        // Addressable, as the IDE's editors are (its dumps show TextEditor[Editor]). Headless, with no editor
        // theme applied, an untagged TextEditor has no template and would be folded away as a bare wrapper.
        AutomationProperties.SetAutomationId(first, "First");
        AutomationProperties.SetAutomationId(second, "Second");
        var host = new StackPanel();
        host.Children.Add(first);
        host.Children.Add(second);
        var window = Show(host);
        try
        {
            AvaloniaEdit.Editing.TextArea? got = null;
            foreach (var editor in new[] { first, second })
            {
                var area = editor.TextArea;
                area.AddHandler(InputElement.KeyDownEvent, (object? _, KeyEventArgs e) => got = area,
                    RoutingStrategies.Tunnel);
            }

            var outcome = UiAutomationDriver.PressKey(window, "F12", null, "Window");

            outcome.Success.Should().BeTrue(outcome.Error);
            got.Should().NotBeNull();
            var path = outcome.Detail!.Split(", at ").Last();
            path.Should().StartWith("Window/", "the reply must carry a path, not only a class name");

            var (resolved, error) = UiAutomationDriver.Resolve(window, path);
            resolved.Should().NotBeNull(error);
            // WHICH editor, not which class. The leaf depends on whether the editor has a template: with
            // AvaloniaEdit's theme loaded, as the running IDE and now this harness load it, the path ends at
            // the TextArea; without it the TextEditor is the leaf. This asserted TextEditor and so passed only
            // while the harness differed from the app. What #611 is about is that the path picks out one of two
            // editors, and that holds either way.
            var resolvedArea = resolved switch
            {
                TextEditor editor => editor.TextArea,
                AvaloniaEdit.Editing.TextArea textArea => textArea,
                _ => null,
            };
            resolvedArea.Should().BeSameAs(got,
                "the path names the editor whose TextArea received the key, not the other one");

            var receiver = got;
            got = null;
            UiAutomationDriver.PressKey(resolved!, "F12", null, path).Success.Should().BeTrue();
            got.Should().BeSameAs(receiver, "pressing at the reported path reaches the same TextArea again");
        }
        finally { window.Close(); }
    }

    // ── hover ──────────────────────────────────────────────────────────────────────────────────────
    // A hover is a POSITION, not just an event: the code editor reads e.GetPosition(TextView) and turns it
    // into a text location, so an event carrying no usable point produces no tip however well it is routed.

    [AvaloniaFact]
    public void Hover_RaisesEnteredAndMoved_CarryingAPosition()
    {
        var button = new Button { Content = "Hover me", Width = 120, Height = 40 };
        var window = Show(button);
        try
        {
            var events = new List<string>();
            Point? seen = null;
            button.AddHandler(InputElement.PointerEnteredEvent,
                // Direct, not Bubble: Avalonia registers PointerEntered/Exited as DIRECT events, so a
                // bubbling handler never sees one however it is raised.
                (object? _, PointerEventArgs e) => events.Add("entered"), RoutingStrategies.Direct);
            button.AddHandler(InputElement.PointerMovedEvent,
                (object? _, PointerEventArgs e) => { events.Add("moved"); seen = e.GetPosition(button); },
                RoutingStrategies.Bubble);

            var outcome = UiAutomationDriver.Hover(button, null, null);

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Mechanism.Should().Be("pointer");
            events.Should().Equal(["entered", "moved"],
                "a tooltip service waits for Entered; the editor listens to Moved — both are needed");
            seen.Should().NotBeNull("an event with no usable position produces no tip");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Hover_DefaultsToTheCentreOfAnOrdinaryControl()
    {
        var button = new Button { Content = "Hover me", Width = 120, Height = 40 };
        var window = Show(button);
        try
        {
            Point? seen = null;
            button.AddHandler(InputElement.PointerMovedEvent,
                (object? _, PointerEventArgs e) => seen = e.GetPosition(button), RoutingStrategies.Bubble);

            UiAutomationDriver.Hover(button, null, null).Success.Should().BeTrue();

            seen!.Value.X.Should().BeApproximately(button.Bounds.Width / 2, 1.0);
            seen!.Value.Y.Should().BeApproximately(button.Bounds.Height / 2, 1.0);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Hover_HonoursAnExplicitPoint()
    {
        // The escape hatch for anything the defaults get wrong — and the only way to hover a specific
        // pixel of a surface that is neither a text editor nor uniform.
        var button = new Button { Content = "Hover me", Width = 120, Height = 40 };
        var window = Show(button);
        try
        {
            Point? seen = null;
            button.AddHandler(InputElement.PointerMovedEvent,
                (object? _, PointerEventArgs e) => seen = e.GetPosition(button), RoutingStrategies.Bubble);

            UiAutomationDriver.Hover(button, 12, 7).Success.Should().BeTrue();

            seen!.Value.X.Should().BeApproximately(12, 1.0);
            seen!.Value.Y.Should().BeApproximately(7, 1.0);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Hover_OnADetachedControl_FailsRatherThanRaisingIntoNothing()
    {
        // No window means no coordinate space. Raising anyway would report success for a hover that could
        // not have had a position — the failure shape this whole pass keeps finding.
        var orphan = new Button { Content = "nowhere" };

        var outcome = UiAutomationDriver.Hover(orphan, null, null);

        outcome.Success.Should().BeFalse();
        outcome.Error.Should().Contain("not attached");
    }

    // The declared tip belongs to the point hovered, not to whichever descendant of the target comes first:
    // a hover on the window that landed in a code editor once answered with the toolbar's "Add Project" (#610).

    [AvaloniaFact]
    public void Hover_OnAContainer_GivesThePathOfTheEditorThePointerWentTo()
    {
        // A hover on the window lands in the first editor under it. "hovered (0, 25.6) on TextEditor[Editor]"
        // did not say which editor (#610); the path does, and resolves back to it.
        var first = new TextEditor { Name = "First" };
        var second = new TextEditor { Name = "Second" };
        AutomationProperties.SetAutomationId(first, "First");
        AutomationProperties.SetAutomationId(second, "Second");
        var host = new StackPanel();
        host.Children.Add(first);
        host.Children.Add(second);
        var window = Show(host);
        try
        {
            var outcome = UiAutomationDriver.Hover(window, null, null, "Window", out var landing);

            outcome.Success.Should().BeTrue(outcome.Error);
            var path = outcome.Detail!.Split(", at ").Last();
            path.Should().StartWith("Window/", "the reply must carry a path, not only a class name");
            var (resolved, error) = UiAutomationDriver.Resolve(window, path);
            resolved.Should().NotBeNull(error);
            resolved.Should().BeSameAs(landing!.Value.Receiver).And.BeSameAs(first);
        }
        finally { window.Close(); }
    }

    private static (Window Window, Button Button) ToolbarBesideAPane()
    {
        var button = new Button
        {
            Content = "+", Width = 120, Height = 40,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
        };
        ToolTip.SetTip(button, "Add Project");
        var pane = new Border { Height = 200, Background = Avalonia.Media.Brushes.White };
        return (Show(new StackPanel { Children = { button, pane } }), button);
    }

    [AvaloniaFact]
    public void Hover_DeclaredTip_IsNotBorrowedFromAnUnrelatedDescendant()
    {
        var (window, _) = ToolbarBesideAPane();
        try
        {
            // Window centre: in the pane, well below the button.
            UiAutomationDriver.Hover(window, null, null, null, out var landing).Success.Should().BeTrue();

            UiAutomationDriver.DeclaredToolTipAt(landing!.Value.Receiver, landing.Value.Point)
                .Should().BeNull("nothing under the hovered point declares a tip, and the button is elsewhere");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Hover_DeclaredTip_IsTheOneUnderThePoint_EvenWhenTheTargetIsItsContainer()
    {
        var (window, button) = ToolbarBesideAPane();
        try
        {
            button.IsEnabled = false; // a disabled toolbar button still declares its tip

            UiAutomationDriver.Hover(window, 10, 10, null, out var landing).Success.Should().BeTrue();

            UiAutomationDriver.DeclaredToolTipAt(landing!.Value.Receiver, landing.Value.Point)
                .Should().Be("Add Project");
        }
        finally { window.Close(); }
    }

    // ── Saying when a node is not on screen (gap 15) ─────────────────────────────────

    [AvaloniaFact]
    public void A_collapsed_control_is_reported_as_hidden()
    {
        // The regression: a read-only banner bound to IsVisible showed in the tree of a form that was NOT
        // read-only, with nothing to distinguish it from a rendered one. Read as "the banner is showing",
        // that says a fresh, perfectly reproducible file is being held unsaveable — a serious bug, and one
        // entirely consistent with the change under test at the time.
        var banner = new TextBlock { Text = "Read-only", IsVisible = false };
        var window = Show(new StackPanel { Children = { banner, new TextBlock { Text = "Code" } } });
        try
        {
            var tree = UiAutomationDriver.Dump(window, "Window", maxDepth: 20, interactiveOnly: false);

            var node = Find(tree, n => n.Name == "Read-only");
            node.Should().NotBeNull("the node stays in the tree — it exists in the template");
            node!.IsHidden.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_showing_control_says_nothing_at_all()
    {
        // Null, not false, so the field is absent from the overwhelmingly common case: a dump runs to
        // hundreds of nodes and the hidden one is the exception worth spelling out.
        var window = Show(new TextBlock { Text = "Code" });
        try
        {
            var tree = UiAutomationDriver.Dump(window, "Window", maxDepth: 20, interactiveOnly: false);

            Find(tree, n => n.Name == "Code")!.IsHidden.Should().BeNull();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_visible_control_inside_a_collapsed_parent_is_hidden_too()
    {
        // Effective visibility, not the local flag. The child's own IsVisible is true, and reporting that
        // would move the same trap up one level instead of closing it.
        var child = new TextBlock { Text = "Inner" };
        var parent = new StackPanel { IsVisible = false, Children = { child } };
        var window = Show(new StackPanel { Children = { parent, new TextBlock { Text = "Code" } } });
        try
        {
            child.IsVisible.Should().BeTrue("the child's own flag is untouched — that is the point");

            var tree = UiAutomationDriver.Dump(window, "Window", maxDepth: 20, interactiveOnly: false);

            Find(tree, n => n.Name == "Inner")!.IsHidden.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Inspect_reports_hidden_for_a_single_control_too()
    {
        // inspect_element is where a caller checks one specific banner, so it must answer the same question.
        var banner = new TextBlock { Text = "Read-only", IsVisible = false };
        var window = Show(new StackPanel { Children = { banner } });
        try
        {
            var detail = UiAutomationDriver.Inspect(banner, "Window/Text[Read-only]");

            detail.IsHidden.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    private static UiNode? Find(UiNode node, Func<UiNode, bool> predicate)
    {
        if (predicate(node)) return node;
        foreach (var child in node.Children)
            if (Find(child, predicate) is { } hit) return hit;
        return null;
    }


    // ── Reading what the IDE said (dataContextMembers values) ────────────────────────

    private sealed class ValueVm
    {
        public string Text { get; set; } = "a runtime error message";
        public int Count { get; } = 42;
        public bool Flag { get; } = true;
        public string? Missing { get; }
        public string Empty { get; } = "";
        public AvaloniaEdit.Document.TextDocument Document { get; } = new("x = 1" + (char)10 + "y = 2");
        public object Complicated { get; } = new();
        public string Explodes => throw new InvalidOperationException("not ready");
        public System.Windows.Input.ICommand? DoIt => null;
    }

    [AvaloniaFact]
    public void Scalar_properties_report_their_value()
    {
        var members = UiAutomationDriver.ReflectDataContextMembers(new ValueVm());

        Member(members, "Text").Value.Should().Be("a runtime error message");
        Member(members, "Count").Value.Should().Be("42");
        Member(members, "Flag").Value.Should().Be("True");
    }

    [AvaloniaFact]
    public void A_TextDocument_reports_its_text()
    {
        // The Immediate window's contents are a TextDocument, not a string — the single most useful piece
        // of text in the IDE, and unreachable to a scalars-only reader.
        UiAutomationDriver.ReflectDataContextMembers(new ValueVm())
            .Single(m => m.Name == "Document").Value.Should().Be("x = 1" + (char)10 + "y = 2");
    }

    [AvaloniaFact]
    public void Null_means_not_read_and_empty_means_empty()
    {
        // The distinction is the point: an empty string is a fact about the value, null is a fact about
        // the reader. Collapsing them would make "no output yet" and "cannot see the output" identical.
        var members = UiAutomationDriver.ReflectDataContextMembers(new ValueVm());

        Member(members, "Empty").Value.Should().Be("");
        Member(members, "Missing").Value.Should().BeNull();
        Member(members, "Complicated").Value.Should().BeNull("an unsupported type is listed, not read");
    }

    [AvaloniaFact]
    public void A_throwing_getter_does_not_fail_the_inspection()
    {
        // A computed getter can easily depend on state that is not there yet. Inspecting a control must
        // not fail because one view-model property is unhappy.
        var members = UiAutomationDriver.ReflectDataContextMembers(new ValueVm());

        Member(members, "Explodes").Value.Should().BeNull();
        members.Should().Contain(m => m.Name == "Text", "the other members still come back");
    }

    [AvaloniaFact]
    public void A_command_is_still_listed_without_a_value()
    {
        var doIt = Member(UiAutomationDriver.ReflectDataContextMembers(new ValueVm()), "DoIt");

        doIt.Kind.Should().Be("command");
        doIt.Value.Should().BeNull();
    }

    [AvaloniaFact]
    public void A_long_value_is_truncated_and_says_so()
    {
        // Debug.Print output has no natural limit, and a tool result that grows without one is a different
        // failure from the one being fixed.
        var vm = new ValueVm { Text = new string('x', 9000) };

        var value = Member(UiAutomationDriver.ReflectDataContextMembers(vm), "Text").Value;

        value!.Length.Should().BeLessThan(9000);
        value.Should().EndWith("chars]").And.Contain("truncated");
    }

    // ── DataGrid rows ────────────────────────────────────────────────────────

    private sealed record GridItem(string Name, int Size);

    private static (Window window, DataGrid grid, DataGridRow[] rows) ShowGrid()
    {
        var grid = new DataGrid
        {
            ItemsSource = new[] { new GridItem("Alpha", 1), new GridItem("Beta", 2) },
            AutoGenerateColumns = true,
        };

        var window = new Window { Content = grid, Width = 400, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var rows = grid.GetVisualDescendants().OfType<DataGridRow>().ToArray();
        return (window, grid, rows);
    }

    [AvaloniaFact]
    public void A_grid_row_advertises_that_it_can_be_selected()
    {
        // Its peer offers nothing, so reporting the peer verbatim would tell an automation client that
        // every grid in the IDE is unselectable — and a token nobody is told about is a token nobody tries.
        var (window, _, rows) = ShowGrid();
        try
        {
            rows.Should().NotBeEmpty("the grid must realize its rows for this to test anything");

            var peer = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(rows[0]);

            UiAutomationDriver.DescribeProviders(peer, rows[0]).Should().Contain("selectionItem");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_Select_SelectsAGridRowThroughTheOwningGrid()
    {
        // The gesture a master-detail window is built on: click a row, read the detail. A DataGridRow's
        // automation peer exposes no ISelectionItemProvider, and the reflection actions cannot help either
        // — they set a property from a string and a selected row is an object no string names. So without
        // this fallback the protocol inspector was readable and undrivable.
        var (window, grid, rows) = ShowGrid();
        try
        {
            rows.Length.Should().BeGreaterThan(1);

            var outcome = UiAutomationDriver.Interact(rows[1], "select", null);

            outcome.Success.Should().BeTrue(outcome.Error);
            grid.SelectedItem.Should().Be(rows[1].DataContext);
        }
        finally { window.Close(); }
    }

    private static VmMember Member(VmMember[] members, string name) => members.Single(m => m.Name == name);
}
