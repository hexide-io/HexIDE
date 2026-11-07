using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;

namespace HexIDE.Runtime.BuiltinControls;

/// <summary>
/// The arrow keys on an option button, a check box or a command button move the focus to the previous or next control in
/// its own container (#1625).
/// </summary>
/// <remarks>
/// Measured on vb6.exe's built EXEs, native and p-code agreeing (the oracle's "Tab and the arrow keys among option buttons"
/// and "Where the arrow keys go from a button"):
/// <list type="bullet">
/// <item>Up and Left go to the previous control, Down and Right to the next, by TabIndex, not by the order the controls are
/// in, and wrapping within the container.</item>
/// <item>The control need not be a button, and its TabStop does not matter: Down reached a TextBox whose TabStop is False.
/// A control that cannot take the focus is passed over: a disabled one, a Label, and a Frame, without going into it.</item>
/// <item>An option button it lands on is selected, as the focus selects one (#1583). A check box is not changed.</item>
/// </list>
/// A TextBox, a PictureBox and the lists keep the arrow keys. UpDown uses only the measured Up-key path (#1093).
/// </remarks>
internal static class ArrowNavigation
{
    public static void Attach<T>(bool upOnly = false) where T : Control =>
        InputElement.KeyDownEvent.AddClassHandler<T>((control, e) =>
        {
            // With a modifier held, vb6.exe was not measured, so nothing is done there.
            if (e.Handled || !ReferenceEquals(e.Source, control) || e.KeyModifiers != KeyModifiers.None)
                return;
            var step = e.Key switch
            {
                Key.Up or Key.Left => -1,
                Key.Down or Key.Right => 1,
                _ => 0,
            };
            if (upOnly && e.Key != Key.Up)
                return;
            if (step == 0 || Next(control, step) is not { } next)
                return;
            e.Handled = true;
            next.Focus(NavigationMethod.Directional);
        });

    // The previous or next control in the container that can take the focus, by TabIndex, wrapping; null where there is no
    // other. Sorting is stable, so two controls with one TabIndex keep the order they are in.
    private static InputElement? Next(Control from, int step)
    {
        if (from.Parent is not Panel container)
            return null;
        var order = container.Children.OfType<InputElement>()
            .Where(c => ReferenceEquals(c, from) || c is { Focusable: true, IsEffectivelyEnabled: true, IsEffectivelyVisible: true })
            .OrderBy(c => KeyboardNavigation.GetTabIndex(c))
            .ToList();
        var at = order.IndexOf(from);
        return at < 0 || order.Count < 2 ? null : order[(at + step + order.Count) % order.Count];
    }
}
