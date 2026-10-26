using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// Focus in a flyout counts as in the window that owns it (#2030). On the desktop a flyout's items have a visual root of
/// their own, so the visual walk from one never reached the window: dump_visual_tree reported no focus, and press_key
/// moved focus out of the flyout before pressing, so Escape closed the dialog instead of the flyout. Headless popups
/// sit in the window's overlay, so these build that shape by hand: a root with no visual parent whose logical parent is
/// the owner, as a desktop popup's is.
/// </summary>
public class FocusInPopupTests
{
    [AvaloniaFact]
    public void A_control_in_a_popup_root_is_under_the_window_that_owns_the_popup()
    {
        var owner = new TextBox();
        var window = new Window { Content = owner };
        var item = new Button { Content = "Paste" };
        var popupRoot = new Border { Child = item };
        ((ISetLogicalParent)popupRoot).SetParent(owner);
        try
        {
            UiAutomationDriver.IsUnderInView(window, item).Should().BeTrue();
            UiAutomationDriver.IsUnderInView(owner, item).Should().BeTrue();
        }
        finally { ((ISetLogicalParent)popupRoot).SetParent(null); }
    }

    [AvaloniaFact]
    public void A_control_in_an_unowned_root_is_not_under_the_window()
    {
        var window = new Window { Content = new TextBox() };
        var item = new Button { Content = "Paste" };
        _ = new Border { Child = item };

        UiAutomationDriver.IsUnderInView(window, item).Should().BeFalse();
    }
}
