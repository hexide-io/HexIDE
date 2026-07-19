using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Classic.CommonControls;

namespace HexIDE.Runtime.BuiltinTypes;

public static class VBColorAvaloniaExtensions
{
    public static VBColor FromAvaloniaColor(Color color)
    {
        return VBColor.FromColor(color.R, color.G, color.B);
    }
}

public static class VBColorExtensions
{
    public static IBrush ToBrush(this VBColor color)
    {
        if (color.Type == VBColor.ColorType.Raw)
            return new SolidColorBrush(new Color(0xFF, color.R, color.G, color.B));

        // FindResource returns AvaloniaProperty.UnsetValue (not null) when the key is missing in
        // Avalonia 12, so ?? does not help — use TryGetResource and an is-check instead.
        if (Application.Current!.TryGetResource(GetKey(color.SystemColor), null, out var resource)
            && resource is IBrush brush)
            return brush;
        return Brushes.DeepPink;
    }

    public static SystemResourceKey GetKey(this VbSystemColor color)
    {
        return color switch
        {
            VbSystemColor.Scrollbar => SystemColors.ScrollBarBrushKey,
            VbSystemColor.Desktop => SystemColors.DesktopBrushKey,
            VbSystemColor.Activecaption => SystemColors.ActiveCaptionBrushKey,
            VbSystemColor.Inactivecaption => SystemColors.InactiveCaptionBrushKey,
            VbSystemColor.Menu => SystemColors.MenuBrushKey,
            VbSystemColor.Window => SystemColors.WindowBrushKey,
            VbSystemColor.Windowframe => SystemColors.WindowFrameBrushKey,
            VbSystemColor.Menutext => SystemColors.MenuTextBrushKey,
            VbSystemColor.Windowtext => SystemColors.WindowTextBrushKey,
            VbSystemColor.Captiontext => SystemColors.ActiveCaptionTextBrushKey,
            VbSystemColor.Activeborder => SystemColors.ActiveBorderBrushKey,
            VbSystemColor.Inactiveborder => SystemColors.InactiveBorderBrushKey,
            VbSystemColor.Appworkspace => SystemColors.AppWorkspaceBrushKey,
            VbSystemColor.Highlight => SystemColors.HighlightBrushKey,
            VbSystemColor.Highlighttext => SystemColors.HighlightTextBrushKey,
            VbSystemColor.Btnface => SystemColors.ControlBrushKey,
            VbSystemColor.Btnshadow => SystemColors.ControlDarkBrushKey,
            VbSystemColor.Graytext => SystemColors.GrayTextBrushKey,
            VbSystemColor.Btntext => SystemColors.ControlTextBrushKey,
            VbSystemColor.Inactivecaptiontext => SystemColors.InactiveCaptionTextBrushKey,
            VbSystemColor.Btnhighlight => SystemColors.ControlLightBrushKey,
            VbSystemColor._3DDKSHADOW => SystemColors.ControlDarkDarkBrushKey,
            VbSystemColor._3DLIGHT => SystemColors.ControlLightLightBrushKey,
            VbSystemColor.Infotext => SystemColors.InfoTextBrushKey,
            VbSystemColor.Infobk => SystemColors.InfoBrushKey,
            _ => throw new ArgumentOutOfRangeException(nameof(color), color, null)
        };
    }

    public static string GetDescription(this VbSystemColor color)
    {
        return color switch
        {
            VbSystemColor.Scrollbar => "Scroll Bars",
            VbSystemColor.Desktop => "Desktop",
            VbSystemColor.Activecaption => "Active Title Bar",
            VbSystemColor.Inactivecaption => "Inactive Title Bar",
            VbSystemColor.Menu => "Menu Bar",
            VbSystemColor.Window => "Window Background",
            VbSystemColor.Windowframe => "Window Frame",
            VbSystemColor.Menutext => "Menu Text",
            VbSystemColor.Windowtext => "Window Text",
            VbSystemColor.Captiontext => "Active Title Bar Text",
            VbSystemColor.Activeborder => "Active Border",
            VbSystemColor.Inactiveborder => "Inactive Border",
            VbSystemColor.Appworkspace => "Application Workspace",
            VbSystemColor.Highlight => "Highlight",
            VbSystemColor.Highlighttext => "Highlight Text",
            VbSystemColor.Btnface => "Button Face",
            VbSystemColor.Btnshadow => "Button Shadow",
            VbSystemColor.Graytext => "Disabled Text",
            VbSystemColor.Btntext => "Button Text",
            VbSystemColor.Inactivecaptiontext => "Inactive Title Bar Text",
            VbSystemColor.Btnhighlight => "Button Highlight",
            VbSystemColor._3DDKSHADOW => "Button Dark Shadow",
            VbSystemColor._3DLIGHT => "Button Light Shadow",
            VbSystemColor.Infotext => "ToolTip Text",
            VbSystemColor.Infobk => "ToolTip",
            _ => throw new ArgumentOutOfRangeException(nameof(color), color, null)
        };
    }
}
