using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.AvaloniaInterop;

/// <summary>
/// A control's Left, Top, Width and Height as a running program sees them (#933): in its container's ScaleMode, as a
/// Single, held as a whole number of twips. HexIDE lays controls out in pixels, at 15 twips a pixel, so the conversion
/// happens here, at the property, which is the only place a program observes the unit.
/// </summary>
/// <remarks>
/// What VB6 does, measured with <c>vb6.exe /make</c>, native and p-code agreeing (the oracle's "A control's position at
/// run time"):
/// <list type="bullet">
/// <item>A .frm holds every position in twips, whatever the form's ScaleMode: a pixel form's <c>Left = 8</c> reads
/// 0.5333334. So the reader's divide-by-15 is right, and only the run time converts.</item>
/// <item>A program reads and writes them in the container's ScaleMode: the form's for a control on the form, a
/// PictureBox's for one inside it, and twips inside a Frame, which has no ScaleMode, even on a pixel form. A form's own
/// Width and Height are twips whatever its ScaleMode.</item>
/// <item>The value is a whole number of twips: <c>Left = 1.5</c> and <c>2.5</c> read 2 (half to even), and 10.5 pixels
/// is 158 twips. There is no snapping to whole pixels: a TextBox's <c>Left = 1201</c> reads 1201. A value past a Long's
/// range, in twips, is 6.</item>
/// <item>A Label is at least one pixel wide and high: <c>Width = 7</c> and <c>Width = 0</c> read 15.</item>
/// </list>
/// </remarks>
internal static class ControlPositions
{
    public static bool IsPosition(PropertyClass property) =>
        ReferenceEquals(property, VBProperties.LeftProperty) || ReferenceEquals(property, VBProperties.TopProperty)
        || IsSize(property);

    private static bool IsSize(PropertyClass property) =>
        ReferenceEquals(property, VBProperties.WidthProperty) || ReferenceEquals(property, VBProperties.HeightProperty);

    private static bool IsVertical(PropertyClass property) =>
        ReferenceEquals(property, VBProperties.TopProperty) || ReferenceEquals(property, VBProperties.HeightProperty);

    /// <summary>The value a program reads, from the control's position in pixels.</summary>
    public static Vb6Value Read(Control control, PropertyClass property, double pixels)
    {
        // A Timer's Left and Top belong only to its designer icon (measured, #2069).
        if (control is VBTimer && (ReferenceEquals(property, VBProperties.LeftProperty)
                                  || ReferenceEquals(property, VBProperties.TopProperty)))
            throw new VBRunTimeException(new VBStandardError(VBStandardError.PropertyGetCantBeExecutedAtRunTime.ErrNo,
                $"'{property.Name}' property cannot be read at run time"));
        // A form's own Width and Height are its outer size, its frame included (#1072).
        if (control is Window form && IsSize(property))
            return new Vb6Value((float)FormFrame.Read(form, IsVertical(property)));
        // A whole number of twips, which is what VB6 holds; the pixels came from twips, so this only drops the noise of
        // the division.
        var twips = Math.Round(pixels * VBScaleModeExtensions.PixelToTwips);
        return new Vb6Value((float)(twips / TwipsPerUnit(control, property)));
    }

    /// <summary>The position in pixels for a value a program assigns, after VB6's rounding to whole twips.</summary>
    public static double ToPixels(Control control, PropertyClass property, double value)
    {
        var twips = value * TwipsPerUnit(control, property);
        if (double.IsNaN(twips) || twips < int.MinValue - 0.5 || twips >= int.MaxValue + 0.5)
            throw new VBRunTimeException(VBStandardError.Overflow);
        twips = Math.Round(twips, MidpointRounding.ToEven);
        // A form's outer size: the window takes the client area that is left inside the frame, and keeps a menu bar above
        // it (#1072, #1127).
        if (control is Window form && IsSize(property))
            return FormFrame.ClientFor(form, IsVertical(property), twips) + (IsVertical(property) ? MenuBarHeight(form) : 0);
        if (control is VBLabel && IsSize(property) && twips < VBScaleModeExtensions.PixelToTwips)
            twips = VBScaleModeExtensions.PixelToTwips;
        return twips / VBScaleModeExtensions.PixelToTwips;
    }

