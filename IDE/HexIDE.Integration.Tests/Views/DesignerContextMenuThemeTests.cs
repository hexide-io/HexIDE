using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HexIDE.Automation;
using HexIDE.Controls;
using HexIDE.Integration.Tests.Automation;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The form designer's context menu is IDE chrome, so it follows the IDE's theme, while the canvas it opens from stays
/// Light (#3400). A popup takes the theme of what it opened from, so without this the menu is bright white under a dark pack.
/// </summary>
public class DesignerContextMenuThemeTests
{
    [AvaloniaFact]
    public async Task A_controls_context_menu_takes_the_IDEs_theme_and_the_canvas_keeps_its_own()
    {
        var app = Application.Current!;
        var before = app.RequestedThemeVariant;
        app.RequestedThemeVariant = ThemeVariant.Dark;
        try
        {
            using var designer = DesignerOverlayTreeTests.Shown();
            var canvas = designer.View.FindControl<Canvas>("NewControlSpawner")!;
            (await UiAutomationDriver.FinishAsync(UiAutomationDriver.Interact(canvas, "click", "20,20 to 100,60")))
                .Success.Should().BeTrue();
            DesignerOverlayTreeTests.Settle(designer.Window);
            var item = designer.View.GetVisualDescendants().OfType<ControlItem>().Single();
            var flyout = (PopupFlyoutBase)item.ContextFlyout!;

            flyout.ShowAt(item);
            DesignerOverlayTreeTests.Settle(designer.Window);
            try
            {
                flyout.IsOpen.Should().BeTrue();
                ((Control)flyout.Popup.Child!).ActualThemeVariant.Should().Be(ThemeVariant.Dark);
                item.ActualThemeVariant.Should().Be(ThemeVariant.Light, "the form's canvas keeps its own theme");
            }
            finally { flyout.Hide(); }
        }
        finally { app.RequestedThemeVariant = before; }
    }
}
