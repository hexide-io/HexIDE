using System;
using System.Globalization;
using System.Threading.Tasks;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A running program formats and reads text in its own locale, the user's regional settings, and not in the
/// thread's culture, which the IDE pins to the invariant one (#777). Expectations marked measured are vb6.exe output
/// under en-GB (<c>docs/vb6-fidelity-oracle.md</c>); the comma-decimal ones are the round trip, which is the property
/// the change exists for, and VB6's own text there is documented rather than measured.
/// </summary>
public class VbLocaleTests : BaseVBTestFixture
{
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");
    private static readonly CultureInfo DeDe = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Runs <paramref name="code"/> with the thread in one culture and the program in another.</summary>
    private async Task RunWith(CultureInfo program, CultureInfo thread, string code)
    {
        var saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = thread;
        try
        {
            using (VbLocale.Use(program))
                await Run(code);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    /// <summary>As <see cref="RunWith"/>, but a program that fails says <paramref name="shown"/> as well.</summary>
    private async Task RunShowing(string shown, CultureInfo program, CultureInfo thread, string code)
    {
        try
        {
            await RunWith(program, thread, code);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"{e.Message} ({shown})", e);
        }
    }

    /// <summary>Each character of <paramref name="text"/> as itself if it is printable ASCII, else as U+XXXX.</summary>
    private static string CodePoints(string text) => string.Join(" ",
        text.Select(c => c is >= ' ' and <= '~' && c != '{' && c != '}' ? c.ToString() : $"U+{(int)c:X4}"));

    // ── the program's locale, not the thread's culture ─────────────────────────────────────────────

    [Fact]
    public async Task A_date_is_written_in_the_programs_locale_while_the_thread_is_invariant()
    {
        // The IDE's case: its threads are invariant, and the user is in en-GB. Measured: 02/01/2003.
        await RunWith(EnGb, CultureInfo.InvariantCulture, "Debug.Print CStr(#1/2/2003#)\n");

        AssertDebugLog([new Vb6Value("02/01/2003")]);
    }

    [Fact]
    public async Task And_the_thread_culture_does_not_leak_in_the_other_way()
    {
        await RunWith(CultureInfo.InvariantCulture, EnGb, "Debug.Print CStr(#1/2/2003#)\n");

        AssertDebugLog([new Vb6Value("01/02/2003")]);
    }

    // ── reading text back, measured under en-GB ────────────────────────────────────────────────────

    [Theory]
    [InlineData("CDate(\"13/1/2003\")", 2003, 1, 13)]
    [InlineData("CDate(CStr(#1/2/2003#))", 2003, 1, 2)]
    [InlineData("CDate(\"1/1/50\")", 1950, 1, 1)]
    [InlineData("CDate(\"1/1/49\")", 2049, 1, 1)]
    [InlineData("DateSerial(29, 1, 1)", 2029, 1, 1)]
    [InlineData("DateSerial(30, 1, 1)", 2030, 1, 1)]      // not 1930: the window is regional, not 0-29
    [InlineData("DateSerial(49, 1, 1)", 2049, 1, 1)]
    [InlineData("DateSerial(50, 1, 1)", 1950, 1, 1)]
    public async Task A_date_read_from_text_or_a_two_digit_year_follows_the_locale(string expression, int year, int month, int day)
    {
        await RunWith(EnGb, CultureInfo.InvariantCulture, $"Debug.Print {expression}\n");

        AssertDebugLog([new Vb6Value(new DateTime(year, month, day))]);
    }

    [Theory]
    [InlineData("DateSerial(30, 1, 1)")]
    [InlineData("CDate(\"1/1/30\")")]
    public async Task The_two_digit_year_window_is_the_locales_own(string expression)
    {
        // Windows lets a user move the window, and VB6 follows it. A locale whose window ends in 2029 puts 30 in 1930,
        // which no thread culture here would.
        var narrow = (CultureInfo)EnGb.Clone();
        narrow.DateTimeFormat.Calendar.TwoDigitYearMax = 2029;

        await RunWith(narrow, CultureInfo.InvariantCulture, $"Debug.Print {expression}\n");

        AssertDebugLog([new Vb6Value(new DateTime(1930, 1, 1))]);
    }

    [Theory]
    [InlineData("IsDate(\"13/1/2003\")")]
    [InlineData("IsNumeric(\"1,5\")")]
    public async Task What_the_locale_reads_as_a_date_or_a_number_is_one(string expression)
    {
        await RunWith(EnGb, CultureInfo.InvariantCulture, $"Debug.Print {expression}\n");

        AssertDebugLog([new Vb6Value(true)]);
    }

    // ── round trips in a comma-decimal locale ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("CStr(1.5)", "1,5")]
    [InlineData("CStr(CDbl(\"1,5\"))", "1,5")]
    [InlineData("CStr(CDbl(CStr(0.25)))", "0,25")]
    [InlineData("CStr(CDate(CStr(#1/2/2003#)))", "02.01.2003")]
    public async Task Text_a_program_wrote_reads_back_as_the_same_value(string expression, string expected)
    {
        await RunWith(DeDe, CultureInfo.InvariantCulture, $"Debug.Print {expression}\n");

        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("Format(1.5, \"0.00\")", "1,50")]
    [InlineData("Format(#1/2/2003#, \"Short Date\")", "02.01.2003")]
    [InlineData("MonthName(1)", "Januar")]
    [InlineData("WeekdayName(1)", "Sonntag")]
    public async Task Format_and_the_names_of_months_and_days_are_the_locales(string expression, string expected)
    {
        // VB6 takes these from the regional settings (documented; the oracle machine is en-GB, so not measured).
        await RunWith(DeDe, CultureInfo.InvariantCulture, $"Debug.Print {expression}\n");

        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Fact]
    public async Task A_date_function_given_a_String_reads_it_in_the_locale()
    {
        await RunWith(EnGb, CultureInfo.InvariantCulture, "Debug.Print CStr(Day(\"13/1/2003\"))\n");

        AssertDebugLog([new Vb6Value("13")]);
    }

    [Theory]
    [InlineData("CBool(\"0,5\")")]          // CBool's number
    [InlineData("IsNumeric(\"1.000,5\")")]  // a German number, which the invariant reading refuses
    public async Task A_number_in_text_is_read_with_the_locales_separators(string expression)
    {
        // Printed as a Boolean, not through CStr: VB6's text for a Boolean under a German locale is not measured.
        await RunWith(DeDe, CultureInfo.InvariantCulture, $"Debug.Print {expression}\n");

        AssertDebugLog([new Vb6Value(true)]);
    }

    [Theory]
    [InlineData("Str(1.5)", " 1.5")]
    [InlineData("CStr(Val(\"1.5\"))", "1,5")]
    public async Task Str_and_Val_keep_the_point_in_every_locale(string expression, string expected)
    {
        // VB6 documents both as always using a period. Val reads "1.5" as one and a half, and the result is then
        // written in the locale.
        await RunWith(DeDe, CultureInfo.InvariantCulture, $"Debug.Print {expression}\n");

        AssertDebugLog([new Vb6Value(expected)]);
    }

    // ── what the program takes from the locale, and what stays VB6's own ───────────────────────────

    /// <summary>A bidi mark before the minus sign, as ICU writes it for 43 cultures.</summary>
    private static readonly string BidiMinus = char.ConvertFromUtf32(0x200E) + "-";

    [Theory]
    [InlineData("th-TH", false)]   // the Buddhist era: 2003 is 2546, and 30 would be 2530
    [InlineData("fa-IR", true)]    // the Persian calendar: 2003 is 1381, and January is not its first month
    [InlineData("ar-SA", true)]    // Umm al-Qura, which cannot reach the zero date at all
    public async Task A_date_is_Gregorian_whatever_the_locales_own_calendar(string name, bool monthsDiffer)
    {
        // CStr writes a Gregorian date (#779), so everything that reads one, or names a month, must be Gregorian too.
        var culture = CultureInfo.GetCultureInfo(name);
        // What CStr writes, and the patterns it writes with, spelled out by code point: the ICU data differs between
        // platforms (macOS's could not read its own ar-SA date back), so a failure on CI must show what was written.
        var program = VbLocale.ForProgram(culture).DateTimeFormat;
        var written = VbDisplay.ToText(new Vb6Value(new DateTime(2003, 1, 2)), culture);
        var shown = $"CStr wrote {CodePoints(written)}; short date {CodePoints(program.ShortDatePattern)}, "
                    + $"long time {CodePoints(program.LongTimePattern)}, AM {CodePoints(program.AMDesignator)}, "
                    + $"PM {CodePoints(program.PMDesignator)}, calendar {program.Calendar.GetType().Name}";

        await RunShowing(shown, culture, CultureInfo.InvariantCulture,
            "Debug.Print CDate(CStr(#1/2/2003#))\n" +
            "Debug.Print DateSerial(30, 1, 1)\n" +
            "Debug.Print IsDate(CStr(#1/2/2003#))\n" +
            "Debug.Print CStr(Year(CStr(#1/2/2003#)))\n" +
            "Debug.Print Format(#1/2/2003#, \"yyyy\")\n" +
            "Debug.Print InStr(Format(0, \"Short Date\"), \"1899\") > 0\n" +
            "Debug.Print InStr(Format(#1/2/2003#, \"Long Date\"), \"2003\") > 0\n" +
            "Debug.Print MonthName(1) = Format(#1/2/2003#, \"mmmm\")\n" +
            "Debug.Print MonthName(1)\n");

        debug.Take(8).Should().Equal([
            new Vb6Value(new DateTime(2003, 1, 2)),
            new Vb6Value(new DateTime(2030, 1, 1)),
            new Vb6Value(true),
            new Vb6Value("2003"),
            new Vb6Value("2003"),
            new Vb6Value(true),
            new Vb6Value(true),
            new Vb6Value(true)], shown);
        if (monthsDiffer)
            debug[8].Value.Should().NotBe(culture.DateTimeFormat.GetMonthName(1), "that is the first month of the locale's own calendar");
    }

    [Fact]
    public void Every_culture_reads_back_the_dates_CStr_writes_in_it()
    {
        // The property the locale exists for, over every culture this platform's ICU knows rather than a chosen few:
        // macOS's data differs from Windows' and Linux's, and it was there that ar-SA could not read back its own date.
        DateTime[] dates =
        [
            new(2003, 1, 2), new(2003, 12, 31, 23, 59, 59), new(2003, 1, 2, 10, 30, 0), new(1899, 12, 30, 10, 30, 0),
            new(1999, 2, 28, 0, 0, 1), new(2049, 6, 15, 12, 0, 0),
        ];
        var failures = new List<string>();
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            using var _ = VbLocale.Use(culture);
            foreach (var date in dates)
            {
                var written = VbDisplay.ToText(new Vb6Value(date));
                if (!VB6BuiltIns.TryReadDate(written, out var read) || VB6BuiltIns.OnZeroDateIfTimeOnly(read) != date)
                    failures.Add($"{culture.Name}: {date:s} wrote {CodePoints(written)}");
            }
        }

        failures.Should().BeEmpty();
    }

