using System;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.AvaloniaInterop;

/// <summary>
/// A control's Left, Top, Width and Height as the Properties window shows them at design time (#1636), from the model's
/// pixels, and back.
/// </summary>
/// <remarks>
/// <para>Measured on VB6's Properties window, in the IDE on the oracle guest's console (the oracle's "What VB6's Properties
/// window shows a position in"):</para>
/// <list type="bullet">
/// <item>A control's are in its container's ScaleMode: the form's on the form, a PictureBox's inside it, and twips inside a
/// Frame, which has no ScaleMode, even on a pixel form. That is the rule a running program reads them by
/// (<see cref="ControlPositions"/>, #933).</item>
/// <item>A User scale maps the container's client area onto ScaleLeft, ScaleTop, ScaleWidth and ScaleHeight: a PictureBox's
/// client area is inside its border. Centimetres and millimetres are 1440 / 2.54 and 1440 / 25.4 twips to the unit.</item>
/// <item>A value that is not whole shows three decimals at most: 356.41, 147.087.</item>
/// <item>A form's own Width and Height are twips, its client size plus the frame <see cref="FormFrame"/> gives a running
/// form, and its Left and Top are the client's less that frame's left and top edges. A UserControl's and a PropertyPage's
/// have no frame.</item>
/// </list>
/// <para>A value typed in is rounded to whole twips, half to even, as an assignment at run time is.</para>
/// </remarks>
public static class DesignPositions
{
    private const double PixelTwips = VBScaleModeExtensions.PixelToTwips;

    public static bool IsPosition(PropertyClass property) => ControlPositions.IsPosition(property);

    /// <summary>The value the Properties window shows for <paramref name="property"/>, from the model's pixels.</summary>
    public static double Shown(FormDefinition form, ComponentInstance component, PropertyClass property, double pixels)
    {
        var twips = pixels * PixelTwips;
        var vertical = IsVertical(property);
        if (component.BaseClass is FormComponentClass)
            return Decimals(twips + RootOffset(form, component, property));
        var scale = ScaleOf(form, component.Container);
        var value = IsSize(property)
            ? twips / scale.TwipsPerUnit(vertical)
            : scale.Origin(vertical) + twips / scale.TwipsPerUnit(vertical);
        return Decimals(value);
    }

    /// <summary>The model's pixels for <paramref name="value"/> typed into the Properties window.</summary>
    public static double ToPixels(FormDefinition form, ComponentInstance component, PropertyClass property, double value)
    {
        var vertical = IsVertical(property);
        double twips;
        if (component.BaseClass is FormComponentClass)
            twips = value - RootOffset(form, component, property);
        else
        {
            var scale = ScaleOf(form, component.Container);
            twips = (IsSize(property) ? value : value - scale.Origin(vertical)) * scale.TwipsPerUnit(vertical);
        }
        if (double.IsNaN(twips) || double.IsInfinity(twips))
            throw new ArgumentOutOfRangeException(nameof(value));
        twips = Math.Round(twips, MidpointRounding.ToEven);
        if (IsSize(property))
            twips = Math.Max(0, twips);
        return twips / PixelTwips;
    }

    /// <summary>The unit the Properties window shows <paramref name="property"/> in, as a reply names it.</summary>
    public static string Unit(FormDefinition form, ComponentInstance component, PropertyClass property) =>
        component.BaseClass is FormComponentClass ? "twips" : ScaleOf(form, component.Container).Unit;

    private static bool IsSize(PropertyClass property) =>
        ReferenceEquals(property, VBProperties.WidthProperty) || ReferenceEquals(property, VBProperties.HeightProperty);

    private static bool IsVertical(PropertyClass property) =>
        ReferenceEquals(property, VBProperties.TopProperty) || ReferenceEquals(property, VBProperties.HeightProperty);

    // Three decimals at most, as VB6's window shows a Single. The model's pixels came from whole twips, so this also drops
    // the noise of dividing by fifteen.
    private static double Decimals(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);

