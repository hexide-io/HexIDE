using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace HexIDE.Runtime.Interpreter;

// Phase 3 — Date/Time intrinsics. Parts (Year..Second, Weekday) -> Integer; DateAdd/DateSerial/TimeSerial ->
// Date; DateDiff -> Long; Timer -> Single. All pinned against vb6.exe: Weekday defaults to a vbSunday base;
// DateSerial/TimeSerial roll over out-of-range fields; DateAdd clamps month-ends (Jan31 +1m -> Feb29);
// DateDiff counts interval BOUNDARIES crossed (8:30->9:15 = 1 hour), not the floored difference.
public partial class VB6BuiltIns
{
    // VB6 date serial 0 = 1899-12-30; time-only values are built on this epoch.
    private static readonly DateTime Epoch = new DateTime(1899, 12, 30);

    private static void RegisterDateTime(Dictionary<string, BuiltinFn> d)
    {
        // Clock (non-deterministic).
        d["Now"]   = (_, _, _) => new Vb6Value(DateTime.Now);
        d["Date"]  = (_, _, _) => new Vb6Value(DateTime.Now.Date);
        d["Time"]  = (_, _, _) => new Vb6Value(Epoch + DateTime.Now.TimeOfDay);
        d["Timer"] = (_, _, _) => new Vb6Value((float)DateTime.Now.TimeOfDay.TotalSeconds);

        // Parts -> Integer.
        d["Year"]    = (_, a, _) => new Vb6Value(AsDate(a[0]).Year);
        d["Month"]   = (_, a, _) => new Vb6Value(AsDate(a[0]).Month);
        d["Day"]     = (_, a, _) => new Vb6Value(AsDate(a[0]).Day);
        d["Hour"]    = (_, a, _) => new Vb6Value(AsDate(a[0]).Hour);
        d["Minute"]  = (_, a, _) => new Vb6Value(AsDate(a[0]).Minute);
        d["Second"]  = (_, a, _) => new Vb6Value(AsDate(a[0]).Second);
        d["Weekday"] = (_, a, _) => new Vb6Value(WeekdayNum(AsDate(a[0]), Fdow(a, 1)));

        // Constructors -> Date.
        d["DateSerial"] = (_, a, _) => DateSerial(AsInt(a[0]), AsInt(a[1]), AsInt(a[2]));
        d["TimeSerial"] = (_, a, _) => TimeSerial(AsInt(a[0]), AsInt(a[1]), AsInt(a[2]));
        d["DateValue"]  = (_, a, _) => new Vb6Value(AsDate(a[0]).Date);
        d["TimeValue"]  = (_, a, _) => new Vb6Value(Epoch + AsDate(a[0]).TimeOfDay);

        // Arithmetic.
        d["DateAdd"]  = (_, a, _) => DateAdd(Interval(a[0]), AsInt(a[1]), AsDate(a[2]));
        d["DateDiff"] = (_, a, _) => new Vb6Value(DateDiff(Interval(a[0]), AsDate(a[1]), AsDate(a[2]), Fdow(a, 3)));
        d["DatePart"] = (_, a, _) => new Vb6Value(DatePart(Interval(a[0]), AsDate(a[1]), Fdow(a, 2)));

        // Names -> String (current culture).
        d["MonthName"]   = (_, a, _) => MonthName(AsInt(a[0]), Supplied(a, 1) && AsDouble(a[1]) != 0);
        d["WeekdayName"] = (_, a, _) => WeekdayName(AsInt(a[0]), Supplied(a, 1) && AsDouble(a[1]) != 0, Fdow(a, 2));
    }

    private static string Interval(Vb6Value v) => AsStr(v).Trim().ToLowerInvariant();

    // firstDayOfWeek arg at index i, defaulting to vbSunday(1); vbUseSystemDayOfWeek(0) is treated as vbSunday.
    private static int Fdow(IReadOnlyList<Vb6Value> a, int i)
    {
        int f = Supplied(a, i) ? AsInt(a[i]) : 1;
        return f == 0 ? 1 : f;
    }

    /// <summary>
    /// A time written without a date is that time on the zero date, 30 December 1899, which is what lets
    /// <c>CStr(#10:30:00 AM#)</c> leave the date out and <c>CDbl</c> of it be 0.4375. Parsed with
    /// <see cref="DateTimeStyles.NoCurrentDateDefault"/>, .NET puts such a time on 1 January 0001, which no VB6 date
    /// can be (its range starts in the year 100); without it, on today's date, which is what the literal used to get.
    /// </summary>
    internal static DateTime OnZeroDateIfTimeOnly(DateTime parsed)
        => parsed.Date == DateTime.MinValue ? Epoch + parsed.TimeOfDay : parsed;

