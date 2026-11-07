using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// The locale a running VB6 program formats and reads text in: the user's regional settings, as VB6 takes them
/// (#777). <c>CStr(#1/2/2003#)</c> is <c>02/01/2003</c> under en-GB, measured.
/// </summary>
/// <remarks>
/// <para>
/// It is the interpreter's own, and deliberately not the thread's culture. The IDE pins the thread's culture to the
/// invariant one, so that the IDE's own language cannot change what a program computes
/// (<c>openspec/specs/localization</c>), and the parsing of source literals relies on that pin (#781). Event
/// procedures run on the UI thread, from Avalonia's events, so a culture set at the start of a run would not reach
/// them either.
/// </para>
/// <para>
/// It resolves in three steps:
/// <list type="number">
/// <item>a scope opened with <see cref="Use"/>, which flows with the code that opened it, as the thread's culture
/// does. That is how a test pins a locale without disturbing tests that run beside it;</item>
/// <item>the <see cref="Default"/> the host sets once at startup, from the user's culture before it pins its own;</item>
/// <item>the thread's culture, which is what everything read before this existed, so nothing else moves.</item>
/// </list>
/// </para>
/// <para>
/// A program sees the culture through <see cref="ForProgram"/>: the user's separators, patterns and names, but some
/// things are VB6's own rather than the culture's. A date is always in the Gregorian calendar, and a sign is always an
/// ASCII <c>-</c> or <c>+</c>. Both were decided for writing, in <see cref="VbDisplay"/> (#779), and reading has to
/// agree with writing, or <c>CDate(CStr(d))</c> is a different date. Nonbreaking spaces in date/time
/// patterns and AM/PM designators become ordinary spaces, matching Windows locale data (#2102).
/// </para>
/// <para>
/// What it governs: the text a value becomes (<c>CStr</c>, <c>&amp;</c>, <c>Format</c>), and text a program reads
/// back as a number or a date (<c>CDbl</c>, <c>CDate</c>, <c>IsDate</c>, a String stored into a number). Not
/// <c>Str</c> and <c>Val</c>, which VB6 fixes to a point, and not source literals, which VB6 always writes with one.
/// </para>
/// </remarks>
public static class VbLocale
{
    private static readonly AsyncLocal<CultureInfo?> Scoped = new();

    /// <summary>The cultures <see cref="ForProgram"/> had to change, by the culture they came from.</summary>
    private static readonly ConditionalWeakTable<CultureInfo, CultureInfo> Adapted = new();

    /// <summary>The user's locale, set by the host at startup. Null until a host sets it.</summary>
    public static CultureInfo? Default { get; set; }

    /// <summary>The locale the running program uses now, as <see cref="ForProgram"/> gives it.</summary>
    public static CultureInfo Current => ForProgram(Scoped.Value ?? Default ?? CultureInfo.CurrentCulture);

    /// <summary>
    /// <paramref name="culture"/> as a VB6 program reads and writes it: the same culture, but with the Gregorian
    /// calendar, ASCII signs and ordinary spaces in date/time patterns and AM/PM designators.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The calendar: th-TH counts years in the Buddhist era, fa-IR in the Persian calendar, and ar-SA in Umm al-Qura.
    /// With the culture's own calendar, <c>CDate("02/01/2003")</c> under th-TH is the year 1460 with no error,
    /// <c>DateSerial(30, 1, 1)</c> is 2530, and ar-SA cannot represent the zero date at all, so <c>Format(0, "Short
    /// Date")</c> threw a CLR exception no handler could trap. Each culture offers Gregorian as an optional calendar.
    /// If one ever does not, its date formats are the invariant ones.
    /// </para>
    /// <para>
    /// The signs: ICU puts a bidi mark before the minus sign for 43 cultures, so <c>IsNumeric("-5")</c> was False.
    /// Windows' own locale data, which VB6 reads, does not.
    /// </para>
    /// <para>
    /// A culture already in that shape is returned unchanged, so a user's own overrides, such as a moved two-digit-year
    /// window, reach the program. <c>CultureInfo.Calendar</c> is still the culture's own calendar on the result; read
    /// <c>DateTimeFormat.Calendar</c>.
    /// </para>
    /// </remarks>
    public static CultureInfo ForProgram(CultureInfo culture) =>
        culture.DateTimeFormat.Calendar is GregorianCalendar
        && culture.NumberFormat.NegativeSign == "-" && culture.NumberFormat.PositiveSign == "+"
        && !HasNonbreakingSpaces(culture.DateTimeFormat)
            ? culture
            : Adapted.GetValue(culture, Adapt);