    [Fact]
    public async Task A_sign_is_an_ASCII_minus_however_the_locale_writes_its_own()
    {
        var bidi = (CultureInfo)DeDe.Clone();
        bidi.NumberFormat.NegativeSign = BidiMinus;

        await RunWith(bidi, CultureInfo.InvariantCulture,
            "Debug.Print IsNumeric(\"-5\")\n" +
            "Debug.Print CDbl(CStr(-1.5))\n" +
            "Debug.Print CBool(\"-1\")\n" +
            "Debug.Print CStr(-1.5)\n" +
            "Debug.Print Format(-1.5, \"0.0\")\n");

        AssertDebugLog([new Vb6Value(true), new Vb6Value(-1.5), new Vb6Value(true), new Vb6Value("-1,5"), new Vb6Value("-1,5")]);
    }

    [Theory]
    [InlineData("ar-SA")]
    [InlineData("fa-IR")]
    [InlineData("he-IL")]
    public async Task A_negative_number_reads_back_in_a_locale_whose_own_minus_carries_a_bidi_mark(string name)
    {
        await RunWith(CultureInfo.GetCultureInfo(name), CultureInfo.InvariantCulture,
            "Debug.Print IsNumeric(\"-5\")\n" +
            "Debug.Print CDbl(CStr(-1.5)) = -1.5\n" +
            "Debug.Print CBool(\"-1\")\n" +
            "Debug.Print Left(CStr(-1.5), 1) = \"-\"\n" +
            "Debug.Print Left(Format(-1.5, \"0.0\"), 1) = \"-\"\n");

        AssertDebugLog([new Vb6Value(true), new Vb6Value(true), new Vb6Value(true), new Vb6Value(true), new Vb6Value(true)]);
    }