    /// <summary>
    /// A mouse event's X or Y (#974): the whole pixel under the pointer, counted from the object's client origin, in
    /// twips, or in the object's own ScaleMode where it has one. Measured: a control's are twips even on a pixel form
    /// (a CommandButton at client (10, 5) is 150, 75 there), and a form's follow its ScaleMode (300, 10).
    /// </summary>
    public static float MouseCoordinate(double pixels, VBScaleMode mode, bool vertical) =>
        (float)(Math.Floor(pixels) * VBScaleModeExtensions.PixelToTwips / TwipsPerUnit(mode, vertical, vertical ? "Y" : "X"));

    /// <summary>
    /// A form's ScaleWidth or ScaleHeight (#1071): its client area in whole pixels, in the form's ScaleMode, as a Single.
    /// Measured: a 2520-twip client reads 2520 and, in pixels, 168. After a resize to 2000 twips of client height it reads
    /// 1995, since the client is whole pixels. On a form with a menu bar it is the area below the bar, which VB6 leaves
    /// out of the client area (#1127): a 3420-twip client under a bar reads 3420.
    /// </summary>
    public static Vb6Value ScaleSize(Window form, bool vertical)
    {
        var what = vertical ? "ScaleHeight" : "ScaleWidth";
        var canvas = form.GetLogicalDescendants().OfType<Canvas>().FirstOrDefault();
        var mode = canvas is null ? VBScaleMode.Twip : VBProps.GetScaleMode(canvas);
        return new Vb6Value((float)(ClientPixels(form, vertical) * VBScaleModeExtensions.PixelToTwips / TwipsPerUnit(mode, vertical, what)));
    }

    /// <summary>A form's client area across or down, in whole pixels: the window less the menu bar it holds.</summary>
    public static double ClientPixels(Window form, bool vertical)
    {
        var pixels = vertical ? form.Height : form.Width;
        if (double.IsNaN(pixels))
            pixels = vertical ? form.ClientSize.Height : form.ClientSize.Width;
        // The window holds the bar here, above the canvas, so the client area is what is left below it. The bar is
        // visible exactly when VBLoader.HasMenuBar made the window taller for it.
        if (vertical)
            pixels -= MenuBarHeight(form);
        return Math.Floor(Math.Max(0, pixels) + 1e-9);
    }

    private static double MenuBarHeight(Window form) =>
        form.GetLogicalDescendants().OfType<Menu>().FirstOrDefault(menu => menu.IsVisible) is { } bar ? bar.Height : 0;

    // How many twips one of the program's units is, for this property of this control.
    private static double TwipsPerUnit(Control control, PropertyClass property)
    {
        if (control is Window)
            return 1;
        var mode = control.Parent is Control host ? VBProps.GetScaleMode(host) : VBScaleMode.Twip;
        return TwipsPerUnit(mode, IsVertical(property), property.Name);
    }

    private static double TwipsPerUnit(VBScaleMode mode, bool vertical, string what)
    {
        switch (mode)
        {
            case VBScaleMode.Twip or VBScaleMode.Point or VBScaleMode.Pixel or VBScaleMode.Character or VBScaleMode.Inch:
                var (horizontal, down) = mode.TwipsPerUnit();
                return vertical ? down : horizontal;
            default:
                // User needs the container's ScaleLeft, ScaleTop, ScaleWidth and ScaleHeight, which HexIDE does not hold.
                // Centimeter and Millimeter read 0.2116669 and 2.116669 for 120 twips, where 1440 / 2.54 twips a
                // centimetre gives 0.2116667: measured, and not explained, so not guessed.
                throw new NotImplementedException($"{what} in a container whose ScaleMode is {mode} is not implemented (#990)");
        }
    }
}
