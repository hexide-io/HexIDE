using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Converters;

public class VBColorDescriptionConverter : IValueConverter
{
    public static VBColorDescriptionConverter Instance { get; } = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is VBColor vbColor)
        {
            if (vbColor.Type == VBColor.ColorType.SystemColor)
                return GetSystemColorName(vbColor.SystemColor);
            return vbColor.ToString();
        }

        return null;
    }

    private static string GetSystemColorName(VbSystemColor color)
    {
        var (key, english) = color switch
        {
            VbSystemColor.Scrollbar => ("Str.SystemColor.Scrollbar", "Scroll Bars"),
            VbSystemColor.Desktop => ("Str.SystemColor.Desktop", "Desktop"),
            VbSystemColor.Activecaption => ("Str.SystemColor.Activecaption", "Active Title Bar"),
            VbSystemColor.Inactivecaption => ("Str.SystemColor.Inactivecaption", "Inactive Title Bar"),
            VbSystemColor.Menu => ("Str.SystemColor.Menu", "Menu Bar"),
            VbSystemColor.Window => ("Str.SystemColor.Window", "Window Background"),
            VbSystemColor.Windowframe => ("Str.SystemColor.Windowframe", "Window Frame"),
            VbSystemColor.Menutext => ("Str.SystemColor.Menutext", "Menu Text"),
            VbSystemColor.Windowtext => ("Str.SystemColor.Windowtext", "Window Text"),
            VbSystemColor.Captiontext => ("Str.SystemColor.Captiontext", "Active Title Bar Text"),
            VbSystemColor.Activeborder => ("Str.SystemColor.Activeborder", "Active Border"),
            VbSystemColor.Inactiveborder => ("Str.SystemColor.Inactiveborder", "Inactive Border"),
            VbSystemColor.Appworkspace => ("Str.SystemColor.Appworkspace", "Application Workspace"),
            VbSystemColor.Highlight => ("Str.SystemColor.Highlight", "Highlight"),
            VbSystemColor.Highlighttext => ("Str.SystemColor.Highlighttext", "Highlight Text"),
            VbSystemColor.Btnface => ("Str.SystemColor.Btnface", "Button Face"),
            VbSystemColor.Btnshadow => ("Str.SystemColor.Btnshadow", "Button Shadow"),
            VbSystemColor.Graytext => ("Str.SystemColor.Graytext", "Disabled Text"),
            VbSystemColor.Btntext => ("Str.SystemColor.Btntext", "Button Text"),
            VbSystemColor.Inactivecaptiontext => ("Str.SystemColor.Inactivecaptiontext", "Inactive Title Bar Text"),
            VbSystemColor.Btnhighlight => ("Str.SystemColor.Btnhighlight", "Button Highlight"),
            VbSystemColor._3DDKSHADOW => ("Str.SystemColor.ThreeDDarkShadow", "Button Dark Shadow"),
            VbSystemColor._3DLIGHT => ("Str.SystemColor.ThreeDLight", "Button Light Shadow"),
            VbSystemColor.Infotext => ("Str.SystemColor.Infotext", "ToolTip Text"),
            VbSystemColor.Infobk => ("Str.SystemColor.Infobk", "ToolTip"),
            _ => throw new ArgumentOutOfRangeException(nameof(color), color, null)
        };

        return Application.Current?.TryGetResource(key, null, out var resource) == true
            && resource is string name ? name : english;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
