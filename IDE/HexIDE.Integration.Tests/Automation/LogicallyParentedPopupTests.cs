using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// A menu-bar dropdown's Popup is a visual child of its MenuItem, so an ordinary visual walk finds it. A
/// ContextMenu and a Button.Flyout are attached with ISetLogicalParent.SetParent instead — their Popup is
/// never a visual child of anything. That one asymmetry produced two separate gap entries: "a context menu
/// opens but is invisible" and "a MenuFlyout's items are invisible". Same cause, two faces.
/// </summary>
public class LogicallyParentedPopupTests
{
    private static Window Show(Control content)
    {
        var window = new Window { Content = content, Width = 400, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static UiNode? Find(UiNode node, string name) =>
        node.Name == name ? node : node.Children.Select(c => Find(c, name)).FirstOrDefault(n => n is not null);

    [AvaloniaFact]
    public void An_open_flyouts_items_are_in_the_tree()
    {
        var flyout = new MenuFlyout
        {
            ItemsSource = new[] { new MenuItem { Header = "Add Form" }, new MenuItem { Header = "Add Module" } },
        };
        var button = new Button { Content = "Add", Flyout = flyout };
        var window = Show(button);
        try
        {
            flyout.ShowAt(button);
            Dispatcher.UIThread.RunJobs();

            var tree = UiAutomationDriver.Dump(window, "Window", maxDepth: 20, interactiveOnly: false);

            Find(tree, "Add Form").Should().NotBeNull("eight toolbar commands hung on exactly this");
            Find(tree, "Add Module").Should().NotBeNull();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void An_open_context_menus_items_are_in_the_tree()
    {
        // Reached differently from a flyout, and the difference is measured rather than assumed: an open
        // ContextMenu is the popup's CHILD, so the popup is its logical PARENT.
        var menu = new ContextMenu { ItemsSource = new[] { new MenuItem { Header = "Properties" } } };
        var target = new Border { ContextMenu = menu, Width = 50, Height = 50 };
        var window = Show(target);
        try
        {
            menu.Open(target);
            Dispatcher.UIThread.RunJobs();

            var tree = UiAutomationDriver.Dump(window, "Window", maxDepth: 20, interactiveOnly: false);

            Find(tree, "Properties").Should().NotBeNull();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_closed_flyout_contributes_nothing()
    {
        // Nothing is realised, so there is nothing to report and nothing to address. Reporting items for a
        // shut menu would be the same false-positive class as a hidden node claiming to be on screen.
        var flyout = new MenuFlyout { ItemsSource = new[] { new MenuItem { Header = "Add Form" } } };
        var button = new Button { Content = "Add", Flyout = flyout };
        var window = Show(button);
        try
        {
            var tree = UiAutomationDriver.Dump(window, "Window", maxDepth: 20, interactiveOnly: false);

            Find(tree, "Add Form").Should().BeNull();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void An_items_path_round_trips_back_to_the_live_control()
    {
        // A path that cannot be fed back is only half an answer: the point is to drive the command.
        var flyout = new MenuFlyout { ItemsSource = new[] { new MenuItem { Header = "Add Form" } } };
        var button = new Button { Content = "Add", Flyout = flyout };
        var window = Show(button);
        try
        {
            flyout.ShowAt(button);
            Dispatcher.UIThread.RunJobs();
            var path = Find(UiAutomationDriver.Dump(window, "Window", 20, false), "Add Form")!.Path;

            var (control, error) = UiAutomationDriver.Resolve(window, path);

            error.Should().BeNull();
            control.Should().BeOfType<MenuItem>();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_invoke_on_the_button_opens_its_flyout()
    {
        // The gap entry said invoke "reports success and does nothing visible — it fires the button's own
        // invoke, which is not what opens a flyout". That explanation is wrong: Button.OnClick opens the
        // flyout, and the entry sent the next reader looking for a fix to a problem that is not there. What
        // was really broken was seeing the result, which the tests above now cover.
        var flyout = new MenuFlyout { ItemsSource = new[] { new MenuItem { Header = "Add Form" } } };
        var button = new Button { Content = "Add", Flyout = flyout };
        var window = Show(button);
        try
        {
            UiAutomationDriver.Interact(button, "invoke", null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();

            flyout.IsOpen.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Expand_opens_a_context_menu_which_nothing_else_can()
    {
        // press_key Apps opens one too, but through ContextRequested, which lands on whichever control the
        // input layer picks rather than the one addressed. This opens the menu belonging to the named
        // control, which is what an assertion needs.
        var menu = new ContextMenu { ItemsSource = new[] { new MenuItem { Header = "Properties" } } };
        var target = new Border { ContextMenu = menu, Width = 50, Height = 50 };
        var window = Show(target);
        try
        {
            UiAutomationDriver.Interact(target, "expand", null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();

            menu.IsOpen.Should().BeTrue();
            Find(UiAutomationDriver.Dump(window, "Window", 20, false), "Properties").Should().NotBeNull(
                "opening it is only half the job — the items have to be addressable");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Expand_opens_a_flyout_and_collapse_shuts_it()
    {
        var flyout = new MenuFlyout { ItemsSource = new[] { new MenuItem { Header = "Add Form" } } };
        var button = new Button { Content = "Add", Flyout = flyout };
        var window = Show(button);
        try
        {
            UiAutomationDriver.Interact(button, "expand", null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            flyout.IsOpen.Should().BeTrue();

            UiAutomationDriver.Interact(button, "collapse", null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            flyout.IsOpen.Should().BeFalse();
        }
        finally { window.Close(); }
    }

    public enum PopupRoute { ContextMenu, ContextFlyout, AttachedFlyout, ButtonFlyout }

    private static (Control Target, Func<bool> IsOpen) Owning(PopupRoute route)
    {
        var items = new[] { new MenuItem { Header = "Properties" } };
        switch (route)
        {
            case PopupRoute.ContextMenu:
                var menu = new ContextMenu { ItemsSource = items };
                return (new Border { ContextMenu = menu, Width = 50, Height = 50 }, () => menu.IsOpen);
            case PopupRoute.ContextFlyout:
                var context = new MenuFlyout { ItemsSource = items };
                return (new Border { ContextFlyout = context, Width = 50, Height = 50 }, () => context.IsOpen);
            case PopupRoute.AttachedFlyout:
                var attached = new MenuFlyout { ItemsSource = items };
                var border = new Border { Width = 50, Height = 50 };
                FlyoutBase.SetAttachedFlyout(border, attached);
                return (border, () => attached.IsOpen);
            default:
                var flyout = new MenuFlyout { ItemsSource = items };
                return (new Button { Content = "Add", Flyout = flyout }, () => flyout.IsOpen);
        }
    }

    [AvaloniaTheory]
    [InlineData(PopupRoute.ContextMenu, "context menu")]
    [InlineData(PopupRoute.ContextFlyout, "flyout")]
    [InlineData(PopupRoute.AttachedFlyout, "flyout")]
    [InlineData(PopupRoute.ButtonFlyout, "flyout")]
    public void Each_reply_says_whether_the_popup_was_already_open_or_closed(PopupRoute route, string popup)
    {
        // expandCollapse is reported because of the popup, so "does not support 'collapse'" contradicted the token
        // still shown, and a second expand was reported as opening what was open (#949).
        var (target, isOpen) = Owning(route);
        var window = Show(target);
        try
        {
            Detail(UiAutomationDriver.Interact(target, "collapse", null)).Should().MatchRegex($"^the {popup} on '.*' was not open; there is nothing to close$");

            Detail(UiAutomationDriver.Interact(target, "expand", null)).Should().StartWith($"opened {popup} on '");
            Dispatcher.UIThread.RunJobs();
            isOpen().Should().BeTrue();
            Detail(UiAutomationDriver.Interact(target, "expand", null)).Should().MatchRegex($"^the {popup} on '.*' was already open$");

            Detail(UiAutomationDriver.Interact(target, "collapse", null)).Should().StartWith($"closed {popup} on '");
            Dispatcher.UIThread.RunJobs();
            isOpen().Should().BeFalse();
            Detail(UiAutomationDriver.Interact(target, "collapse", null)).Should().MatchRegex($"^the {popup} on '.*' was not open; there is nothing to close$");
        }
        finally { window.Close(); }
    }

    private static string? Detail(InteractOutcome outcome)
    {
        outcome.Success.Should().BeTrue(outcome.Error);
        return outcome.Detail;
    }

    [AvaloniaFact]
    public void Collapse_closes_an_open_context_flyout_beside_a_closed_context_menu()
    {
        // "Not open" is a success, so checking the context menu alone would report the open flyout as nothing to close.
        var flyout = new MenuFlyout { ItemsSource = new[] { new MenuItem { Header = "Cut" } } };
        var target = new Border { ContextMenu = new ContextMenu(), ContextFlyout = flyout, Width = 50, Height = 50 };
        var window = Show(target);
        try
        {
            flyout.ShowAt(target);
            Dispatcher.UIThread.RunJobs();

            Detail(UiAutomationDriver.Interact(target, "collapse", null)).Should().StartWith("closed flyout on '");
            flyout.IsOpen.Should().BeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_shared_flyout_open_on_one_control_is_not_closed_from_another()
    {
        // The theme's TextBox ContextFlyout is one instance for every box, so IsOpen alone is true on all of them.
        var flyout = new MenuFlyout { ItemsSource = new[] { new MenuItem { Header = "Cut" } } };
        var a = new Button { Content = "A", ContextFlyout = flyout };
        var b = new Button { Content = "B", ContextFlyout = flyout };
        var window = Show(new StackPanel { Children = { a, b } });
        try
        {
            flyout.ShowAt(a);
            Dispatcher.UIThread.RunJobs();

            Detail(UiAutomationDriver.Interact(b, "collapse", null)).Should().EndWith("was not open; there is nothing to close");
            flyout.IsOpen.Should().BeTrue();
            var paths = UiAutomationDriver.Find(UiAutomationDriver.Dump(window, "Window", 20, false), "Cut", 50).Matches.Select(m => m.Path).ToList();
            paths.Should().Contain(p => p.Contains("Button[A]/"), "the flyout is listed under the control it is open on");
            paths.Should().NotContain(p => p.Contains("Button[B]/"), "and not under another that shares it");
            Detail(UiAutomationDriver.Interact(a, "collapse", null)).Should().StartWith("closed flyout on '");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_dropdown_already_closed_or_open_says_so()
    {
        var combo = new ComboBox { ItemsSource = new[] { "One", "Two" }, Width = 100 };
        var window = Show(combo);
        try
        {
            Detail(UiAutomationDriver.Interact(combo, "collapse", null)).Should().EndWith("was already collapsed");
            Detail(UiAutomationDriver.Interact(combo, "expand", null)).Should().StartWith("expanded '");
            Dispatcher.UIThread.RunJobs();
            Detail(UiAutomationDriver.Interact(combo, "expand", null)).Should().EndWith("was already expanded");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Collapse_on_a_control_with_no_popup_is_still_unsupported()
    {
        var target = new Border { Width = 50, Height = 50 };
        var window = Show(target);
        try
        {
            UiAutomationDriver.Interact(target, "collapse", null).Error.Should().Be("element does not support 'collapse'");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_control_owning_a_popup_advertises_expandCollapse()
    {
        // The verb existing and nothing saying so is how eight toolbar commands came to be recorded as
        // unreachable: the entry's author tried expand, was told it was unsupported, and believed it.
        var menu = new ContextMenu { ItemsSource = new[] { new MenuItem { Header = "Properties" } } };
        var target = new Border { ContextMenu = menu, Width = 50, Height = 50 };
        var button = new Button { Content = "Add", Flyout = new MenuFlyout() };
        var plain = new Border { Width = 10, Height = 10 };
        var window = Show(new StackPanel { Children = { target, button, plain } });
        try
        {
            var tree = UiAutomationDriver.Dump(window, "Window", 20, false);

            FindByType(tree, "Button")!.Providers.Should().Contain("expandCollapse",
                "the button owns a flyout");
        }
        finally { window.Close(); }
    }

    private static UiNode? FindByType(UiNode node, string type) =>
        node.ControlType == type ? node : node.Children.Select(c => FindByType(c, type)).FirstOrDefault(n => n is not null);

    [AvaloniaFact]
    public void A_ContextFlyout_is_found_too_and_it_is_how_this_codebase_writes_them()
    {
        // The third attachment route, and the one that mattered. The Project Explorer's context menu is a
        // MenuFlyout on TreeView.ContextFlyout (Tools/Projects/ProjectToolView.axaml:125) — not a
        // ContextMenu, and not on the TreeViewItem. Handling only the first two made an open, plainly
        // visible menu report nothing, which reads exactly like a menu that never opened.
        var flyout = new MenuFlyout { ItemsSource = new[] { new MenuItem { Header = "Properties" } } };
        var tree = new TreeView { ContextFlyout = flyout, Width = 100, Height = 100 };
        var window = Show(tree);
        try
        {
            UiAutomationDriver.Interact(tree, "expand", null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();

            flyout.IsOpen.Should().BeTrue();
            Find(UiAutomationDriver.Dump(window, "Window", 20, false), "Properties").Should().NotBeNull();
        }
        finally { window.Close(); }
    }
}
