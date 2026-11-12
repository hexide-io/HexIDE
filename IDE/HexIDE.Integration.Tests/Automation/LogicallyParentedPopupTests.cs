using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.LogicalTree;
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

    private sealed class OwnerViewModel { public string Title { get; set; } = "x"; }

    // A popup's content is in a visual tree of its own, so the walk for where a DataContext is set stopped at the popup, and
    // a control in it listed every member of what it inherited as though the context started there (#2892). The context is
    // on the owner and not the window because a headless popup is in the window's overlay layer: a walk up the visuals gets
    // to the window from there, as a real popup's can't, and only the popup's logical parent leads to the owner.
    [AvaloniaFact]
    public void A_control_in_a_popup_says_where_it_inherits_its_DataContext_from()
    {
        var item = new MenuItem { Header = "Properties" };
        var menu = new ContextMenu { ItemsSource = new[] { item } };
        var target = new Border { ContextMenu = menu, Width = 50, Height = 50, DataContext = new OwnerViewModel() };
        var window = Show(target);
        try
        {
            UiAutomationDriver.Interact(target, "expand", null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            item.DataContext.Should().BeOfType<OwnerViewModel>("the case is a context inherited through the popup");

            var inspected = UiAutomationDriver.Inspect(item, "Window/ContextMenu/MenuItem", window);

            inspected.DataContextOwner.Should().Be(UiAutomationDriver.PathWithin(window, "Window", target));
            inspected.DataContextMembers.Should().NotContain(m => m.Name == "Title");
            inspected.DataContextNote.Should().Contain("inherits its DataContext");
        }
        finally { window.Close(); }
    }

    // A context set on the popup's own content starts there, so it's still that content's, and the walk stops at it.
    [AvaloniaFact]
    public void A_DataContext_set_inside_a_popup_is_not_reported_as_inherited_from_outside_it()
    {
        var item = new MenuItem { Header = "Properties" };
        var menu = new ContextMenu { ItemsSource = new[] { item }, DataContext = new OwnerViewModel() };
        var target = new Border { ContextMenu = menu, Width = 50, Height = 50, DataContext = "outside" };
        var window = Show(target);
        try
        {
            UiAutomationDriver.Interact(target, "expand", null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();

            var inspected = UiAutomationDriver.Inspect(menu, "Window/ContextMenu", window);

            inspected.DataContextOwner.Should().BeNull();
            inspected.DataContextMembers.Should().Contain(m => m.Name == "Title");
        }
        finally { window.Close(); }
    }

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

    // A running Toolbar's dropdown: a MenuFlyout attached to its button and shown with ShowAt. The attached flyout was read
    // only for controls other than buttons, so the open menu was in neither the dump nor the snapshot (#2323).
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_open_flyout_attached_to_a_button_is_in_the_tree(bool toggle)
    {
        var flyout = new MenuFlyout { ItemsSource = new[] { new MenuItem { Header = "First" }, new MenuItem { Header = "Second" } } };
        Button button = toggle ? new ToggleButton { Content = "Menu" } : new Button { Content = "Menu" };
        FlyoutBase.SetAttachedFlyout(button, flyout);
        var window = Show(button);
        try
        {
            flyout.ShowAt(button);
            Dispatcher.UIThread.RunJobs();

            var tree = UiAutomationDriver.Dump(window, "Window", maxDepth: 20, interactiveOnly: false);

            // Headlessly a popup is an overlay inside the window, so the walk finds the items whatever this route says. On a
            // desktop it is a window of its own, which only the owner leads to: that route is what this guards.
            UiAutomationDriver.AttachedPopupsOfPublic(button).Should().ContainSingle().Which.Should().BeSameAs(flyout.Popup);
            Find(tree, "First").Should().NotBeNull("the menu is open on the button");
            Find(tree, "Second").Should().NotBeNull();
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

    // A Properties row's menu sits on the Border at the root of its template, which the dump shows as the row's None
    // child. The row is what a caller aims at, and expand on it was refused with no hint (#2877).
    [AvaloniaFact]
    public void Expand_on_a_row_reaches_the_menu_on_its_one_unnamed_wrapper()
    {
        var menu = new ContextMenu { ItemsSource = new[] { new MenuItem { Header = "Export picture..." } } };
        var wrapper = new Border { ContextMenu = menu, Width = 50, Height = 20, Child = new TextBlock { Text = "Picture" } };
        var row = new ContentControl { Content = wrapper };
        var window = Show(row);
        try
        {
            var opened = UiAutomationDriver.Interact(row, "expand", null);
            Dispatcher.UIThread.RunJobs();

            opened.Success.Should().BeTrue(opened.Error);
            menu.IsOpen.Should().BeTrue();
            opened.Detail.Should().Contain("the unnamed child of").And.Contain("that holds the menu");

            var closed = UiAutomationDriver.Interact(row, "collapse", null);
            Dispatcher.UIThread.RunJobs();

            closed.Success.Should().BeTrue(closed.Error);
            menu.IsOpen.Should().BeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Expand_on_a_node_with_two_unnamed_menu_wrappers_is_left_to_the_caller()
    {
        var first = new Border { ContextMenu = new ContextMenu(), Width = 50, Height = 20 };
        var second = new Border { ContextMenu = new ContextMenu(), Width = 50, Height = 20 };
        var row = new StackPanel { Children = { first, second } };
        var holder = new ContentControl { Content = row };
        var window = Show(holder);
        try
        {
            var refused = UiAutomationDriver.Interact(holder, "expand", null);

            refused.Success.Should().BeFalse();
            refused.Error.Should().Be("element does not support 'expand'");
            first.ContextMenu!.IsOpen.Should().BeFalse();
            second.ContextMenu!.IsOpen.Should().BeFalse();
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

    [AvaloniaFact]
    public void Escape_on_a_tree_node_closes_its_ancestors_context_flyout()
    {
        var command = new MenuItem { Header = "Properties" };
        var flyout = new MenuFlyout { Items = { command } };
        var item = new TreeViewItem { Header = "Form1", IsSelected = true };
        Avalonia.Automation.AutomationProperties.SetAutomationId(item, "Node");
        var tree = new TreeView { Items = { item }, ContextFlyout = flyout };
        var window = Show(tree);
        try
        {
            var path = UiAutomationDriver.PathWithin(window, "Window", item)!;
            var target = UiAutomationDriver.Resolve(window, path).control!;
            flyout.ShowAt(tree);
            Dispatcher.UIThread.RunJobs();
            command.Focus().Should().BeTrue();
            var menuPath = UiAutomationDriver.PathWithin(window, "Window", command)!;
            UiAutomationDriver.Resolve(window, path).control.Should().BeSameAs(target);

            var outcome = UiAutomationDriver.PressKey(target, "Escape", null, path);
            Dispatcher.UIThread.RunJobs();

            outcome.Success.Should().BeTrue(outcome.Error);
            flyout.IsOpen.Should().BeFalse();
            outcome.Detail.Should().Contain(menuPath).And.NotContain("moved focus");
            Find(UiAutomationDriver.Dump(window, "Window", 20, false), "Properties").Should().BeNull();
        }
        finally { flyout.Hide(); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("resolve")]
    [InlineData("invoke")]
    [InlineData("indexes")]
    public void Attached_menu_preserves_its_owners_paths(string check)
    {
        var invoked = false;
        var command = new MenuItem { Header = "Properties" };
        command.Click += (_, _) => invoked = true;
        var menu = new ContextMenu { Items = { command } };
        var item = new TreeViewItem { Header = "Form1", IsSelected = true };
        var tree = new TreeView { Items = { item }, ContextMenu = menu };
        var window = Show(tree);
        try
        {
            var before = UiAutomationDriver.Dump(window, "Window", 20, false);
            var itemPath = Find(before, "Form1")!.Path;
            var savedControl = UiAutomationDriver.Resolve(window, itemPath).control;
            savedControl.Should().NotBeNull();
            var ownerPath = UiAutomationDriver.PathWithin(window, "Window", tree)!;
            var owner = UiAutomationDriver.Dump(tree, ownerPath, 20, false);
            var paths = owner.Children.Select(c => c.Path).ToArray();
            paths.Should().NotBeEmpty();
            var controls = paths.Select(p => UiAutomationDriver.Resolve(window, p).control).ToArray();

            menu.Open(tree);
            Dispatcher.UIThread.RunJobs();

            if (check == "resolve")
            {
                UiAutomationDriver.Resolve(window, itemPath).control.Should().BeSameAs(savedControl);
                UiAutomationDriver.Resolve(window, paths[0]).control.Should().BeSameAs(controls[0]);
            }
            else if (check == "invoke")
            {
                var opened = UiAutomationDriver.Dump(tree, ownerPath, 20, false);
                var popup = opened.Children.SingleOrDefault(c => c.ControlType == "ContextMenu");
                popup.Should().NotBeNull();
                var path = Find(popup!, "Properties")!.Path;
                var resolved = UiAutomationDriver.Resolve(window, path);
                resolved.error.Should().BeNull();
                resolved.control.Should().BeSameAs(command);
                UiAutomationDriver.Inspect(command, path).Path.Should().Be(path);
                UiAutomationDriver.PressKey(resolved.control!, "Home", null, path).Success.Should().BeTrue();
                UiAutomationDriver.Interact(resolved.control!, "invoke", null).Success.Should().BeTrue();
                invoked.Should().BeTrue();
            }
            else
            {
                var opened = UiAutomationDriver.Dump(tree, ownerPath, 20, false);
                opened.Children.Where(c => c.ControlType != "ContextMenu").Select(c => c.Path)
                    .Should().Equal(paths);
            }
        }
        finally { menu.Close(); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(PopupRoute.ContextFlyout)]
    [InlineData(PopupRoute.AttachedFlyout)]
    [InlineData(PopupRoute.ButtonFlyout)]
    public void A_flyout_keeps_an_addressable_root(PopupRoute route)
    {
        var (target, _) = Owning(route);
        var window = Show(target);
        try
        {
            UiAutomationDriver.Interact(target, "expand", null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            var ownerPath = UiAutomationDriver.PathWithin(window, "Window", target)!;
            var dump = UiAutomationDriver.Dump(target, ownerPath, 20, false);
            var popup = dump.Children.SingleOrDefault(c => c.ControlType == "Flyout");
            popup.Should().NotBeNull();
            var item = Find(popup!, "Properties")!;
            UiAutomationDriver.Resolve(window, popup!.Path).control.Should().NotBeNull();
            UiAutomationDriver.Resolve(window, item.Path).control.Should().BeOfType<MenuItem>();
            UiAutomationDriver.Inspect(UiAutomationDriver.Resolve(window, popup.Path).control!, popup.Path)
                .Path.Should().Be(popup.Path);
        }
        finally { UiAutomationDriver.Interact(target, "collapse", null); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(PopupRoute.ContextMenu)]
    [InlineData(PopupRoute.ContextFlyout)]
    [InlineData(PopupRoute.AttachedFlyout)]
    [InlineData(PopupRoute.ButtonFlyout)]
    public void Inspect_PopupOwner_ReportsExpandCollapseState(PopupRoute route)
    {
        var (target, isOpen) = Owning(route);
        var window = Show(target);
        try
        {
            var closed = UiAutomationDriver.Inspect(target, "owner");
            closed.Providers.Should().Contain("expandCollapse");
            closed.ExpandCollapseState.Should().Be("Collapsed");

            UiAutomationDriver.Interact(target, "expand", null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            isOpen().Should().BeTrue();
            UiAutomationDriver.Inspect(target, "owner").ExpandCollapseState.Should().Be("Expanded");

            UiAutomationDriver.Interact(target, "collapse", null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            UiAutomationDriver.Inspect(target, "owner").ExpandCollapseState.Should().Be("Collapsed");
        }
        finally { UiAutomationDriver.Interact(target, "collapse", null); window.Close(); }
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

    [AvaloniaTheory]
    [InlineData("collapse")]
    [InlineData("dump")]
    [InlineData("expand")]
    public void A_shared_context_menu_is_open_only_on_its_current_control(string action)
    {
        var cut = new MenuItem { Header = "Cut" };
        var menu = new ContextMenu { Items = { cut } };
        var a = new Button { Content = "A", ContextMenu = menu };
        var b = new Button { Content = "B", ContextMenu = menu };
        var window = Show(new StackPanel { Children = { a, b } });
        try
        {
            menu.Open(a);
            Dispatcher.UIThread.RunJobs();

            if (action == "collapse")
            {
                Detail(UiAutomationDriver.Interact(b, "collapse", null)).Should()
                    .Be("the context menu on 'B' was not open; there is nothing to close");
                menu.IsOpen.Should().BeTrue();
                ((Popup)menu.GetLogicalParent()!).Parent.Should().BeSameAs(a);
            }
            else if (action == "dump")
            {
                var paths = UiAutomationDriver.Find(UiAutomationDriver.Dump(window, "Window", 20, false),
                    "Cut", 50).Matches.Select(m => m.Path).ToList();
                paths.Should().Contain(p => p.Contains("Button[A]/"));
                paths.Should().NotContain(p => p.Contains("Button[B]/"));
                var path = paths.Single(p => p.Contains("Button[A]/"));
                UiAutomationDriver.Resolve(window, path).control.Should().BeSameAs(cut);
                UiAutomationDriver.Resolve(window, path.Replace("Button[A]/", "Button[B]/"))
                    .control.Should().BeNull();
            }
            else
            {
                Detail(UiAutomationDriver.Interact(b, "expand", null)).Should().Be("opened context menu on 'B'");
                menu.IsOpen.Should().BeTrue();
                ((Popup)menu.GetLogicalParent()!).Parent.Should().BeSameAs(b);
            }
        }
        finally { menu.Close(); window.Close(); }
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
