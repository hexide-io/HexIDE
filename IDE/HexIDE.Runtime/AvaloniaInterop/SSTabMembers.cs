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
        var index = Index(tab, name, args);
        return name.ToLowerInvariant() switch
        {
            "tabcaption" => new Vb6Value(tab.Caption(index)),
            "tabenabled" => new Vb6Value(tab.TabEnabled(index)),
            "tabvisible" => new Vb6Value(tab.TabVisible(index)),
            _ => new Vb6Value(tab.TabPicture(index)),
        };
    }

    public static void WriteIndexed(VBSSTab tab, string name, IReadOnlyList<Vb6Value> args, Vb6Value value)
    {
        var index = Index(tab, name, args);
        switch (name.ToLowerInvariant())
        {
            case "tabcaption": tab.Caption(index, Text(value)); break;
            case "tabenabled": tab.TabEnabled(index, Boolean(value)); break;
            case "tabvisible": tab.TabVisible(index, Boolean(value)); break;
            default: tab.TabPicture(index, Picture(value)); break;
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
            case "picture": value = new Vb6Value(tab.TabPicture(tab.Tab)); break;
            case "caption": value = new Vb6Value(tab.Caption(tab.Tab)); break;
            case "style": value = new Vb6Value((long)tab.VbStyle); break;
            case "taborientation": value = new Vb6Value((long)tab.TabOrientation); break;
            case "tabheight": value = ReadDimension(tab, tab.TabHeightHimetric); break;
            case "tabmaxwidth": value = ReadDimension(tab, tab.TabMaxWidthHimetric); break;
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
            case "picture": tab.TabPicture(tab.Tab, Picture(value)); break;
            case "caption": tab.Caption(tab.Tab, Text(value)); break;
            case "style": tab.VbStyle = Choice(value, 0, 1); break;
            case "taborientation": tab.TabOrientation = Choice(value, 0, 3); break;
            case "tabheight": tab.TabHeightHimetric = Dimension(tab, value, true); break;
            case "tabmaxwidth": tab.TabMaxWidthHimetric = Dimension(tab, value, false); break;
            case "enabled": tab.IsEnabled = Boolean(value); break;
            case "wordwrap": tab.WordWrap = Boolean(value); break;
            case "showfocusrect": tab.ShowFocusRect = Boolean(value); break;
            default: return false;
        }
        return true;
    }

    private static PanelPicture Picture(Vb6Value value)
    {
        if (value.Value is PanelPicture picture) return picture;
        if (value.Type == Vb6Value.ValueType.Object && value.Value is null) return new PanelPicture([]);
        // #3426 / #4297: controls supply a separate Picture object, not their identity.
        if (value.Value is VBPictureBox box) return ControlPicture(box.PictureData);
        if (value.Value is VBImage image) return ControlPicture(image.Picture);
        if (value.Value is Control and not (VBTextBox or VBLabel or IFormView))
            throw new NotImplementedException("SSTab picture assignment from this control is unmeasured (#3426)");
        throw new VBRunTimeException(VBStandardError.TypeMismatch);
    }

    internal static PanelPicture ControlPicture(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 }) return new PanelPicture([]);
        var offset = Serialization.FrxDeserializer.PayloadOffset(bytes);
        offset += VBImage.StdPictureHeaderLength(bytes, offset);
        return PanelPicture.FromFile(bytes[offset..]);
    }

    private static int Index(VBSSTab tab, string name, IReadOnlyList<Vb6Value> args)
    {
        if (args.Count != 1)
            throw new NotImplementedException("SSTab indexed argument counts are not measured");
        var index = name.Equals("tabpicture", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToInt32(VB6BuiltIns.NarrowNum(args[0], Vb6Value.ValueType.Integer).Value) : Integer(args[0]);
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

    private static VBScaleMode DimensionScale(VBSSTab tab) =>
        tab.Parent is Control host ? VBProps.GetScaleMode(host) : VBScaleMode.Twip;

    private static Vb6Value ReadDimension(VBSSTab tab, double himetric)
    {
        var mode = DimensionScale(tab);
        // #4953: whole HIMETRIC storage, rounded reads only in twips and pixels.
        if (mode == VBScaleMode.Twip)
            return new Vb6Value((float)Math.Round(himetric * 1440 / 2540));
        if (mode == VBScaleMode.Pixel)
            return new Vb6Value((float)Math.Round(himetric * 96 / 2540));
        if (mode is not (VBScaleMode.Point or VBScaleMode.Character or VBScaleMode.Inch)
            || himetric != Math.Truncate(himetric))
            throw new NotImplementedException("SSTab dimension reads in this scale or at half HIMETRIC are not implemented (#4970)");
        var host = tab.Parent as Control;
        var unit = host is not null && RuntimeScale.Of(host) is { } scale
            ? scale.TwipsPerUnit(false) : RuntimeScale.Unit(mode, false);
        return new Vb6Value((float)(himetric * (float)(1440d / 2540) / unit));
    }

    private static double Dimension(VBSSTab tab, Vb6Value value, bool height)
    {
        var n = Number(value);
        if (height ? n <= 0 : n < 0)
            throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
        var mode = DimensionScale(tab);
        if (mode is not (VBScaleMode.Twip or VBScaleMode.Pixel))
            throw new NotImplementedException("SSTab dimension assignments in this scale are not implemented (#4970)");
        var himetric = n * (mode == VBScaleMode.Pixel ? 15 : 1) * 2540 / 1440;
        // Exact-half storage rounding is unmeasured; retain the half for twip/pixel reads.
        if (himetric - Math.Truncate(himetric) == 0.5)
            return himetric;
        var rounded = Math.Round(himetric);
        if (n > 0 && rounded == 0)
            throw new NotImplementedException("SSTab positive dimensions rounding to zero HIMETRIC are not implemented (#4970)");
        return rounded;
    }

    private static bool Boolean(Vb6Value value) => value.Value is bool b ? b
        : throw new NotImplementedException("SSTab Boolean conversion is not measured");
    private static string Text(Vb6Value value) => value.WithoutOddBytes("SSTabMembers text use").Value is string s ? s
        : throw new NotImplementedException("SSTab text conversion is not measured");

    private static void RefuseUnimplemented(string name)
    {
        if (SSTabComponentClass.Instance.Member(name) is { Implemented: false })
            throw new NotImplementedException($"SSTab {name} is not implemented");
    }
}
