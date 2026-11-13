using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Platform;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using HexIDE.Forms.Views;
using HexIDE.Integration.Tests.Automation;
using HexIDE.Localization;

namespace HexIDE.Integration.Tests.Views;

public class ContextMenuMeasureTests
{
    [AvaloniaFact(Skip = "Needs Avalonia 12.1.5; pinned back to 12.1.4 for #3869")]
    public void CodeMenuFitsItsShortcutsAndSubmenuArrowsOnFirstOpening()
    {
        new LocalizationService().Apply("en");
        var view = new CodeEditorView();
        var window = NativePopupWindow(out var backing);
        window.Content = view;
        window.Width = 900;
        window.Height = 800;
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var editor = view.FindControl<TextEditor>("TextEditor")!;
            var menu = (MenuFlyout)editor.ContextFlyout!;
            double firstWidth = 0;
            for (var opening = 0; opening < 2; opening++)
            {
                menu.ShowAt(editor, true);
                Dispatcher.UIThread.RunJobs();
                using var frame = backing.CaptureRenderedFrame();
                frame.Should().NotBeNull();
                var presenter = (Control)menu.Popup.Child!;
                var root = (PopupRoot)TopLevel.GetTopLevel(presenter)!;
                root.ClientSize.Width.Should().BeGreaterThanOrEqualTo(presenter.DesiredSize.Width);
                if (opening == 0) firstWidth = presenter.Bounds.Width;
                else presenter.Bounds.Width.Should().BeApproximately(firstWidth, 0.1);
                foreach (var item in menu.Items.OfType<MenuItem>())
                {
                    foreach (var text in item.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)))
                    {
                        var position = text.TranslatePoint(default, item)!.Value;
                        (position.X + text.TextLayout.WidthIncludingTrailingWhitespace).Should().BeLessThanOrEqualTo(item.Bounds.Width + 0.1,
                            $"{item.Header}: {text.Text} must fit on opening {opening + 1}");
                    }
                    if (item.Items.Count > 0)
                    {
                        var arrow = item.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(p => p.IsEffectivelyVisible);
                        arrow.Bounds.Width.Should().BeGreaterThan(0);
                        var position = arrow.TranslatePoint(default, item)!.Value;
                        (position.X + arrow.Bounds.Width).Should().BeLessThanOrEqualTo(item.Bounds.Width + 0.1,
                            $"{item.Header}'s arrow must fit on opening {opening + 1}");
                    }
                }
                menu.Hide();
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally { window.Close(); }
    }

    // Headless normally uses overlay popups. Keep its real Skia drawing and input, but supply
    // a native popup whose positioner applies the requested size, as a desktop backend does.
    private static Window NativePopupWindow(out Window backing)
    {
        backing = new Window();
        var parent = backing.PlatformImpl!;
        var platform = PopupBoundsTests.ForwardingPlatform<IWindowImpl>.Wrap((method, args) =>
        {
            if (method.Name != "CreatePopup") return method.Invoke(parent, args);
            var inner = (IPopupImpl)new Window().PlatformImpl!;
            var positioner = Substitute.For<IPopupPositioner>();
            positioner.When(p => p.Update(Arg.Any<PopupPositionerParameters>()))
                .Do(call => typeof(IWindowImpl).GetMethod("Resize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                    .Invoke(inner, [call.Arg<PopupPositionerParameters>().Size, WindowResizeReason.Layout]));
            return PopupBoundsTests.ForwardingPlatform<IPopupImpl>.Wrap((popupMethod, popupArgs) =>
                popupMethod.Name == "get_PopupPositioner" ? positioner : popupMethod.Invoke(inner, popupArgs));
        });
        return new Window(platform);
    }
}
