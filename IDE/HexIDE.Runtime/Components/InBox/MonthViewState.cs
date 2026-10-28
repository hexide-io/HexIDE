namespace HexIDE.Runtime.Components.InBox;

// The nonvisual part of #1095 A. Kept separate from placement and persistence until B and C are implemented.
internal sealed class MonthViewState : ICSharpPropertyBag
{
    private VbDate value = VbDate.FromDateTime(DateTime.Today);
    private VbDate minDate = VbDate.FromDateTime(new DateTime(1753, 1, 1));
    private VbDate maxDate = VbDate.FromDateTime(new DateTime(9999, 12, 31));
    private VbDate selStart;
    private VbDate selEnd;
    private bool multiSelect;
    private int maxSelCount = 7;
    private int rows = 1;
    private int columns = 1;
    private int startOfWeek = 1;
    private bool showToday = true;
    private bool showWeekNumbers;
    private int appearance = 1;
    private int borderStyle;
    private int mousePointer;
    private int oleDropMode;
    private int scrollRate;
    private bool enabled = true;

    public MonthViewState() => selStart = selEnd = value;

    public bool TryGetProperty(string name, out Vb6Value result)
    {
        result = name.ToLowerInvariant() switch
        {
            "appearance" => new(appearance), "borderstyle" => new(borderStyle),
            "mousepointer" => new(mousePointer), "oledropmode" => new(oleDropMode),
            "scrollrate" => new(scrollRate), "enabled" => new(enabled),
            "value" or "_value" => new(value),
            "mindate" => new(minDate), "maxdate" => new(maxDate),
            "selstart" => new(selStart), "selend" => new(selEnd),
            "multiselect" => new(multiSelect), "maxselcount" => new(maxSelCount),
            "monthrows" => new(rows), "monthcolumns" => new(columns),
            "startofweek" => new((long)startOfWeek),
            "showtoday" => new(showToday), "showweeknumbers" => new(showWeekNumbers),
            "year" => new(value.Instant.Year), "month" => new((long)value.Instant.Month),
            "day" => new(value.Instant.Day), "dayofweek" => new((long)value.Instant.DayOfWeek + 1),
            "week" => new(Week(value.Instant)),
            "visibledays" => throw Unmeasured(name),
            _ => default,
        };
        return name.ToLowerInvariant() is "value" or "_value" or "mindate" or "maxdate" or "selstart" or "selend"
            or "multiselect" or "maxselcount" or "monthrows" or "monthcolumns" or "startofweek" or "showtoday"
            or "appearance" or "borderstyle" or "mousepointer" or "oledropmode" or "scrollrate" or "enabled"
            or "week" or "showweeknumbers" or "year" or "month" or "day" or "dayofweek";
    }

