using System;
using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.ProjectElements;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components;

/// <summary>
/// VB6's Line (#163): two endpoints, X1, Y1, X2 and Y2, in the container's scale, and no Left, Top, Width or Height. It
/// raises no events. Measured on vb6.exe: the oracle's "The Line control".
/// </summary>
public class LineComponentClass : ComponentBaseClass
{
    static LineComponentClass()
    {
        // Window Text, &H80000008 (measured: a Line with no BorderColor in its .frm reads it).
        BorderColorProperty.OverrideDefault<LineComponentClass>(VBColor.FromSystemColor(VbSystemColor.Windowtext));
    }

    public LineComponentClass() : base([BorderColorProperty, ShapeBorderStyleProperty, BorderWidthProperty, DrawModeProperty,
        X1Property, X2Property, Y1Property, Y2Property], events: [], hasBounds: false)
    {
    }

    public override string Name => "Line";
    public override string VBTypeName => "VB.Line";

    protected override Control InstantiateInternal(ComponentInstance instance) =>
        new VBLine(instance.GetPropertyOrDefault(X1Property), instance.GetPropertyOrDefault(Y1Property),
            instance.GetPropertyOrDefault(X2Property), instance.GetPropertyOrDefault(Y2Property))
        {
            BorderColor = instance.GetPropertyOrDefault(BorderColorProperty),
            BorderStyle = instance.GetPropertyOrDefault(ShapeBorderStyleProperty),
            BorderWidth = (int)instance.GetPropertyOrDefault(BorderWidthProperty),
            DrawMode = instance.GetPropertyOrDefault(DrawModeProperty),
        };

    public static ComponentBaseClass Instance { get; } = new LineComponentClass();

    /// <summary>
    /// How many twips one unit of a line's container is, across and down: the scale its X1, Y1, X2 and Y2 are in.
    /// </summary>
    /// <remarks>
    /// The form's ScaleMode on the form, a PictureBox's on one, and twips in a Frame, which has none, as a control's Left
    /// is read (#933). A user scale is the container's client size over its ScaleWidth and ScaleHeight, in Single
    /// precision, as VB6 holds a scale: the About Dialog's 5730-twip client over a ScaleWidth of 5380.766 makes its X1
    /// of 84.515 90 twips, and 90 twips reads back 84.51466 and 2445 reads 1687.583 (measured). Worked in Double, the
    /// same quotients read 84.51465 and 1687.582. HexIDE holds a user scale only for a form. Centimetres and
    /// millimetres refuse, as positions do, because VB6's measured values for them are unexplained.
    /// </remarks>
    public static (double X, double Y) TwipsPerUnit(ComponentInstance line, FormDefinition form)
    {
        var container = line.Container;
        if (container is null || container.BaseClass is FormComponentClass)
        {
            var mode = (VBScaleMode)(form.Scale?.Mode ?? (int)VBScaleMode.Twip);
            if (mode != VBScaleMode.User)
                return Known(mode);
            var root = VBLoader.FormComponentOf(form);
            if (form.Scale is not { Width: { } width, Height: { } height } || width == 0 || height == 0)
                throw new NotImplementedException("A Line on a form with a user scale but no ScaleWidth or ScaleHeight is not implemented (#163)");
            return ((float)(root.GetPropertyOrDefault(WidthProperty) * VBScaleModeExtensions.PixelToTwips) / (float)width,
                (float)(root.GetPropertyOrDefault(HeightProperty) * VBScaleModeExtensions.PixelToTwips) / (float)height);
        }
        if (container.BaseClass is PictureBoxComponentClass)
        {
            var mode = VBLoader.DeclaredScaleMode(container) ?? VBScaleMode.Twip;
            if (mode == VBScaleMode.User)
                throw new NotImplementedException("A Line in a PictureBox whose ScaleMode is User is not implemented (#990)");
            return Known(mode);
        }
        return (1, 1);

        static (double X, double Y) Known(VBScaleMode mode) => mode switch
        {
            VBScaleMode.Twip or VBScaleMode.Point or VBScaleMode.Pixel or VBScaleMode.Character or VBScaleMode.Inch =>
                mode.TwipsPerUnit(),
            _ => throw new NotImplementedException($"A Line in a container whose ScaleMode is {mode} is not implemented (#990)"),
        };
    }
}