    // What the Properties window adds to the root's client rectangle, in twips: the frame round a form, measured equal to
    // a running form's (#1072), and nothing round a UserControl or PropertyPage, which have none.
    private static double RootOffset(FormDefinition form, ComponentInstance root, PropertyClass property)
    {
        if (form.RootVBTypeName is "VB.UserControl" or "VB.PropertyPage")
            return 0;
        var menu = VBLoader.HasMenuBar(form);
        var chrome = FormChrome.Read(root, menu);
        var (across, down) = FormFrame.Of(chrome.BorderStyle, chrome.CaptionBar, menu);
        if (ReferenceEquals(property, VBProperties.WidthProperty))
            return across;
        if (ReferenceEquals(property, VBProperties.HeightProperty))
            return down;
        // The frame's left edge is half the border across, and its top the rest of the frame down.
        return ReferenceEquals(property, VBProperties.LeftProperty) ? -across / 2 : -(down - across / 2);
    }

    // The scale a control's position is in: its container's, or twips inside a Frame.
    private static Scale ScaleOf(FormDefinition form, ComponentInstance? container)
    {
        if (container is null || container.BaseClass is FormComponentClass)
        {
            var root = container ?? VBLoader.FormComponentOf(form);
            return Scale.Of(form.Scale?.Mode ?? (int)VBScaleMode.Twip,
                VBLoader.RawDouble(root, "ScaleLeft") ?? 0, VBLoader.RawDouble(root, "ScaleTop") ?? 0,
                form.Scale?.Width, form.Scale?.Height,
                root.GetPropertyOrDefault(VBProperties.WidthProperty) * PixelTwips,
                root.GetPropertyOrDefault(VBProperties.HeightProperty) * PixelTwips);
        }
        if (container.BaseClass is PictureBoxComponentClass picture)
        {
            var inset = picture.ClientInset(container);
            return Scale.Of(VBLoader.RawNumber(container, "ScaleMode") ?? (int)VBScaleMode.Twip,
                VBLoader.RawDouble(container, "ScaleLeft") ?? 0, VBLoader.RawDouble(container, "ScaleTop") ?? 0,
                VBLoader.RawDouble(container, "ScaleWidth"), VBLoader.RawDouble(container, "ScaleHeight"),
                (container.GetPropertyOrDefault(VBProperties.WidthProperty) - inset.Left - inset.Right) * PixelTwips,
                (container.GetPropertyOrDefault(VBProperties.HeightProperty) - inset.Top - inset.Bottom) * PixelTwips);
        }
        return Scale.Twips;
    }

    private readonly record struct Scale(string Unit, double AcrossTwips, double DownTwips, double Left, double Top)
    {
        public static readonly Scale Twips = new("twips", 1, 1, 0, 0);

        public double TwipsPerUnit(bool vertical) => vertical ? DownTwips : AcrossTwips;

        public double Origin(bool vertical) => vertical ? Top : Left;

        // A User scale with no ScaleWidth or ScaleHeight to map, or a ScaleMode VB6 doesn't have, is shown in twips: there
        // is nothing to divide by.
        public static Scale Of(int mode, double left, double top, double? width, double? height, double clientWidth,
            double clientHeight)
        {
            switch ((VBScaleMode)mode)
            {
                case VBScaleMode.User when width is { } w and not 0 && height is { } h and not 0:
                    return new Scale("user units", clientWidth / w, clientHeight / h, left, top);
                case VBScaleMode.Twip:
                    return Twips;
                case VBScaleMode.Point or VBScaleMode.Pixel or VBScaleMode.Character or VBScaleMode.Inch
                    or VBScaleMode.Millimiter or VBScaleMode.Centimiter:
                    var (across, down) = ((VBScaleMode)mode).TwipsPerUnit();
                    return new Scale(UnitName((VBScaleMode)mode), across, down, 0, 0);
                default:
                    return Twips;
            }
        }

        private static string UnitName(VBScaleMode mode) => mode switch
        {
            VBScaleMode.Point => "points",
            VBScaleMode.Pixel => "pixels",
            VBScaleMode.Character => "characters",
            VBScaleMode.Inch => "inches",
            VBScaleMode.Millimiter => "millimetres",
            _ => "centimetres",
        };
    }
}