    public bool TrySetProperty(string name, Vb6Value incoming)
    {
        switch (name.ToLowerInvariant())
        {
            case "appearance": appearance = BinaryStyle(incoming, name); break;
            case "borderstyle": borderStyle = BinaryStyle(incoming, name); break;
            case "mousepointer":
            {
                var pointer = Integer(incoming, name);
                if (pointer == -1) throw InvalidValue();
                if (pointer is < 0 or > 2) throw Unmeasured(name);
                mousePointer = pointer;
                break;
            }
            case "oledropmode":
            {
                var mode = Integer(incoming, name);
                if (mode == 2) throw InvalidValue();
                if (mode is < 0 or > 1) throw Unmeasured(name);
                oleDropMode = mode;
                break;
            }
            case "scrollrate":
            {
                var rate = Integer(incoming, name);
                if (rate == -1) throw InvalidValue();
                if (rate is not (0 or 1 or 3 or 100)) throw Unmeasured(name);
                scrollRate = rate;
                break;
            }
            case "enabled":
                if (Integer(incoming, name) is not (1 or 2 or -1)) throw Unmeasured(name);
                enabled = true;
                break;
            case "value" or "_value": SetValue(InRange(Date(incoming))); break;
            case "mindate":
            {
                var date = ExactDate(incoming, name);
                if (date.Instant.TimeOfDay != TimeSpan.Zero || value.Instant.TimeOfDay != TimeSpan.Zero) RefuseRangeChange(name);
                if (date.Instant.Year < 1753) throw Error(35772);
                RefuseTimedPair(date, value, name);
                RefuseTimedPair(date, maxDate, name);
                if (date.Serial > maxDate.Serial) throw Error(35775);
                if (date.Serial <= value.Serial) RefuseRangeChange(name);
                minDate = date;
                if (value.Serial < date.Serial) SetValue(date);
                break;
            }
            case "maxdate":
            {
                var date = ExactDate(incoming, name);
                if (date.Instant.TimeOfDay != TimeSpan.Zero || value.Instant.TimeOfDay != TimeSpan.Zero) RefuseRangeChange(name);
                if (date.Serial > VbDate.FromDateTime(new DateTime(9999, 12, 31)).Serial) throw Unmeasured(name);
                RefuseTimedPair(date, value, name);
                RefuseTimedPair(date, minDate, name);
                if (date.Serial < minDate.Serial) throw Error(35774);
                if (date.Serial < selStart.Serial || date.Serial >= selEnd.Serial) RefuseRangeChange(name);
                maxDate = date;
                if (value.Serial > date.Serial) SetValue(date);
                break;
            }
            case "selstart":
            {
                if (!multiSelect) throw Error(35786);
                var date = MemberDate(ExactDate(incoming, name), name);
                if (date.Instant.TimeOfDay != TimeSpan.Zero) throw Unmeasured(name);
                var end = date.Serial > selEnd.Serial ? date : selEnd;
                CheckSelection(date, end);
                value = selStart = date;
                selEnd = end;
                break;
            }
            case "selend":
            {
                if (!multiSelect) throw Error(35785);
                var date = MemberDate(ExactDate(incoming, name), name);
                if (date.Instant.TimeOfDay != TimeSpan.Zero) throw Unmeasured(name);
                if (date.Serial < selStart.Serial) { SetValue(date); break; }
                CheckSelection(selStart, date);
                selEnd = date;
                break;
            }
            case "multiselect":
            {
                var enabled = Boolean(incoming, name);
                if (!enabled && selStart != selEnd)
                {
                    if (value.Instant.TimeOfDay != TimeSpan.Zero) throw Unmeasured(name + " with a timed Value and selection range");
                    SetValue(selStart);
                }
                multiSelect = enabled;
                break;
            }
            case "maxselcount":
            {
                var count = Integer(incoming, name);
                if (count < 1) throw InvalidValue();
                if (count < selEnd.Serial - selStart.Serial + 1) throw Error(35770);
                maxSelCount = count;
                break;
            }
            case "monthrows": SetMonths(Integer(incoming, name), columns); break;
            case "monthcolumns": SetMonths(rows, Integer(incoming, name)); break;
            case "startofweek":
            {
                var day = Integer(incoming, name);
                startOfWeek = day is >= 1 and <= 7 ? day : throw InvalidValue();
                break;
            }
            case "showtoday": showToday = Boolean(incoming, name); break;
            case "showweeknumbers": showWeekNumbers = Boolean(incoming, name); break;
            case "day" or "month" or "year":
            {
                RefuseRangeChange(name);
                var part = Integer(incoming, name);
                var current = value.Instant;
                DateTime date;
                try
                {
                    date = new DateTime(name.Equals("year", StringComparison.OrdinalIgnoreCase) ? part : current.Year,
                        name.Equals("month", StringComparison.OrdinalIgnoreCase) ? part : current.Month,
                        name.Equals("day", StringComparison.OrdinalIgnoreCase) ? part : current.Day);
                }
                catch (ArgumentOutOfRangeException) { throw InvalidValue(); }
                if (current.TimeOfDay != TimeSpan.Zero) throw Unmeasured(name);
                SetValue(MemberDate(VbDate.FromDateTime(date), name));
                break;
            }
            case "dayofweek":
            {
                RefuseRangeChange(name);
                var day = Integer(incoming, name);
                if (day is < 1 or > 7) throw InvalidValue();
                if (value.Instant.TimeOfDay != TimeSpan.Zero) throw Unmeasured(name);
                DateTime date;
                try { date = value.Instant.AddDays(day - 1 - (int)value.Instant.DayOfWeek); }
                catch (ArgumentOutOfRangeException) { throw Unmeasured(name); }
                SetValue(MemberDate(VbDate.FromDateTime(date), name));
                break;
            }
            case "week":
            {
                var week = Integer(incoming, name);
                if (week is < 1 or > 53) throw InvalidValue();
                RefuseRangeChange(name);
                if (value.Instant.TimeOfDay != TimeSpan.Zero) throw Unmeasured(name);
                DateTime date;
                try { date = value.Instant.AddDays((week - Week(value.Instant)) * 7); }
                catch (ArgumentOutOfRangeException) { throw Unmeasured(name); }
                SetValue(MemberDate(VbDate.FromDateTime(date), name));
                break;
            }
            case "visibledays": throw Error(383);
            default: return false;
        }
        return true;
    }