    // ── a date impossible in the locale's order is read the other way round (#793) ───────────────────

    [Theory]
    [InlineData("CDate(\"1/13/2003\")", 2003, 1, 13, 0)]
    [InlineData("CDate(\"1/13/2003 10:30\")", 2003, 1, 13, 10)]
    [InlineData("CDate(\"1-13-2003\")", 2003, 1, 13, 0)]
    [InlineData("CDate(\"1 13 2003\")", 2003, 1, 13, 0)]
    [InlineData("CDate(\"1/13-2003\")", 2003, 1, 13, 0)]
    [InlineData("CDate(\"1/13/30\")", 2030, 1, 13, 0)]
    [InlineData("CDate(\"1/13/12\")", 2012, 1, 13, 0)]
    [InlineData("DateValue(\"1/13/2003\")", 2003, 1, 13, 0)]
    [InlineData("CDate(\"12/1/2003\")", 2003, 1, 12, 0)]   // possible in the locale's order, so read in it
    [InlineData("CDate(\"13/1/2003\")", 2003, 1, 13, 0)]
    public async Task A_date_impossible_in_the_locales_order_is_read_the_other_way_round(string expression, int year, int month, int day, int hour)
    {
        // Measured under en-GB, day first (oracle, "A date the locale's order cannot make").
        await RunWith(EnGb, CultureInfo.InvariantCulture, $"Debug.Print {expression}\n");

        AssertDebugLog([new Vb6Value(new DateTime(year, month, day, hour, hour == 0 ? 0 : 30, 0))]);
    }