    private static CultureInfo Adapt(CultureInfo culture)
    {
        var adapted = (CultureInfo)culture.Clone();
        if (adapted.DateTimeFormat.Calendar is not GregorianCalendar)
        {
            var gregorian = Array.Find(culture.OptionalCalendars, c => c is GregorianCalendar);
            if (gregorian is null)
                adapted.DateTimeFormat = (DateTimeFormatInfo)CultureInfo.InvariantCulture.DateTimeFormat.Clone();
            else
            {
                var format = adapted.DateTimeFormat;
                format.Calendar = gregorian;
                // A Gregorian date never needs its era, and ar-SA's writes one that reads back as the time of day: its
                // era is U+0645, which is also its PM designator, so a written date came back as noon.
                format.ShortDatePattern = WithoutEra(format.ShortDatePattern);
                format.LongDatePattern = WithoutEra(format.LongDatePattern);
                format.YearMonthPattern = WithoutEra(format.YearMonthPattern);
            }
        }
        var dates = adapted.DateTimeFormat;
        var fullDateTimePattern = dates.FullDateTimePattern;
        dates.ShortDatePattern = OrdinarySpaces(dates.ShortDatePattern);
        dates.LongDatePattern = OrdinarySpaces(dates.LongDatePattern);
        dates.ShortTimePattern = OrdinarySpaces(dates.ShortTimePattern);
        dates.LongTimePattern = OrdinarySpaces(dates.LongTimePattern);
        dates.FullDateTimePattern = OrdinarySpaces(fullDateTimePattern);
        dates.MonthDayPattern = OrdinarySpaces(dates.MonthDayPattern);
        dates.YearMonthPattern = OrdinarySpaces(dates.YearMonthPattern);
        dates.AMDesignator = OrdinarySpaces(dates.AMDesignator);
        dates.PMDesignator = OrdinarySpaces(dates.PMDesignator);
        adapted.NumberFormat.NegativeSign = "-";
        adapted.NumberFormat.PositiveSign = "+";
        return CultureInfo.ReadOnly(adapted);
    }

    private static bool HasNonbreakingSpaces(DateTimeFormatInfo format) =>
        HasNonbreakingSpaces(format.ShortDatePattern) || HasNonbreakingSpaces(format.LongDatePattern)
        || HasNonbreakingSpaces(format.ShortTimePattern) || HasNonbreakingSpaces(format.LongTimePattern)
        || HasNonbreakingSpaces(format.FullDateTimePattern) || HasNonbreakingSpaces(format.MonthDayPattern)
        || HasNonbreakingSpaces(format.YearMonthPattern) || HasNonbreakingSpaces(format.AMDesignator)
        || HasNonbreakingSpaces(format.PMDesignator);

    private static bool HasNonbreakingSpaces(string text) => text.IndexOfAny(['\u202f', '\u00a0']) >= 0;

    private static string OrdinarySpaces(string text) => text.Replace('\u202f', ' ').Replace('\u00a0', ' ');

    /// <summary><paramref name="pattern"/> without its era (<c>g</c> or <c>gg</c>), or the space that set it apart.</summary>
    private static string WithoutEra(string pattern)
    {
        var kept = new System.Text.StringBuilder(pattern.Length);
        char? quote = null;
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (quote is null && c == 'g')
            {
                while (i + 1 < pattern.Length && pattern[i + 1] == 'g')
                    i++;
                if (kept.Length > 0 && kept[^1] == ' ')
                    kept.Length--;
                else if (i + 1 < pattern.Length && pattern[i + 1] == ' ')
                    i++;
                continue;
            }
            if (c is '\'' or '"')
                quote = quote == c ? null : quote ?? c;
            kept.Append(c);
        }
        return kept.ToString();
    }

    /// <summary>
    /// Runs the code that follows in <paramref name="culture"/> until the result is disposed. It flows into awaited
    /// work started from here, and does not reach code running beside it.
    /// </summary>
    public static IDisposable Use(CultureInfo culture)
    {
        var previous = Scoped.Value;
        Scoped.Value = culture;
        return new Restore(previous);
    }

    private sealed class Restore(CultureInfo? previous) : IDisposable
    {
        public void Dispose() => Scoped.Value = previous;
    }
}
