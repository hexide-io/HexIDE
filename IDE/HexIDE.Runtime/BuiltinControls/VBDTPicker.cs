using Avalonia.Controls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

// VB6 SP6 native and p-code, #1094 group 1. Input and persistence follow in parts B and C.
public sealed class VBDTPicker : Control, ICSharpPropertyBag
{
    private DateTime current = DateTime.Now;
    private DateTime min = new(1601, 1, 1), max = new(9999, 12, 31);
    private bool checkBox, upDown, isNull;
    private int format = 1;
    private string customFormat = "";
    private int mousePointer, oleDropMode;
    private readonly Dictionary<string, long> calendarColours = new(StringComparer.Ordinal)
    {
        ["calendarbackcolor"] = unchecked((int)0x80000005),
        ["calendarforecolor"] = unchecked((int)0x80000012),
        ["calendartitlebackcolor"] = unchecked((int)0x8000000f),
        ["calendartitleforecolor"] = unchecked((int)0x80000012),
        ["calendartrailingforecolor"] = unchecked((int)0x80000011),
    };

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        var member = name.ToLowerInvariant();
        if (IsCalendarColour(member))
        {
            value = new Vb6Value(calendarColours[member]);
            return true;
        }
        value = member switch
        {
            "value" => isNull ? Vb6Value.Null : new Vb6Value(VbDate.FromDateTime(current)),
            "mindate" => new Vb6Value(VbDate.FromDateTime(min)),
            "maxdate" => new Vb6Value(VbDate.FromDateTime(max)),
            "format" => new Vb6Value((long)format),
            "mousepointer" => new Vb6Value((short)mousePointer),
            "oledropmode" => new Vb6Value((short)oleDropMode),
            "customformat" => new Vb6Value(customFormat),
            "checkbox" => new Vb6Value(checkBox),
            "updown" => new Vb6Value(upDown),
            "year" or "month" or "day" or "hour" or "minute" or "second" or "dayofweek" =>
                isNull ? Vb6Value.Null : new Vb6Value((short)Part(member)),
            _ => default,
        };
        if (member.StartsWith("calendar", StringComparison.Ordinal) || member is "hwnd" or "font" or "mouseicon")
            throw Unimplemented(name);
        return member is "value" or "mousepointer" or "oledropmode" or "mindate" or "maxdate" or "format" or "customformat" or "checkbox" or "updown"
            or "year" or "month" or "day" or "hour" or "minute" or "second" or "dayofweek";
    }

    public bool TrySetProperty(string name, Vb6Value value)
    {
        switch (name.ToLowerInvariant())
        {
            case "value":
                if (value.IsNull || value.Type == Vb6Value.ValueType.EmptyVariant)
                {
                    if (!checkBox) throw Error(35787, "Can't set Value to NULL when CheckBox property = FALSE.");
                    if (!value.IsNull) throw Unimplemented("Empty with CheckBox");
                    isNull = true;
                }
                else SetDate(Date(value, numeric: true));
                break;
            case "mindate":
                var lower = Date(value);
                if (lower > max) throw Error(35775, "A value was specified for the MinDate property that is higher than the current value of MaxDate.");
                min = lower;
                if (current < min) current = min.Date + current.TimeOfDay;
                break;
            case "maxdate":
                var upper = Date(value);
                if (upper < min) throw Error(35774, "A value was specified for the MaxDate property that is lower than the current value of MinDate.");
                max = upper;
                if (current > max) current = max.Date + current.TimeOfDay;
                break;
            case "checkbox":
                checkBox = Boolean(value);
                if (!checkBox) isNull = false;
                break;
            case "updown": upDown = Boolean(value); break;
            case "customformat":
                if (value.Type != Vb6Value.ValueType.String) throw Unimplemented("CustomFormat conversion");
                customFormat = (string)value.Value!;
                break;
            case "format":
                var next = Integer(value);
                if (next is < 0 or > 3) throw Invalid();
                format = next;
                break;
            case "year" or "month" or "day" or "hour" or "minute" or "second" or "dayofweek":
                SetPart(name.ToLowerInvariant(), Integer(value, part: true));
                break;
            case "hwnd": throw Error(383, "Property is read-only");
            case "calendarbackcolor" or "calendarforecolor" or "calendartitlebackcolor" or "calendartitleforecolor"
                or "calendartrailingforecolor":
                if (value.Type != Vb6Value.ValueType.Integer && value.Type != Vb6Value.ValueType.Long)
                    throw Unimplemented(name + " conversion");
                var colour = Convert.ToInt64(value.Value);
                if (colour is < 0 or > 0xffffff)
                    throw Unimplemented(name + " system colour");
                calendarColours[name.ToLowerInvariant()] = colour;
                break;
            case "mousepointer":
                var pointer = Integer(value);
                if (pointer == 100) throw Invalid();
                if (pointer is not (0 or 11 or 99)) throw Unimplemented("MousePointer value");
                mousePointer = pointer;
                break;
            case "oledropmode":
                var mode = Integer(value);
                if (mode is not (0 or 1)) throw Unimplemented("OLEDropMode value");
                oleDropMode = mode;
                break;
            case "font" or "mouseicon": throw Unimplemented(name);
            default: return false;
        }
        return true;
    }

    private static bool IsCalendarColour(string name) => name is "calendarbackcolor" or "calendarforecolor"
        or "calendartitlebackcolor" or "calendartitleforecolor" or "calendartrailingforecolor";

    private int Part(string name) => name switch
    {
        "year" => current.Year, "month" => current.Month, "day" => current.Day,
        "hour" => current.Hour, "minute" => current.Minute, "second" => current.Second,
        "dayofweek" => (int)current.DayOfWeek + 1, _ => throw Unimplemented(name),
    };

    private void SetPart(string name, int number)
    {
        var date = isNull ? DateTime.Now : current;
        DateTime next;
        try
        {
            next = name == "dayofweek"
                ? number is >= 1 and <= 7 ? date.AddDays(number - ((int)date.DayOfWeek + 1)) : throw Invalid()
                : new DateTime(name == "year" ? number : date.Year, name == "month" ? number : date.Month,
                    name == "day" ? number : date.Day, name == "hour" ? number : date.Hour,
                    name == "minute" ? number : date.Minute, name == "second" ? number : date.Second);
        }
        catch (ArgumentOutOfRangeException) { throw Invalid(); }
        if (name == "year" && next.Date < min.Date) throw Invalid();
        SetDate(next);
    }

    private void SetDate(DateTime date)
    {
        if (date.Date < min.Date || date.Date > max.Date)
            throw Error(35773, "A date was specified that does not fall within the MinDate and MaxDate properties.");
        if (date < min || date > max) return;
        current = date;
        isNull = false;
    }

    private static DateTime Date(Vb6Value value, bool numeric = false)
    {
        if (value.Type != Vb6Value.ValueType.Date && value.Type != Vb6Value.ValueType.String
            && value.Type != Vb6Value.ValueType.Boolean
            && !(numeric && (value.Type == Vb6Value.ValueType.Long || value.Type == Vb6Value.ValueType.Double)))
            throw Unimplemented("date conversion");
        try { return ((VbDate)VB6BuiltIns.CDate(value).Value!).Instant; }
        catch (VBRunTimeException) { throw Invalid(); }
    }

    private static int Integer(Vb6Value value, bool part = false)
    {
        if (part && value.IsNull) throw Invalid();
        if (value.Type != Vb6Value.ValueType.Integer && value.Type != Vb6Value.ValueType.Long
            && value.Type != Vb6Value.ValueType.Double && !(part && value.Type == Vb6Value.ValueType.String))
            throw Unimplemented("integer conversion");
        try { return Convert.ToInt32(VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Integer, null).Value); }
        catch (VBRunTimeException ex) when (part && ex.Error.ErrNo == 13) { throw Invalid(); }
    }

    private static bool Boolean(Vb6Value value) => value.Type == Vb6Value.ValueType.Boolean
        ? (bool)value.Value! : throw Unimplemented("Boolean conversion");
    private static VBRunTimeException Invalid() => new(VBStandardError.InvalidPropertyValue);
    private static VBRunTimeException Error(int code, string message) => new(new VBStandardError(code, message));
    private static NotImplementedException Unimplemented(string member) => new($"DTPicker {member} is not implemented (#1094)");
}