    internal Vb6Value VisibleDay(Vb6Value index)
    {
        if (index.Type != Vb6Value.ValueType.Integer && index.Type != Vb6Value.ValueType.Double
            && !(index.Type == Vb6Value.ValueType.String && (string?)index.Value == "2"))
            throw Unmeasured("VisibleDays index conversion");
        var number = Convert.ToInt32(VB6BuiltIns.NarrowNum(index, Vb6Value.ValueType.Integer).Value);
        var count = rows * columns;
        if (count is not (1 or 12)) throw Unmeasured("VisibleDays first month for two to eleven months");
        var firstMonth = new DateTime(value.Instant.Year, count == 12 ? 1 : value.Instant.Month, 1);
        DateTime first;
        DateTime last;
        try
        {
            first = GridStart(firstMonth);
            last = GridStart(firstMonth.AddMonths(count - 1)).AddDays(41);
        }
        catch (ArgumentOutOfRangeException) { throw Unmeasured("VisibleDays at the calendar boundary"); }
        if (number < 1 || number > (last - first).Days + 1) throw InvalidValue();
        var now = DateTime.Now;
        return new Vb6Value(VbDate.FromDateTime(first.AddDays(number - 1)
            .AddHours(now.Hour).AddMinutes(now.Minute).AddSeconds(now.Second)));
    }

    private DateTime GridStart(DateTime date) => date.AddDays(-((7 + (int)date.DayOfWeek - (startOfWeek - 1)) % 7));

    internal int Week(DateTime date)
    {
        var first = GridStart(new DateTime(date.Year, 1, 4));
        if (date < first) first = GridStart(new DateTime(date.Year - 1, 1, 4));
        else if (date.Year < 9999 && date >= GridStart(new DateTime(date.Year + 1, 1, 4)))
            first = GridStart(new DateTime(date.Year + 1, 1, 4));
        return (date.Date - first).Days / 7 + 1;
    }

    private static int BinaryStyle(Vb6Value input, string name)
    {
        var style = Integer(input, name);
        if (style is -1 or 2) throw InvalidValue();
        return style is 0 or 1 ? style : throw Unmeasured(name);
    }

    internal (VbDate Value, VbDate Start, VbDate End) Selection => (value, selStart, selEnd);

    internal void RestoreSelection((VbDate Value, VbDate Start, VbDate End) selection) =>
        (value, selStart, selEnd) = selection;

    private void SetValue(VbDate date)
    {
        value = date;
        selStart = selEnd = VbDate.FromDateTime(date.Instant.Date);
    }

    private VbDate InRange(VbDate date)
    {
        RefuseTimedPair(date, minDate, "Value");
        RefuseTimedPair(date, maxDate, "Value");
        return date.Serial >= minDate.Serial && date.Serial <= maxDate.Serial ? date : throw Error(35773);
    }