    [Theory]
    [InlineData("CDate(\"1/13\") = DateSerial(Year(Date), 1, 13)")]   // two parts: this year
    [InlineData("CDate(\"13/1\") = DateSerial(Year(Date), 1, 13)")]
    [InlineData("CDate(\"1/12\") = DateSerial(Year(Date), 12, 1)")]   // possible as it stands: 1 December
    [InlineData("IsDate(\"1/13/2003\")")]
    [InlineData("CStr(Day(\"1/13/2003\")) = \"13\"")]
    [InlineData("CStr(Month(\"1/13/2003\")) = \"1\"")]
    public async Task The_other_order_reaches_every_reader_of_a_date(string expression)
    {
        await RunWith(EnGb, CultureInfo.InvariantCulture, $"Debug.Print {expression}\n");

        AssertDebugLog([new Vb6Value(true)]);
    }

    [Theory]
    [InlineData("CDate(\"31/2/2003\")")]    // impossible both ways round
    [InlineData("CDate(\"2/31/2003\")")]
    [InlineData("CDate(\"30/2/2003\")")]
    [InlineData("CDate(\"13/13/2003\")")]
    [InlineData("CDate(\"0/13/2003\")")]
    [InlineData("CDate(\"32/1/2003\")")]
    [InlineData("CDate(\"2003/13/1\")")]    // a year first is not swapped
    [InlineData("CDate(\"13/2003\")")]      // nor a month and a year
    [InlineData("CDate(\"1.13.2003\")")]    // and a dot is not a date separator here at all
    public async Task A_date_impossible_either_way_round_is_still_Type_mismatch(string expression)
    {
        await RunWith(EnGb, CultureInfo.InvariantCulture,
            $"On Error Resume Next\nDim d\nd = {expression}\nDebug.Print Err.Number\n");

        AssertDebugLog([new Vb6Value(13L)]);
    }

