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
        if (color.Type != VBColor.ColorType.SystemColor)
            return new SolidColorBrush(new Color(0xFF, color.R, color.G, color.B));

        // FindResource returns AvaloniaProperty.UnsetValue (not null) when the key is missing in
        // Avalonia 12, so ?? does not help — use TryGetResource and an is-check instead.
        if (Application.Current!.TryGetResource(GetKey(color.SystemColor), null, out var resource)
            && resource is IBrush brush)
            return brush;

        // Pink, so a missing colour shows. Logged once per colour, so it can be traced to the key nothing defines; seven
        // went unnoticed until someone saw the pink (#908).
        lock (UnresolvedSystemColors)
        {
            if (UnresolvedSystemColors.Add(color.SystemColor))
                Serilog.Log.Warning("No colour is defined for the system colour {SystemColor} ({Key}); drawing it DeepPink",
                    color.SystemColor, GetKey(color.SystemColor));
        }
        return Brushes.DeepPink;
    }

    private static readonly System.Collections.Generic.HashSet<VbSystemColor> UnresolvedSystemColors = new();

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
}