    // Date parts and SelStart must not inherit Value’s out-of-range error.
    private VbDate MemberDate(VbDate date, string name)
    {
        RefuseTimedPair(date, minDate, name);
        RefuseTimedPair(date, maxDate, name);
        if (date.Serial >= minDate.Serial && date.Serial <= maxDate.Serial) return date;
        if (date.Serial > maxDate.Serial && name.Equals("SelEnd", StringComparison.OrdinalIgnoreCase)) throw Error(35770);
        throw Unmeasured(name + " outside MinDate/MaxDate");
    }

    // Keeping a bound's time was measured; how times interact on the boundary's day was not.
    private static void RefuseTimedPair(VbDate left, VbDate right, string name)
    {
        var first = left.Instant;
        var second = right.Instant;
        if (first.Date == second.Date && (first.TimeOfDay != TimeSpan.Zero || second.TimeOfDay != TimeSpan.Zero))
            throw Unmeasured(name + " time at a date boundary");
    }

    private void RefuseRangeChange(string name)
    {
        if (selStart != selEnd) throw Unmeasured(name + " with a selection range");
    }

    private void CheckSelection(VbDate start, VbDate end)
    {
        if (end.Serial - start.Serial + 1 > maxSelCount) throw Error(35770);
    }

    private void SetMonths(int newRows, int newColumns)
    {
        if (newRows is < 1 or > 12 || newColumns is < 1 or > 12) throw Error(35776);
        if (newRows * newColumns > 12) throw Error(35777);
        rows = newRows;
        columns = newColumns;
    }

    private static VbDate Date(Vb6Value input)
    {
        if (input.IsNull) throw new VBRunTimeException(VBStandardError.TypeMismatch);
        if (input.Type == Vb6Value.ValueType.EmptyVariant) return new VbDate(0);
        if (input.Type != Vb6Value.ValueType.Date && input.Type != Vb6Value.ValueType.String && input.Type != Vb6Value.ValueType.Integer
            && input.Type != Vb6Value.ValueType.Long && input.Type != Vb6Value.ValueType.Double
            && input.Type != Vb6Value.ValueType.Boolean && input.Type != Vb6Value.ValueType.EmptyVariant)
            throw Unmeasured("Value conversion");
        return (VbDate)VB6BuiltIns.CDate(input).Value!;
    }

    private static VbDate ExactDate(Vb6Value input, string name) => input.Type == Vb6Value.ValueType.Date
        ? (VbDate)input.Value! : throw Unmeasured(name + " conversion");

    private static int Integer(Vb6Value input, string name) => input.Type == Vb6Value.ValueType.Integer
        ? Convert.ToInt32(input.Value) : throw Unmeasured(name + " conversion");

    private static bool Boolean(Vb6Value input, string name) => input.Type == Vb6Value.ValueType.Boolean
        ? (bool)input.Value! : throw Unmeasured(name + " conversion");

    private static VBRunTimeException InvalidValue() => new(VBStandardError.InvalidPropertyValue);
    private static NotImplementedException Unmeasured(string name) =>
        new($"MonthView {name} is unmeasured and not implemented (#1095)");

    private static VBRunTimeException Error(int number) => new(new VBStandardError(number, number switch
    {
        383 => "Property is read-only",
        35770 => "An invalid date range was specified.",
        35772 => "An invalid date was specified for the MinDate property.",
        35773 => "A date was specified that does not fall within the MinDate and MaxDate properties.",
        35774 => "A value was specified for the MaxDate property that is lower than the current value of MinDate.",
        35775 => "A value was specified for the MinDate property that is higher than the current value of MaxDate.",
        35776 => "A value was specified for MonthRows or MonthColumns that is not between 1 and 12.",
        35777 => "A value was specified for MonthRows or MonthColumns that would cause the total number of months (i.e. MonthRows * MonthCols) to be greater than 12.",
        35785 => "Can't set SelEnd property when MultiSelect = False.",
        35786 => "Can't set SelStart property when MultiSelect = False.",
        _ => throw new ArgumentOutOfRangeException(nameof(number)),
    }));
}