    // The first two numbers of a date, when they could be a day and a month: one or two digits each, separated by
    // what VB6 reads between them. A dot is not among them: under en-GB, "1.2.2003" is not a date at all (measured).
    private static readonly Regex LeadingDayAndMonth = new(@"^(\s*)(\d{1,2})([/\- ]+)(\d{1,2})(?!\d)", RegexOptions.CultureInvariant);

    /// <summary>
    /// Text a program reads as a date, in its locale (#777). When the locale's order makes an impossible date, VB6
    /// reads the first two numbers the other way round, and so does this (#793).
    /// </summary>
    /// <remarks>
    /// Measured under en-GB, which puts the day first: <c>CDate("1/13/2003")</c> is 13 January 2003, with or without
    /// a time after it, with dashes, spaces or mixed separators, and with a two-digit year (<c>"1/13/30"</c> is 2030)
    /// or none (<c>"1/13"</c> is 13 January this year). <c>"12/1/2003"</c> is still 12 January: the swap happens only
    /// when the locale's order fails. It does not rescue a date impossible both ways round (<c>"31/2/2003"</c>,
    /// <c>"13/13/2003"</c>), a year first (<c>"2003/13/1"</c>), or a month and a year (<c>"13/2003"</c>): each is Type
    /// mismatch. A locale that puts the month first is taken to swap the same way, which is not measured.
    /// </remarks>
    internal static bool TryReadDate(string text, out DateTime result)
    {
        var culture = VbLocale.Current;
        if (DateTime.TryParse(text, culture, DateTimeStyles.NoCurrentDateDefault, out result))
            return true;
        var leading = LeadingDayAndMonth.Match(text);
        if (!leading.Success)
            return false;
        var swapped = leading.Groups[1].Value + leading.Groups[4].Value + leading.Groups[3].Value + leading.Groups[2].Value
                      + text[leading.Length..];
        return DateTime.TryParse(swapped, culture, DateTimeStyles.NoCurrentDateDefault, out result);
    }

    // VB6 date serial (OLE Automation) — accepts a Date, a numeric serial, or a parseable string.
    private static DateTime AsDate(Vb6Value v)
    {
        if (v.Value is DateTime dt) return dt;
        if (v.Value is string s && TryReadDate(s, out var pd))
            return OnZeroDateIfTimeOnly(pd);
        if (Vb6Value.TryNumericToDouble(v, out var oa))
        {
            try { return DateTime.FromOADate(oa); }
            catch (ArgumentException) { throw InvalidCall(); }
        }
        throw new VBRunTimeException(VBStandardError.TypeMismatch);
    }

    // 1=Sunday..7=Saturday shifted so the firstDayOfWeek day is 1.
    private static int WeekdayNum(DateTime dt, int fdow) => ((int)dt.DayOfWeek - (fdow - 1) + 7) % 7 + 1;

    // Two-digit years follow the regional window, not a fixed rule (#777, measured): with Windows' default window
    // ending in 2049, DateSerial(30, 1, 1) is 2030, (49, ...) is 2049 and (50, ...) is 1950, against the documented
    // 0-29/30-99 split. The locale's calendar carries that window: its DateTimeFormat's, which is Gregorian for a
    // program, not CultureInfo.Calendar, which under th-TH would put 30 in the Buddhist year 2530. Four-digit years
    // pass through.
    private static Vb6Value DateSerial(int year, int month, int day)
    {
        if (year is >= 0 and <= 99) year = VbLocale.Current.DateTimeFormat.Calendar.ToFourDigitYear(year);
        try { return new Vb6Value(new DateTime(year, 1, 1).AddMonths(month - 1).AddDays(day - 1)); }
        catch (ArgumentOutOfRangeException) { throw InvalidCall(); }
    }

