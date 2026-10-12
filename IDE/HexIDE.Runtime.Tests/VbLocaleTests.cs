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
}
