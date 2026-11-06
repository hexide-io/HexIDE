using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// Guards reaching inside an open menu (gap 12 in docs/mcp-server-gaps.md).
///
/// A menu's items are realised in a popup, which is its own visual root — not a visual child of the
/// MenuItem. So a plain visual walk reported every top-level `MenuItem` with `"children": []`, and the
/// contents of a menu (separators, shortcut gestures, enabled/checked state, submenu nesting) could only
/// be asserted as data, never through the tools that drive the live IDE.
///
/// And it could not be opened either: `MenuItem` exposes no ExpandCollapse provider, so `interact expand`
/// failed outright. The two halves compound — a menu that cannot be opened has nothing to look inside.
///
/// And driving one did less than a click (#947): invoke left the menu open and moved no tick, and collapse on a bar
/// header left the bar lit and taking keys.
/// </summary>
public class MenuPopupTests : WindowTestFixture
{
    private static (Window window, Menu menu, MenuItem file) BuildMenuWindow()
    {
        var open = new MenuItem { Header = "Open" };
        var recent = new MenuItem { Header = "Recent", Items = { new MenuItem { Header = "Project1.vbp" } } };
        var file = new MenuItem
        {
            Header = "File",
            Items = { open, new Separator(), recent },
        };
        var menu = new Menu { Items = { file } };
        var window = new Window { Content = menu };
        window.Show();
        return (window, menu, file);
    }

    [AvaloniaFact]
    public void A_closed_menu_reports_no_children()
    {
        var (window, _, _) = BuildMenuWindow();

        var dump = UiAutomationDriver.Dump(window, "Window", 20, interactiveOnly: false);
        var fileNode = Find(dump, "File");

        fileNode.Should().NotBeNull();
        fileNode!.Children.Should().BeEmpty("a closed menu has not realised its items yet");
    }

    [AvaloniaFact]
    public void Expand_opens_a_menu_that_has_no_expand_collapse_provider()
    {
        var (_, _, file) = BuildMenuWindow();

        var outcome = UiAutomationDriver.Interact(file, "expand", null);

        outcome.Success.Should().BeTrue();
        file.IsSubMenuOpen.Should().BeTrue();
    }

    [AvaloniaFact]
    public void An_open_menu_exposes_its_items_in_the_tree()
    {
        var (window, _, file) = BuildMenuWindow();
        UiAutomationDriver.Interact(file, "expand", null);

        var dump = UiAutomationDriver.Dump(window, "Window", 20, interactiveOnly: false);
        var fileNode = Find(dump, "File");

        fileNode!.Children.Should().NotBeEmpty("an open menu's items live in its popup, which the walk now crosses into");
        Find(dump, "Open").Should().NotBeNull();
        Find(dump, "Recent").Should().NotBeNull();
    }

    [AvaloniaFact]
    public void An_item_inside_an_open_menu_addresses_and_round_trips()
    {
        // The point of the tree change: a path emitted for a popup item must resolve back to the live
        // control, or interact/press_key still cannot reach anything inside a menu.
        var (window, _, file) = BuildMenuWindow();
        UiAutomationDriver.Interact(file, "expand", null);

        var dump = UiAutomationDriver.Dump(window, "Window", 20, interactiveOnly: false);
        var openNode = Find(dump, "Open");
        openNode.Should().NotBeNull();

        var (resolved, error) = UiAutomationDriver.Resolve(window, openNode!.Path);

        error.Should().BeNull();
        resolved.Should().BeOfType<MenuItem>().Which.Header.Should().Be("Open");
    }

    [AvaloniaFact]
    public void An_item_inside_an_open_menu_can_be_invoked()
    {
        var (window, _, file) = BuildMenuWindow();
        UiAutomationDriver.Interact(file, "expand", null);
        var dump = UiAutomationDriver.Dump(window, "Window", 20, interactiveOnly: false);
        var (openItem, _) = UiAutomationDriver.Resolve(window, Find(dump, "Open")!.Path);

        var clicked = false;
        ((MenuItem)openItem!).Click += (_, _) => clicked = true;

        var outcome = UiAutomationDriver.Interact(openItem!, "invoke", null);

        outcome.Success.Should().BeTrue();
        clicked.Should().BeTrue();
    }

