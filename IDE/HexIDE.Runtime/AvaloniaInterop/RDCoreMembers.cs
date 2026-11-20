using System;
using Avalonia;
using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.AvaloniaInterop;

/// <summary>
/// What RDCore adds to VB6's controls, for a run that takes RDCore's additions (#4967): an Image's, Label's or
/// PictureBox's <c>Opacity</c> (#4982). A VB6 or VBA project's controls have no such member, and using one there is
/// the error it always was.
/// </summary>
/// <remarks>
/// <para>
/// The supported cases were measured under RDC, RDCore's runner, with a probe project of the rdcore session's own
/// (2026-11-19, on #4967 and #4982). None of it was taken from RDCore's source.
/// </para>
/// <list type="bullet">
/// <item>Opacity is a Double. It reads 1 on a new Image, Label or PictureBox. For an Image,
/// <c>Controls.Add</c> also defaults to 1, and loading a picture leaves it alone.</item>
/// <item>It takes 0 to 1 as given: 0.333333333333 is kept as it is, and "0.25" is coerced. A value outside that range,
/// such as -0.1, 1.5, 2 or 1.0000001, is 380, "Invalid property value", and the value before it stays.</item>
/// <item>A form file's <c>Opacity = 0.5</c> in an Image's, Label's or PictureBox's block is read. A value there outside 0 to 1, such as 2 or
/// -0.5, is ignored with no error, and the control keeps 1.</item>
/// </list>
/// <para>
/// How it draws was measured in a shown RDC window on Linux. The control is blended over what is behind it with Opacity
/// as its alpha, so a (220, 40, 40) picture on a white form draws (238, 148, 148) at 0.5. The Image's own Avalonia
/// opacity draws exactly that. A picture with transparency of its own, and an Image over another control, are
/// unmeasured. A Label's and PictureBox's opaque BackColor blends the same way (measured, #4982). A PictureBox's
/// Visible keeps its Opacity and showing it restores that alpha, so its stored value is separate from visibility.
/// Transparent Labels, children inside a translucent PictureBox, and Windows/macOS drawing remain unmeasured;
/// they retain Avalonia's drawing, reasoned rather than checked under RDC.
/// </para>
/// </remarks>
internal static class RDCoreMembers
{
    private const string Opacity = "Opacity";

    private static readonly AttachedProperty<double> PictureBoxOpacityProperty =
        AvaloniaProperty.RegisterAttached<VBProps, Control, double>("RDCoreOpacity", 1);

    internal static double PictureBoxOpacity(Control control) => control.GetValue(PictureBoxOpacityProperty);

    private static void SetOpacity(Control control, double opacity)
    {
        if (control is VBPictureBox)
        {
            control.SetValue(PictureBoxOpacityProperty, opacity);
            VBVisibility.RefreshOpacity(control);
        }
        else
            control.Opacity = opacity;
    }

    internal static bool TryGet(Control control, string member, out Vb6Value value)
    {
        value = default;
        if (!Applies(control, member))
            return false;
        value = new Vb6Value(control is VBPictureBox ? PictureBoxOpacity(control) : control.Opacity);
        return true;
    }

    internal static bool TrySet(Control control, string member, Vb6Value value)
    {
        if (!Applies(control, member))
            return false;
        SetOpacity(control, InRange(Convert.ToDouble(VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Double, null).Value)));
        return true;
    }

    /// <summary>An Image's, Label's or PictureBox's <c>Opacity</c> line from its form file, as it is placed or cloned by <c>Load</c>.</summary>
    internal static void ApplyFormFile(Control control, ComponentInstance component, bool rdcoreAdditions)
    {
        // RDC reads 0.5 from a form file, and ignores 2 or -0.5 there with no error, keeping 1 (measured). Code setting the
        // same values is 380.
        if (!rdcoreAdditions || control is not (VBImage or VBLabel or VBPictureBox)
            || VBLoader.RawDouble(component, Opacity) is not { } opacity || opacity is not (>= 0 and <= 1))
            return;
        SetOpacity(control, opacity);
    }

    private static bool Applies(Control control, string member) =>
        control is (VBImage or VBLabel or VBPictureBox) && VBProps.GetRDCoreAdditions(control)
            && member.Equals(Opacity, StringComparison.OrdinalIgnoreCase);

    private static double InRange(double opacity) =>
        opacity is >= 0 and <= 1 ? opacity : throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
}
