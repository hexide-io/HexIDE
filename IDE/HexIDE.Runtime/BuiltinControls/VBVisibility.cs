using Avalonia;
using Avalonia.Controls;

namespace HexIDE.Runtime.BuiltinControls;

/// <summary>
/// VB6's <c>Visible</c>, which is not Avalonia's <c>IsVisible</c> once a control can contain others.
///
/// Avalonia never applies the template of a control that is not visible, so hiding a container unrealises
/// its entire subtree. Nothing inside it would then dispatch an event — not <c>Timer1_Timer</c>, not
/// <c>Text1_Change</c>, not <c>Command1_Click</c> — because event dispatch walks up the VISUAL tree to find
/// the module execution root, and an unrealised child has no visual parent to walk. In VB6 a control inside
/// a hidden Frame is merely not drawn; its code keeps running, and a timer inside one keeps firing.
///
/// So a container is hidden by making it invisible and untouchable rather than absent: opacity zero so it
/// paints nothing, hit-test off so it swallows no clicks, but still realised, laid out, and dispatching.
/// Everything else keeps using <c>IsVisible</c>, which is both cheaper and what removes a control from
/// layout — and for a leaf control there is no subtree to lose.
/// </summary>
internal static class VBVisibility
{
    public static void Set(Control control, bool visible)
    {
        if (control is not IVBContainerControl)
        {
            control.IsVisible = visible;
            return;
        }

        control.IsVisible = true;
        control.Opacity = visible ? 1 : 0;
        control.IsHitTestVisible = visible;
    }

    public static bool Get(Control control) =>
        control is IVBContainerControl
            ? control.IsVisible && control.Opacity > 0
            : control.IsVisible;
}