    [AvaloniaFact]
    public void Collapse_closes_an_open_menu()
    {
        var (_, _, file) = BuildMenuWindow();
        UiAutomationDriver.Interact(file, "expand", null);

        var outcome = UiAutomationDriver.Interact(file, "collapse", null);

        outcome.Success.Should().BeTrue();
        file.IsSubMenuOpen.Should().BeFalse();
    }

    // #947: the menu, and its bar, stayed open after invoke, and collapse left the header lit with the bar taking keys.
    // A real click on an item closes the whole menu; a second click on a header closes the bar.

    [AvaloniaFact]
    public void Expand_on_a_header_opens_the_bar_as_a_click_does()
    {
        var (_, menu, file) = BuildMenuWindow();

        UiAutomationDriver.Interact(file, "expand", null).Success.Should().BeTrue();

        menu.IsOpen.Should().BeTrue();
    }

    [AvaloniaFact]
    public void Invoke_on_an_item_closes_the_whole_menu_as_a_click_does()
    {
        var (_, menu, file) = BuildMenuWindow();
        UiAutomationDriver.Interact(file, "expand", null);
        var open = file.Items.OfType<MenuItem>().First();

        var outcome = UiAutomationDriver.Interact(open, "invoke", null);

        outcome.Success.Should().BeTrue(outcome.Error);
        outcome.Detail.Should().Be("invoked MenuItem 'Open' and closed the menu, as a click does");
        file.IsSubMenuOpen.Should().BeFalse();
        menu.IsOpen.Should().BeFalse();
        file.IsSelected.Should().BeFalse("the header is not left lit");
    }

    [AvaloniaFact]
    public void Invoke_on_an_item_that_stays_open_on_click_leaves_the_menu_open()
    {
        var (_, _, file) = BuildMenuWindow();
        var sticky = new MenuItem { Header = "Sticky", StaysOpenOnClick = true };
        file.Items.Add(sticky);
        UiAutomationDriver.Interact(file, "expand", null);

        var outcome = UiAutomationDriver.Interact(sticky, "invoke", null);

        outcome.Detail.Should().Be("invoked MenuItem 'Sticky'");
        file.IsSubMenuOpen.Should().BeTrue();
    }

    [AvaloniaTheory]
    [InlineData(false, "(now ticked)")]
    [InlineData(true, "(now unticked)")]
    public void Invoke_on_a_checkable_item_moves_its_tick_as_a_click_does(bool wasChecked, string said)
    {
        var (_, _, file) = BuildMenuWindow();
        var minimap = new MenuItem { Header = "Minimap", ToggleType = MenuItemToggleType.CheckBox, IsChecked = wasChecked };
        file.Items.Add(minimap);
        UiAutomationDriver.Interact(file, "expand", null);

        var outcome = UiAutomationDriver.Interact(minimap, "invoke", null);

        outcome.Success.Should().BeTrue(outcome.Error);
        minimap.IsChecked.Should().Be(!wasChecked);
        outcome.Detail.Should().Be($"invoked MenuItem 'Minimap' {said} and closed the menu, as a click does");
    }