    [Theory]
    [InlineData("IsDate(\"13/13/2003\")")]
    [InlineData("IsDate(\"31/2/2003\")")]
    public async Task IsDate_is_False_for_a_date_impossible_either_way_round(string expression)
    {
        await RunWith(EnGb, CultureInfo.InvariantCulture, $"Debug.Print {expression}\n");

        AssertDebugLog([new Vb6Value(false)]);
    }

    // ── each place Format reads the locale, told apart from the thread's culture ─────────────────────

    [Theory]
    [InlineData("Format(CCur(1.5))", "1,5")]                                           // General Number of an exact value
    [InlineData("Format(#1/2/2003#, \"dddd d mmmm\")", "Donnerstag 2 Januar")]        // a date mask's names
    [InlineData("Format(\"1,5\", \"0.00\")", "1,50")]                                  // a String read as a number
    [InlineData("Format(1.5E+30, \"General Number\")", "1,5E+30")]                     // beyond the decimal engine
    [InlineData("Format(1234.5, \"#,##0.00\")", "1.234,50")]                           // grouping
    [InlineData("Format(#1/2/2003#, \"Long Date\")", "Donnerstag, 2. Januar 2003")]  // a named date format
    public async Task Each_Format_site_reads_the_programs_locale(string expression, string expected)
    {
        await RunWith(DeDe, CultureInfo.InvariantCulture, $"Debug.Print {expression}\n");

        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Fact]
    public async Task Formats_Currency_takes_the_locales_symbol()
    {
        // Where VB6 puts the symbol under de-DE is not measured, so only its presence is pinned.
        await RunWith(DeDe, CultureInfo.InvariantCulture, "Debug.Print InStr(Format(1.5, \"Currency\"), \"€\") > 0\n");

        AssertDebugLog([new Vb6Value(true)]);
    }

    [Fact]
    public async Task Formats_ttttt_is_the_locales_long_time()
    {
        // A pattern of its own, because de-DE's long time is the invariant one and could not tell the two apart.
        var dotted = (CultureInfo)DeDe.Clone();
        dotted.DateTimeFormat.LongTimePattern = "HH.mm.ss";

        await RunWith(dotted, CultureInfo.InvariantCulture, "Debug.Print Format(#10:30:00 PM#, \"ttttt\")\n");

        AssertDebugLog([new Vb6Value("22.30.00")]);
    }

    [Fact]
    public async Task Formats_scientific_sign_is_ASCII_and_not_the_threads()
    {
        // The thread carries the bidi minus, so a site that read the thread's culture would write it. The value is
        // computed, not written as -1.5: the source's literals are read in the thread's culture (#781), which here
        // would not read its own minus sign.
        var bidiThread = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        bidiThread.NumberFormat.NegativeSign = BidiMinus;

        await RunWith(DeDe, bidiThread, "Debug.Print Format(0 - 1.5, \"0.00E+00\")\n");

        AssertDebugLog([new Vb6Value("-1,50E+00")]);
    }
}
