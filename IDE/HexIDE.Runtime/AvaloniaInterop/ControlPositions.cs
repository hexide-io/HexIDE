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
        return new Vb6Value((float)(Origin(control, property) + twips / TwipsPerUnit(control, property)));
    }

    /// <summary>The position in pixels for a value a program assigns, after VB6's rounding to whole twips.</summary>
    public static double ToPixels(Control control, PropertyClass property, double value)
    {
        // Negative sizes measured for these targets in #4071; preserve other controls' validation.
        if (IsSize(property) && control is (VBCommandButton or VBLabel or Window) && value < 0)
            throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
        var twips = (value - Origin(control, property)) * TwipsPerUnit(control, property);
        if (double.IsNaN(twips) || twips < int.MinValue - 0.5 || twips >= int.MaxValue + 0.5)
            throw new VBRunTimeException(VBStandardError.Overflow);
        twips = Math.Round(twips, MidpointRounding.ToEven);
        // A form's outer size: the window takes the client area that is left inside the frame, and keeps a menu bar above
        // it (#1072, #1127).
        if (control is Window form && IsSize(property))
            return FormFrame.RememberWindowSize(form, IsVertical(property),
                FormFrame.ClientFor(form, IsVertical(property), twips) + (IsVertical(property) ? MenuBarHeight(form) : 0));
        if (control is VBFlatScrollBar && IsSize(property) && twips == 0)
            twips = 30;
        if (IsSize(property))
        {
            if (control is VBComboBox combo && IsVertical(property))
            {
                if (combo.IsSimpleCombo && SimpleComboHeights.Find(AttachedProperties.GetFont(combo) ?? VBFont.Default) is { } metrics)
                {
                    var pixels = twips / VBScaleModeExtensions.PixelToTwips;
                    if (pixels < metrics.Edit)
                        return combo.Height;
                    if (pixels >= metrics.Edit + 4 + metrics.Row)
                        return SimpleComboHeights.Snap(pixels, metrics);
                }
                // The interval from the edit box to one row, and other fonts/styles, remain unmeasured (#1655).
                throw new VBRunTimeException(VBStandardError.PropertySetCantBeUsedWithAReadOnlyProperty);
            }
            twips = ApplyMinimum(control, IsVertical(property), twips);
        }
        return twips / VBScaleModeExtensions.PixelToTwips;
    }

    // #4071, oracle PRs #4069/#4095: compare pixels rounded half up, but retain accepted whole twips.
    // Only CommandButton, TextBox and Label were measured for this rule; other controls keep their prior comparison.
    private static double ApplyMinimum(Control control, bool vertical, double twips)
    {
        var minimum = MinimumSize(control, vertical);
        if (control is VBCommandButton or VBTextBox or VBLabel)
            return Math.Floor(twips / VBScaleModeExtensions.PixelToTwips + 0.5)
                < minimum / VBScaleModeExtensions.PixelToTwips ? minimum : twips;
        return Math.Max(twips, minimum);
    }

    public static void EnforceOtherDimension(Control control, PropertyClass assigned)
    {
        if (!IsSize(assigned) || control is not (VBCommandButton or VBTextBox or VBLabel))
            return;
        var vertical = !IsVertical(assigned);
        var pixels = vertical ? control.Height : control.Width;
        if (double.IsNaN(pixels))
            return;
        var adjusted = ApplyMinimum(control, vertical, pixels * VBScaleModeExtensions.PixelToTwips)
            / VBScaleModeExtensions.PixelToTwips;
        if (adjusted == pixels)
            return;
        if (vertical) control.Height = adjusted;
        else control.Width = adjusted;
    }

    // #1029: measured floors in twips, not host font metrics. Other font/border combinations stay as they were.
    private static double MinimumSize(Control control, bool vertical)
    {
        var font = AttachedProperties.GetFont(control) ?? VBFont.Default;
        var plain = font.Weight == VBFont.NormalWeight && !font.Italic && !font.Underline && !font.Strikethrough
            && font.Charset == 0;
        var standard = plain && string.Equals(font.FontFamilyName, "MS Sans Serif", StringComparison.OrdinalIgnoreCase)
            && font.Size == 8.25;
        // #4071 / PR #4095: measured at 96 dpi only; font derivation and other dpi remain unmeasured.
        var large = plain && string.Equals(font.FontFamilyName, "MS Sans Serif", StringComparison.OrdinalIgnoreCase)
            && font.Size == 24;
        var arial = plain && string.Equals(font.FontFamilyName, "Arial", StringComparison.OrdinalIgnoreCase)
            && font.Size == 14.25;
        switch (control)
        {
            case VBTextBox box when standard && box.VbBorderStyle == 0:
                return vertical ? 195 : 75;
            case VBTextBox box when standard && box.VbBorderStyle == 1:
                return vertical ? 285 : 150;
            case VBTextBox box when arial && box.VbBorderStyle == 1:
                return vertical ? 450 : 240;
            case VBCommandButton when standard:
                return vertical ? 195 : 75;
            case VBCommandButton when large:
                return (vertical ? 37 : 16) * VBScaleModeExtensions.PixelToTwips;
            case VBCommandButton when arial:
                return vertical ? 330 : 120;
            case VBCheckBox or VBOptionButton:
                return vertical ? 195 : 75;
            case VBComboBox:
                return 390;
            case VBListBox:
                return vertical ? 255 : 135;
            case VBScrollBar:
                return 30;
            case VBFrame or VBPictureBox or VBImage or VBShape or VBLabel:
                return 15;
            default:
                return double.NegativeInfinity;
        }
    }

    /// <summary>
    /// A mouse event's X or Y (#974): the whole pixel under the pointer, counted from the object's client origin, in
    /// twips, or in the object's own ScaleMode where it has one. Measured: a control's are twips even on a pixel form
    /// (a CommandButton at client (10, 5) is 150, 75 there), and a form's follow its ScaleMode (300, 10).
    /// </summary>
    public static float MouseCoordinate(double pixels, VBScaleMode mode, bool vertical, Control? origin = null)
    {
        var scale = origin is null ? null : RuntimeScale.Of(origin);
        return (float)((scale?.Origin(vertical) ?? 0) + Math.Floor(pixels) * VBScaleModeExtensions.PixelToTwips
            / (scale?.TwipsPerUnit(vertical) ?? TwipsPerUnit(mode, vertical, vertical ? "Y" : "X")));
    }

    /// <summary>
    /// A form's ScaleWidth or ScaleHeight (#1071): its client area in whole pixels, in the form's ScaleMode, as a Single.
    /// Measured: a 2520-twip client reads 2520 and, in pixels, 168. After a resize to 2000 twips of client height it reads
    /// 1995, since the client is whole pixels. On a form with a menu bar it is the area below the bar, which VB6 leaves
    /// out of the client area (#1127): a 3420-twip client under a bar reads 3420.
    /// </summary>
    public static Vb6Value ScaleSize(Window form, bool vertical)
    {
        var what = vertical ? "ScaleHeight" : "ScaleWidth";
        if (RuntimeScale.TryGet(form, what, out var size))
            return size;
        var canvas = form.GetLogicalDescendants().OfType<Canvas>().FirstOrDefault();
        var mode = canvas is null ? VBScaleMode.Twip : VBProps.GetScaleMode(canvas);
        return new Vb6Value((float)(ClientPixels(form, vertical) * VBScaleModeExtensions.PixelToTwips / TwipsPerUnit(mode, vertical, what)));
    }

    /// <summary>A form's client area across or down, in whole pixels: the window less the menu bar it holds.</summary>
    public static double ClientPixels(Window form, bool vertical)
    {
        var pixels = FormFrame.WindowSize(form, vertical);
        if (double.IsNaN(pixels))
            pixels = vertical ? form.ClientSize.Height : form.ClientSize.Width;
        // The window holds the bar here, above the canvas, so the client area is what is left below it. The bar is
        // initially visible when VBLoader.HasMenuBar made the window taller; posted menu changes update it (#1140).
        if (vertical)
            pixels -= MenuBarHeight(form);
        return Math.Floor(Math.Max(0, pixels) + 1e-9);
    }

    private static double MenuBarHeight(Window form) =>
        form.GetLogicalDescendants().OfType<Menu>().FirstOrDefault(menu => menu.IsVisible) is { } bar ? bar.Height : 0;

    private static double Origin(Control control, PropertyClass property) =>
        control is not Window && !IsSize(property) && control.Parent is Control host && RuntimeScale.Of(host) is { } scale
            ? scale.Origin(IsVertical(property)) : 0;

    // How many twips one of the program's units is, for this property of this control.
    private static double TwipsPerUnit(Control control, PropertyClass property)
    {
        if (control is Window)
            return 1;
        var host = control.Parent as Control;
        if (host is not null && RuntimeScale.Of(host) is { } scale)
        {
            // #990 round 2: sizes are magnitudes even in a flipped user scale; positions keep its sign.
            var unit = scale.TwipsPerUnit(IsVertical(property));
            return IsSize(property) ? Math.Abs(unit) : unit;
        }
        return TwipsPerUnit(host is null ? VBScaleMode.Twip : VBProps.GetScaleMode(host), IsVertical(property), property.Name);
    }

    private static double TwipsPerUnit(VBScaleMode mode, bool vertical, string what)
    {
        switch (mode)
        {
            case VBScaleMode.Twip or VBScaleMode.Point or VBScaleMode.Pixel or VBScaleMode.Character or VBScaleMode.Inch
                or VBScaleMode.Centimiter or VBScaleMode.Millimiter:
                return RuntimeScale.Unit(mode, vertical);
            default:
                // A User scale outside the running form/PictureBox mapping still has no measured unit (#4693).
                throw new NotImplementedException($"{what} in a container whose ScaleMode is {mode} is not implemented (#4693)");
        }
    }
}
