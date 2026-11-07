using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// Focus in a flyout counts as in the window that owns it (#2030). On the desktop a flyout's items have a visual root of
/// their own, so the visual walk from one never reached the window: dump_visual_tree reported no focus, and press_key
/// moved focus out of the flyout before pressing, so Escape closed the dialog instead of the flyout. Headless popups sit
/// in the window's overlay, so these build the desktop's shape by hand: a closed popup, whose content has no visual parent
/// at all, placed at the control that owns it.
/// </summary>
public class FocusInPopupTests
{
    [AvaloniaFact]
    public void A_control_in_a_popup_is_under_the_control_the_popup_is_placed_at_and_its_window()
    {
        var owner = new TextBox();
        var window = new Window { Content = owner };
        var item = new Button { Content = "Paste" };
        _ = new Popup { PlacementTarget = owner, Child = new StackPanel { Children = { item } } };
        window.Show();
        try
        {
            UiAutomationDriver.IsUnderInView(window, item).Should().BeTrue();
            UiAutomationDriver.IsUnderInView(owner, item).Should().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_control_in_a_popup_placed_nowhere_is_not_under_the_window()
    {
        var window = new Window { Content = new TextBox() };
        var item = new Button { Content = "Paste" };
        _ = new Popup { Child = new StackPanel { Children = { item } } };
        window.Show();
        try
        {
            UiAutomationDriver.IsUnderInView(window, item).Should().BeFalse();
        }
        finally { window.Close(); }
    }
}