    [AvaloniaFact]
    public void Invoke_on_a_disabled_item_is_refused_and_raises_no_click()
    {
        var (_, _, file) = BuildMenuWindow();
        var disabled = new MenuItem { Header = "Disabled", IsEnabled = false };
        var clicked = false;
        disabled.Click += (_, _) => clicked = true;
        file.Items.Add(disabled);
        UiAutomationDriver.Interact(file, "expand", null);

        var outcome = UiAutomationDriver.Interact(disabled, "invoke", null);

        outcome.Success.Should().BeFalse();
        outcome.Error.Should().Contain("disabled");
        clicked.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Toggle_on_a_menu_item_says_to_invoke_it()
    {
        var (_, _, file) = BuildMenuWindow();
        var minimap = new MenuItem { Header = "Minimap", ToggleType = MenuItemToggleType.CheckBox };
        file.Items.Add(minimap);
        UiAutomationDriver.Interact(file, "expand", null);

        var outcome = UiAutomationDriver.Interact(minimap, "toggle", null);

        outcome.Success.Should().BeFalse();
        outcome.Error.Should().Contain("'invoke' clicks it");
        minimap.IsChecked.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Toggle_on_a_menu_item_with_no_tick_says_so_and_runs_nothing()
    {
        var (_, _, file) = BuildMenuWindow();
        UiAutomationDriver.Interact(file, "expand", null);
        var open = file.Items.OfType<MenuItem>().First();
        var clicked = false;
        open.Click += (_, _) => clicked = true;

        var outcome = UiAutomationDriver.Interact(open, "toggle", null);

        outcome.Success.Should().BeFalse();
        outcome.Error.Should().Contain("has no tick to toggle");
        clicked.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Collapse_on_a_header_leaves_the_bar_as_a_second_click_does()
    {
        var (_, menu, file) = BuildMenuWindow();
        UiAutomationDriver.Interact(file, "expand", null);

        var outcome = UiAutomationDriver.Interact(file, "collapse", null);

        outcome.Detail.Should().Be("closed menu 'File' and left the menu bar");
        menu.IsOpen.Should().BeFalse();
        file.IsSelected.Should().BeFalse("the header is not left lit");
    }

    [AvaloniaFact]
    public void Expand_on_an_open_menu_says_it_was_already_open()
    {
        var (_, _, file) = BuildMenuWindow();
        UiAutomationDriver.Interact(file, "expand", null).Detail.Should().Be("opened menu 'File'");

        var outcome = UiAutomationDriver.Interact(file, "expand", null);

        outcome.Success.Should().BeTrue(outcome.Error);
        outcome.Detail.Should().Be("menu 'File' was already open");
        file.IsSubMenuOpen.Should().BeTrue();
    }

    [AvaloniaFact]
    public void Collapse_on_a_closed_header_says_there_is_nothing_to_close()
    {
        var (_, _, file) = BuildMenuWindow();

        var outcome = UiAutomationDriver.Interact(file, "collapse", null);

        outcome.Success.Should().BeTrue(outcome.Error);
        outcome.Detail.Should().Contain("was already closed").And.Contain("nothing to close");
    }

    // Alt opens the bar with no menu showing.
    [AvaloniaFact]
    public void Collapse_on_a_header_of_an_active_bar_says_it_left_the_bar_not_that_it_closed_the_menu()
    {
        var (_, menu, file) = BuildMenuWindow();
        menu.Open();

        var outcome = UiAutomationDriver.Interact(file, "collapse", null);

        outcome.Detail.Should().Be("menu 'File' was not open, but the menu bar was; left the menu bar");
        menu.IsOpen.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Collapse_on_a_submenu_closes_only_that_submenu()
    {
        var (_, menu, file) = BuildMenuWindow();
        UiAutomationDriver.Interact(file, "expand", null);
        var recent = file.Items.OfType<MenuItem>().Single(i => (string?)i.Header == "Recent");
        UiAutomationDriver.Interact(recent, "expand", null);

        var outcome = UiAutomationDriver.Interact(recent, "collapse", null);

        outcome.Detail.Should().Be("closed menu 'Recent'");
        recent.IsSubMenuOpen.Should().BeFalse();
        file.IsSubMenuOpen.Should().BeTrue();
    }

    [AvaloniaFact]
    public void Invoke_on_a_context_menu_item_closes_the_context_menu()
    {
        var copy = new MenuItem { Header = "Copy" };
        var owner = new Border { Width = 50, Height = 50, ContextMenu = new ContextMenu { Items = { copy } } };
        var window = new Window { Content = owner };
        window.Show();
        try
        {
            UiAutomationDriver.Interact(owner, "expand", null).Success.Should().BeTrue();
            owner.ContextMenu!.IsOpen.Should().BeTrue();

            var outcome = UiAutomationDriver.Interact(copy, "invoke", null);

            outcome.Detail.Should().EndWith("and closed the menu, as a click does");
            owner.ContextMenu.IsOpen.Should().BeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_leaf_item_still_reports_expand_as_unsupported()
    {
        // The MenuItem fallback must not claim to open something with no submenu — that would turn a
        // clear "does not support expand" into a silent no-op the caller reads as success.
        var (window, _, file) = BuildMenuWindow();
        UiAutomationDriver.Interact(file, "expand", null);
        var dump = UiAutomationDriver.Dump(window, "Window", 20, interactiveOnly: false);
        var (leaf, _) = UiAutomationDriver.Resolve(window, Find(dump, "Open")!.Path);

        var outcome = UiAutomationDriver.Interact(leaf!, "expand", null);

        outcome.Success.Should().BeFalse();
        outcome.Error.Should().Contain("expand");
    }

    [AvaloniaFact]
    public void A_separator_is_visible_in_its_place_in_the_menu()
    {
        // A separator has no automation type, no provider and no focus, so the control-view rule called it
        // plumbing and dropped it. But its POSITION is exactly what a VB6-fidelity check asks about, and a
        // dump that omits it cannot answer "is the rule above Recent or below it".
        var (window, _, file) = BuildMenuWindow();
        UiAutomationDriver.Interact(file, "expand", null);

        var dump = UiAutomationDriver.Dump(window, "Window", 20, interactiveOnly: false);
        var items = Flatten(Find(dump, "File")!)
            .Where(n => n.ControlType is "MenuItem" or "Separator")
            .Select(n => n.ControlType == "Separator" ? "---" : n.Name)
            .ToList();

        items.Should().ContainInOrder("Open", "---", "Recent");
    }

    [AvaloniaFact]
    public void A_menu_advertises_the_actions_it_accepts()
    {
        // MenuItemAutomationPeer exposes no invoke or expandCollapse provider, so reporting the peer verbatim showed a
        // menu as undrivable — and a token nobody sees is a token nobody tries. Discovery has to agree with
        // what Interact will actually do, or the fix is unreachable through the documented workflow.
        var (window, _, file) = BuildMenuWindow();
        UiAutomationDriver.Interact(file, "expand", null);
        var dump = UiAutomationDriver.Dump(window, "Window", 20, interactiveOnly: false);

        Find(dump, "File")!.Providers.Should().Contain(["invoke", "expandCollapse"]);

        // A leaf gets invoke but not expandCollapse: it has nothing to open.
        var leaf = Find(dump, "Open")!;
        leaf.Providers.Should().Contain("invoke");
        leaf.Providers.Should().NotContain("expandCollapse");
    }

    [AvaloniaFact]
    public void A_checkable_menu_item_does_not_advertise_toggle()
    {
        // From Avalonia 12.0.5 the peer of a checkable item offers a toggle, but it only sets IsChecked: no Click, so
        // neither a program's handler nor an IDE command runs. Toggle is refused on every menu item for that reason, so
        // advertising it would promise a verb that only refuses.
        var (window, _, file) = BuildMenuWindow();
        file.Items.Add(new MenuItem { Header = "Minimap", ToggleType = MenuItemToggleType.CheckBox });
        UiAutomationDriver.Interact(file, "expand", null);
        var dump = UiAutomationDriver.Dump(window, "Window", 20, interactiveOnly: false);

        Find(dump, "Minimap")!.Providers.Should().Contain("invoke").And.NotContain("toggle");
    }

    private static UiNode? Find(UiNode node, string name)
    {
        if (node.Name == name) return node;
        foreach (var c in node.Children)
        {
            if (Find(c, name) is { } hit) return hit;
        }
        return null;
    }

    private static IEnumerable<UiNode> Flatten(UiNode node)
    {
        yield return node;
        foreach (var c in node.Children)
        {
            foreach (var d in Flatten(c)) yield return d;
        }
    }
}