    private static Vb6Value DateAdd(string iv, int n, DateTime dt)
    {
        try
        {
            return new Vb6Value(iv switch
            {
                "yyyy" => dt.AddYears(n),
                "q" => dt.AddMonths(n * 3),
                "m" => dt.AddMonths(n),
                "y" or "d" or "w" => dt.AddDays(n),
                "ww" => dt.AddDays(n * 7),
                "h" => dt.AddHours(n),
                "n" => dt.AddMinutes(n),
                "s" => dt.AddSeconds(n),
                _ => throw InvalidCall(),
            });
        }
        // Overflow past the Date range (e.g. DateAdd("yyyy", 100000, Now)) → Err 5 (oracle-verified: DateAdd gives
        // Invalid procedure call, NOT Overflow), not an uncatchable .NET ArgumentOutOfRangeException.
        catch (ArgumentOutOfRangeException) { throw InvalidCall(); }
    }

    // TimeSerial builds a time by adding h/m/s to the epoch; a huge count (e.g. TimeSerial(9999999, 0, 0)) pushes
    // past the Date range → Err 6 (Overflow, oracle-verified — distinct from DateAdd's Err 5), not an uncatchable
    // .NET ArgumentOutOfRangeException.
    private static Vb6Value TimeSerial(int h, int m, int s)
    {
        try { return new Vb6Value(Epoch.AddHours(h).AddMinutes(m).AddSeconds(s)); }
        catch (ArgumentOutOfRangeException) { throw new VBRunTimeException(VBStandardError.Overflow); }
    }

    // Boundary-counting: floor each operand to the interval, then subtract (verified vs vb6.exe).
    private static long DateDiff(string iv, DateTime d1, DateTime d2, int fdow) => iv switch
    {
        "yyyy" => d2.Year - d1.Year,
        "q" => (d2.Year * 4 + (d2.Month - 1) / 3) - (d1.Year * 4 + (d1.Month - 1) / 3),
        "m" => (d2.Year * 12 + d2.Month) - (d1.Year * 12 + d1.Month),
        "y" or "d" => (long)(d2.Date - d1.Date).TotalDays,
        "w" => (d2.Date - d1.Date).Days / 7,
        "ww" => (WeekStart(d2.Date, fdow) - WeekStart(d1.Date, fdow)).Days / 7,
        "h" => (long)(Floor(d2, "h") - Floor(d1, "h")).TotalHours,
        "n" => (long)(Floor(d2, "n") - Floor(d1, "n")).TotalMinutes,
        "s" => (long)(Floor(d2, "s") - Floor(d1, "s")).TotalSeconds,
        _ => throw InvalidCall(),
    };

    private static int DatePart(string iv, DateTime dt, int fdow) => iv switch
    {
        "yyyy" => dt.Year,
        "q" => (dt.Month - 1) / 3 + 1,
        "m" => dt.Month,
        "y" => dt.DayOfYear,
        "d" => dt.Day,
        "w" => WeekdayNum(dt, fdow),
        "ww" => WeekOfYear(dt),
        "h" => dt.Hour,
        "n" => dt.Minute,
        "s" => dt.Second,
        _ => throw InvalidCall(),
    };

    private static DateTime Floor(DateTime d, string unit) => unit switch
    {
        "h" => new DateTime(d.Year, d.Month, d.Day, d.Hour, 0, 0),
        "n" => new DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0),
        _ => new DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second),
    };

    private static DateTime WeekStart(DateTime d, int fdow)
    {
        int target = (fdow - 1) % 7;                 // 0=Sunday..6=Saturday
        int back = ((int)d.DayOfWeek - target + 7) % 7;
        return d.AddDays(-back);
    }

    // vbFirstJan1 + Sunday-start week rule (VB6 default), matching vb6.exe (Jan 1 2020 -> 1, Dec 31 2020 -> 53).
    private static int WeekOfYear(DateTime d) =>
        CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(d, CalendarWeekRule.FirstDay, DayOfWeek.Sunday);

    private static Vb6Value MonthName(int m, bool abbreviate)
    {
        if (m is < 1 or > 12) throw InvalidCall();
        var dfi = VbLocale.Current.DateTimeFormat;
        return new Vb6Value(abbreviate ? dfi.GetAbbreviatedMonthName(m) : dfi.GetMonthName(m));
    }

    private static Vb6Value WeekdayName(int weekday, bool abbreviate, int fdow)
    {
        if (weekday is < 1 or > 7) throw InvalidCall();
        var day = (DayOfWeek)(((fdow - 1) + (weekday - 1)) % 7);
        var dfi = VbLocale.Current.DateTimeFormat;
        return new Vb6Value(abbreviate ? dfi.GetAbbreviatedDayName(day) : dfi.GetDayName(day));
    }
}
