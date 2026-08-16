using System;
using System.Collections.Generic;
using System.Globalization;

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
        d["MonthName"]   = (_, a, _) => MonthName(AsInt(a[0]), a.Count >= 2 && AsDouble(a[1]) != 0);
        d["WeekdayName"] = (_, a, _) => WeekdayName(AsInt(a[0]), a.Count >= 2 && AsDouble(a[1]) != 0, Fdow(a, 2));
    }

    private static string Interval(Vb6Value v) => AsStr(v).Trim().ToLowerInvariant();

    // firstDayOfWeek arg at index i, defaulting to vbSunday(1); vbUseSystemDayOfWeek(0) is treated as vbSunday.
    private static int Fdow(IReadOnlyList<Vb6Value> a, int i)
    {
        int f = a.Count > i ? AsInt(a[i]) : 1;
        return f == 0 ? 1 : f;
    }

    // VB6 date serial (OLE Automation) — accepts a Date, a numeric serial, or a parseable string.
    private static DateTime AsDate(Vb6Value v)
    {
        if (v.Value is DateTime dt) return dt;
        if (v.Value is string s && DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.NoCurrentDateDefault, out var pd))
            return pd;
        if (Vb6Value.TryNumericToDouble(v, out var oa))
        {
            try { return DateTime.FromOADate(oa); }
            catch (ArgumentException) { throw InvalidCall(); }
        }
        throw new VBRunTimeException(VBStandardError.TypeMismatch);
    }

    // 1=Sunday..7=Saturday shifted so the firstDayOfWeek day is 1.
    private static int WeekdayNum(DateTime dt, int fdow) => ((int)dt.DayOfWeek - (fdow - 1) + 7) % 7 + 1;

    // Two-digit years follow the OS/culture window (VB6 reads the same registry setting; .NET's default
    // TwoDigitYearMax matches — e.g. 30->2030, 75->1975). Four-digit years pass through.
    private static Vb6Value DateSerial(int year, int month, int day)
    {
        if (year is >= 0 and <= 99) year = CultureInfo.CurrentCulture.Calendar.ToFourDigitYear(year);
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
        var dfi = CultureInfo.CurrentCulture.DateTimeFormat;
        return new Vb6Value(abbreviate ? dfi.GetAbbreviatedMonthName(m) : dfi.GetMonthName(m));
    }

    private static Vb6Value WeekdayName(int weekday, bool abbreviate, int fdow)
    {
        if (weekday is < 1 or > 7) throw InvalidCall();
        var day = (DayOfWeek)(((fdow - 1) + (weekday - 1)) % 7);
        var dfi = CultureInfo.CurrentCulture.DateTimeFormat;
        return new Vb6Value(abbreviate ? dfi.GetAbbreviatedDayName(day) : dfi.GetDayName(day));
    }
}
