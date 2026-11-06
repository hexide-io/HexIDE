using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.AvaloniaInterop;

internal static class StatusBarMembers
{
    private static readonly HashSet<string> Shared = new(StringComparer.OrdinalIgnoreCase)
    {
        "Name", "Index", "Left", "Top", "Width", "Height", "Visible", "Tag", "ToolTipText", "TabIndex", "Container", "Parent",
        "Object", "Move", "ZOrder", "Drag", "ShowWhatsThis", "FontName", "FontSize", "FontBold", "FontItalic", "FontUnderline", "FontStrikethru",
    };
    public static bool? TryGet(Control control, string name, out Vb6Value value)
    {
        value = default;
        if (control is not VBStatusBar bar) return null;
        switch (name.ToLowerInvariant())
        {
            case "panels": value = new(bar.Panels); break;
            case "style": value = new(bar.Style); break;
            case "simpletext": value = new(bar.SimpleText); break;
            case "showtips": value = new(bar.ShowTips); break;
            case "enabled": value = new(bar.IsEnabled); break;
            case "mousepointer": value = new(bar.MousePointer); break;
            case "oledropmode": value = new(bar.OLEDropMode); break;
            case "align": value = new(bar.Align); break;
            case "tabstop": throw StatusBarPanels.Error(438);
            case "index": return null;
            default:
                Refuse(name);
                return Shared.Contains(name) ? null : false;
        }
        return true;
    }

    public static bool? TrySet(Control control, string name, Vb6Value value)
    {
        if (control is not VBStatusBar bar) return null;
        switch (name.ToLowerInvariant())
        {
            case "panels": throw StatusBarPanels.Error(383);
            case "style": bar.Style = StatusBarPanels.Choice(value, 1); break;
            case "simpletext": bar.SimpleText = StatusBarPanels.Text(value); break;
            case "showtips": bar.ShowTips = (bool)VB6BuiltIns.CBool(value).Value!; break;
            case "enabled": bar.IsEnabled = (bool)VB6BuiltIns.CBool(value).Value!; break;
            case "mousepointer" or "oledropmode":
                throw new NotImplementedException($"StatusBar {name} assignment has not been measured (#1087)");
            case "align":
                var align = StatusBarPanels.Choice(value, 2);
                bar.Align = (short)align;
                if (align == 1) Canvas.SetTop(bar, 0);
                else if (align == 2) PlaceAtBottom(bar);
                break;
            case "height":
                if (bar.Align == 2 && !double.IsFinite(bar.ContainerHeight))
                    throw new NotImplementedException("StatusBar bottom alignment needs a known container height (#1087)");
                AvaloniaInteroperability.TrySet(bar, VBProperties.HeightProperty, value);
                if (bar.Align == 2) PlaceAtBottom(bar);
                break;
            case "tabstop": throw StatusBarPanels.Error(438);
            default:
                Refuse(name);
                return Shared.Contains(name) ? null : false;
        }
        return true;
    }

    private static void PlaceAtBottom(VBStatusBar bar)
    {
        var height = bar.Parent is Control parent && parent.Bounds.Height > 0 ? parent.Bounds.Height : bar.ContainerHeight;
        if (!double.IsFinite(height))
            throw new NotImplementedException("StatusBar bottom alignment needs a known container height (#1087)");
        Canvas.SetTop(bar, height - bar.Height);
    }

    public static bool? TryCall(Control control, string name)
    {
        if (control is not VBStatusBar bar) return null;
        Refuse(name);
        if (!name.Equals("Refresh", StringComparison.OrdinalIgnoreCase)) return null;
        bar.InvalidateVisual();
        return true;
    }

    private static void Refuse(string name)
    {
        if (name.ToLowerInvariant() is "dragicon" or "dragmode" or "negotiate" or "whatsthishelpid"
            || StatusBarComponentClass.Instance.Member(name) is { Implemented: false })
            throw new NotImplementedException($"StatusBar {name} is not implemented (#1087)");
    }
}
