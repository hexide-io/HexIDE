using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.AvaloniaInterop;

internal static class SSTabMembers
{
    public static bool IsIndexed(string name) => name.ToLowerInvariant() is "tabcaption" or "tabenabled" or "tabvisible" or "tabpicture";

    public static Vb6Value ReadIndexed(VBSSTab tab, string name, IReadOnlyList<Vb6Value> args)
    {
        var index = Index(tab, args);
        return name.ToLowerInvariant() switch
        {
            "tabcaption" => new Vb6Value(tab.Caption(index)),
            "tabenabled" => new Vb6Value(tab.TabEnabled(index)),
            "tabvisible" => new Vb6Value(tab.TabVisible(index)),
            _ => throw new NotImplementedException("SSTab TabPicture is not implemented"),
        };
    }

    public static void WriteIndexed(VBSSTab tab, string name, IReadOnlyList<Vb6Value> args, Vb6Value value)
    {
        var index = Index(tab, args);
        switch (name.ToLowerInvariant())
        {
            case "tabcaption": tab.Caption(index, Text(value)); break;
            case "tabenabled": tab.TabEnabled(index, Boolean(value)); break;
            case "tabvisible": tab.TabVisible(index, Boolean(value)); break;
            default: throw new NotImplementedException("SSTab TabPicture is not implemented");
        }
    }

    public static bool TryGet(Control control, string name, out Vb6Value value)
    {
        value = default;
        if (control is not VBSSTab tab)
            return false;
        RefuseUnimplemented(name);
        switch (name.ToLowerInvariant().Trim('[', ']'))
        {
            case "_tab": value = new Vb6Value((short)0); break;
            case "tab": value = new Vb6Value((short)tab.Tab); break;
            case "tabs": value = new Vb6Value((short)tab.Tabs); break;
            case "tabsperrow": value = new Vb6Value((short)tab.TabsPerRow); break;
            case "rows": value = new Vb6Value((short)tab.Rows); break;
            case "caption": value = new Vb6Value(tab.Caption(tab.Tab)); break;
            case "style": value = new Vb6Value((long)tab.VbStyle); break;
            case "taborientation": value = new Vb6Value((long)tab.TabOrientation); break;
            case "tabheight": CheckDimensionScale(tab); value = ControlPositions.Read(tab, VBProperties.HeightProperty, tab.TabHeight); break;
            case "tabmaxwidth": CheckDimensionScale(tab); value = ControlPositions.Read(tab, VBProperties.WidthProperty, tab.TabMaxWidth); break;
            case "wordwrap": value = new Vb6Value(tab.WordWrap); break;
            case "showfocusrect": value = new Vb6Value(tab.ShowFocusRect); break;
            default: return false;
        }
        return true;
    }

    public static bool TrySet(Control control, string name, Vb6Value value)
    {
        if (control is not VBSSTab tab)
            return false;
        RefuseUnimplemented(name);
        switch (name.ToLowerInvariant().Trim('[', ']'))
        {
            case "_tab": break;
            case "tab": tab.Tab = Integer(value); break;
            case "tabs": tab.Tabs = Integer(value); break;
            case "tabsperrow": tab.TabsPerRow = Choice(value, 1, 99); break;
            case "rows": throw new VBRunTimeException(VBStandardError.PropertySetCantBeUsedWithAReadOnlyProperty);
            case "caption": tab.Caption(tab.Tab, Text(value)); break;
            case "style": tab.VbStyle = Choice(value, 0, 1); break;
            case "taborientation": tab.TabOrientation = Choice(value, 0, 3); break;
            case "tabheight": tab.TabHeight = Dimension(tab, value, true); break;
            case "tabmaxwidth": tab.TabMaxWidth = Dimension(tab, value, false); break;
            case "enabled": tab.IsEnabled = Boolean(value); break;
            case "wordwrap": tab.WordWrap = Boolean(value); break;
            case "showfocusrect": tab.ShowFocusRect = Boolean(value); break;
            default: return false;
        }
        return true;
    }

    private static int Index(VBSSTab tab, IReadOnlyList<Vb6Value> args)
    {
        if (args.Count != 1)
            throw new NotImplementedException("SSTab indexed argument counts are not measured");
        var index = Integer(args[0]);
        tab.CheckIndex(index);
        return index;
    }

    // The oracle records ranges, but not coercions. Refuse those rather than borrowing another control's rules.
    private static double Number(Vb6Value value) => value.Value switch
    {
        short n => n, int n => n, long n => n, float n => n, double n => n,
        _ => throw new NotImplementedException("SSTab property conversion is not measured"),
    };

    private static int Integer(Vb6Value value)
    {
        var number = Number(value);
        if (!double.IsFinite(number) || number != Math.Truncate(number) || number < int.MinValue || number > int.MaxValue)
            throw new NotImplementedException("SSTab integer conversion is not measured");
        return (int)number;
    }

    private static int Choice(Vb6Value value, int min, int max)
    {
        var n = Integer(value);
        return n >= min && n <= max ? n : throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
    }

    private static double Dimension(VBSSTab tab, Vb6Value value, bool height)
    {
        CheckDimensionScale(tab);
        var n = Number(value);
        if (height ? n <= 0 : n < 0)
            throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
        return ControlPositions.ToPixels(tab, height ? VBProperties.HeightProperty : VBProperties.WidthProperty, n);
    }

    private static void CheckDimensionScale(VBSSTab tab)
    {
        // The pixel sample reads 33 for 500 twips, unlike generic control geometry's fractional pixels.
        if (tab.Parent is Control host && VBProps.GetScaleMode(host) != VBScaleMode.Twip)
            throw new NotImplementedException("SSTab non-twip dimension rounding is not implemented");
    }

    private static bool Boolean(Vb6Value value) => value.Value is bool b ? b
        : throw new NotImplementedException("SSTab Boolean conversion is not measured");
    private static string Text(Vb6Value value) => value.Value is string s ? s
        : throw new NotImplementedException("SSTab text conversion is not measured");

    private static void RefuseUnimplemented(string name)
    {
        if (SSTabComponentClass.Instance.Member(name) is { Implemented: false })
            throw new NotImplementedException($"SSTab {name} is not implemented");
    }
}
