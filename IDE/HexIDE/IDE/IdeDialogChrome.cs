using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace HexIDE.IDE;

internal static class IdeDialogChrome
{
    internal static void Apply(Window window)
    {
        window.CanMinimize = false;
        window.ShowInTaskbar = false;
        window.Classes.Add("ideDialog");
        window.Loaded += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (!window.IsVisible)
                return;
            var controls = window.GetVisualDescendants().OfType<Control>()
                .Where(control => control.IsEffectivelyVisible && control.IsEffectivelyEnabled).ToArray();
            var target = controls.FirstOrDefault(control => control.Classes.Contains("dialogInitialFocus"));
            if (target is null && controls.Any(control => control.IsFocused))
                return;
            target ??= controls.OfType<Button>().FirstOrDefault(button => button.IsDefault)
                ?? controls.OfType<TextBox>().FirstOrDefault(box => !box.IsReadOnly && box.Focusable && box.IsTabStop)
                ?? controls.FirstOrDefault(control => control.Focusable && control.IsTabStop);
            target?.Focus(NavigationMethod.Tab);
        });
    }
}
