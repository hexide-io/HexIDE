using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.AvaloniaInterop;

internal static class SliderMembers
{
    public static bool? TryGet(Control control, string name, out Vb6Value value)
    {
        value = default;
        if (control is not VBSlider s)
            return null;
        Refuse(name);
        object? read = name.ToUpperInvariant() switch
        {
            "MIN" => s.Min, "MAX" => s.Max, "VALUE" or "_VALUE" => s.Value,
            "SMALLCHANGE" => s.SmallChange, "LARGECHANGE" => s.LargeChange,
            "TICKSTYLE" => s.TickStyle, "TICKFREQUENCY" => s.TickFrequency, "GETNUMTICKS" => s.GetNumTicks,
            "ORIENTATION" => s.Orientation, "TEXTPOSITION" => s.TextPosition, "TEXT" => s.Text,
            "SELECTRANGE" => s.SelectRange, "SELSTART" => s.SelStart, "SELLENGTH" => s.SelLength,
            "BORDERSTYLE" => s.BorderStyle, "OLEDROPMODE" => s.OLEDropMode,
            "ENABLED" => s.IsEnabled, "MOUSEPOINTER" => (long)AttachedProperties.GetMousePointer(s),
            _ => null,
        };
        if (read is null)
            return null;
        value = read switch { long n => new Vb6Value(n), bool b => new Vb6Value(b), string t => new Vb6Value(t), _ => default };
        return true;
    }

    public static bool? TrySet(Control control, string name, Vb6Value value, bool throughObject = false)
    {
        if (control is not VBSlider s)
            return null;
        Refuse(name);
        s.InvalidateVisual();
        var key = name.ToUpperInvariant();
        if (key == "GETNUMTICKS")
            throw new VBRunTimeException(VBStandardError.WrongNumberOfArgumentsOrInvalidPropertyAssignment);
        // #1091 / oracle PR #5506: Object dispatch reports a Null store as 13 rather than 94.
        if (throughObject && value.IsNull && key is "MIN" or "MAX" or "VALUE" or "_VALUE"
            or "SMALLCHANGE" or "LARGECHANGE" or "TICKFREQUENCY" or "SELSTART" or "SELLENGTH"
            or "SELECTRANGE" or "ENABLED" or "TEXT")
            throw new VBRunTimeException(VBStandardError.TypeMismatch);
        if (key is "SELECTRANGE" or "ENABLED")
        {
            var flag = (bool)VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Boolean, null).Value!;
            if (key == "ENABLED") s.IsEnabled = flag;
            else
            {
                s.SelectRange = flag;
                if (!flag) s.SelectionStart = s.SelectionLength = 0;
            }
            return true;
        }
        if (key == "TEXT")
        {
            var text = VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.String, null);
            s.Text = (string)text.WithoutOddBytes("SliderMembers text use").Value!;
            return true;
        }
        if (SliderComponentClass.Instance.Member(name) is not { Kind: MemberKind.Property } && key != "_VALUE")
            return null;
        var n = Number(value);
        switch (key)
        {
            case "VALUE": case "_VALUE": s.SetValue(n, true); break;
            case "MIN": s.SetRange(n, true); break;
            case "MAX": s.SetRange(n, false); break;
            case "SMALLCHANGE": s.SmallChange = AtLeastZero(n); break;
            case "LARGECHANGE": s.LargeChange = AtLeastZero(n); break;
            case "TICKFREQUENCY": s.TickFrequency = AtLeastZero(n); break;
            case "TICKSTYLE": s.TickStyle = Choice(n, 3); break;
            case "TEXTPOSITION": s.TextPosition = Choice(n, 1); break;
            case "BORDERSTYLE": s.BorderStyle = Choice(n, 1); break;
            case "OLEDROPMODE": s.OLEDropMode = Choice(n, 1); break;
            case "ORIENTATION":
                Choice(n, 1);
                if (s.Orientation != n)
                {
                    var width = s.Width;
                    s.Width = System.Math.Round(s.Height);
                    s.Height = width;
                    s.Orientation = n;
                }
                break;
            case "MOUSEPOINTER":
                if (n is not (>= 0 and <= 15 or 99)) throw Invalid();
                var pointer = (VBCursorType)n;
                s.Cursor = pointer.ToCursor();
                AttachedProperties.SetMousePointer(s, pointer);
                break;
            case "SELSTART":
                if (n < 0 || n < s.Min || n > s.Max) throw Invalid();
                if (!s.SelectRange) s.SetValue(n, false);
                else
                {
                    s.SelectionStart = n;
                    s.SelectionLength = System.Math.Min(s.SelectionLength, s.Max - n);
                }
                break;
            case "SELLENGTH":
                if (!s.SelectRange || n < 0 || n > s.Max - s.SelectionStart) throw Invalid();
                s.SelectionLength = n;
                break;
            default: return null;
        }
        return true;
    }

    public static bool? TryCall(Control control, string name)
    {
        if (control is not VBSlider s) return null;
        Refuse(name);
        switch (name.ToUpperInvariant())
        {
            case "CLEARSEL":
                if (s.SelectRange) { s.SelectionStart = -1; s.SelectionLength = 0; s.InvalidateVisual(); }
                return true;
            case "DOCLICK": s.ExecuteSub(ComponentBaseClass.ClickEvent); return true;
            case "REFRESH": s.InvalidateVisual(); return true;
            default: return null;
        }
    }

    private static long Number(Vb6Value value) =>
        System.Convert.ToInt64(VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Long, null).Value);

    private static long AtLeastZero(long n) => n >= 0 ? n : throw Invalid();
    private static long Choice(long n, long max) => n >= 0 && n <= max ? n : throw Invalid();
    private static VBRunTimeException Invalid() => new(VBStandardError.InvalidPropertyValue);
    private static void Refuse(string name)
    {
        if (SliderComponentClass.Instance.Member(name) is { Implemented: false, Kind: not MemberKind.Event })
            throw new NotImplementedException($"Slider {name} is not implemented (#1091).");
    }
}
