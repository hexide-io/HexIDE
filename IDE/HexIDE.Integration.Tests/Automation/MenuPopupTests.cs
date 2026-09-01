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
/// </summary>
public class MenuPopupTests
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
        // MenuItemAutomationPeer exposes no providers at all, so reporting the peer verbatim showed a menu
        // as undrivable — and a token nobody sees is a token nobody tries. Discovery has to agree with
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
