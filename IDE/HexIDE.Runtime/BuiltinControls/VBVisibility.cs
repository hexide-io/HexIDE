using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using HexIDE.Runtime.AvaloniaInterop;

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
    // A PictureBox's RDCore Opacity is independent of Visible (#4982); other containers still show at alpha 1.
    private static readonly AttachedProperty<bool> HiddenProperty =
        AvaloniaProperty.RegisterAttached<VBProps, Control, bool>("Hidden");

    internal static void RefreshOpacity(Control control) =>
        control.Opacity = control.GetValue(HiddenProperty) ? 0
            : control is VBPictureBox ? RDCoreMembers.PictureBoxOpacity(control) : 1;

    public static void Set(Control control, bool visible)
    {
        // A form's window: itself on the desktop, the host's window holding its root in single view (#4068).
        if (FormRoot.View(control) is { } view)
        {
            if (view.Context.Program is { } program)
            {
                if (visible) program.Show(view);
                else program.Hide(view, reportModalError: false);
            }
            else if (visible) view.ShowForm();
            else view.HideForm();
            return;
        }
        var changed = Get(control) != visible;
        if (changed && RuntimePaint.Form(control) is { IsShown: true } parent)
        {
            if (!visible && parent.Context.Window is { } window)
                RuntimePaint.Post(window);
            else if (control is VBPictureBox)
                RuntimePaint.Post(control);
        }
        if (control is not IVBContainerControl)
        {
            control.IsVisible = visible;
            return;
        }

        control.IsVisible = true;
        control.SetValue(HiddenProperty, !visible);
        RefreshOpacity(control);
        control.IsHitTestVisible = visible;
    }

    public static bool Get(Control control)
    {
        // Label, Image, Shape, Line and menus keep their own value; every other control, a windowless UserControl included,
        // reads False while its form isn't shown (#4829, oracle page visible-before-a-form-is-shown.md). A top-level menu's
        // own value is what decides the menu bar (#1140).
        if (control is not (VBLine or VBLabel or VBImage or VBShape or VBMenu or VBMenuSeparator) && FormRoot.View(control) is null
            && control.GetLogicalAncestors().OfType<IFormView>().FirstOrDefault() is { IsShown: false })
            return false;

        return control is IVBContainerControl
            ? control.IsVisible && !control.GetValue(HiddenProperty)
            : control.IsVisible;
    }
}
