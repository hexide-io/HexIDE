using System.Globalization;
using System.Runtime.InteropServices;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public partial class WindowsDateLocaleTests : BaseVBTestFixture
{
    [Fact]
    public void ForProgram_reads_the_calendar_once_per_culture()
    {
        var reads = 0;
        var calendars = new WindowsDateLocale.CalendarCache((name, type) =>
        {
            name.Should().Be("en-GB");
            type.Should().Be(0x80001009);
            reads++;
            return "23"; // A non-Gregorian fixture keeps this test independent of native date data.
        });
        var culture = CultureInfo.GetCultureInfo("en-GB");
        for (var i = 0; i < 100; i++)
            VbLocale.ForProgram(culture, calendars.AppliesTo);
        reads.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Only_a_culture_carrying_user_overrides_reads_user_settings(bool useUserOverride)
    {
        var culture = new CultureInfo("en-GB", useUserOverride);
        var reads = new List<uint>();
        WindowsDateLocale.Apply(culture, "en-gb", (name, type) =>
        {
            name.Should().Be("en-GB");
            reads.Add(type);
            return "'fixture'";
        });
        reads.Should().NotBeEmpty();
        reads.Should().OnlyContain(type => (type & 0x80000000u) == (useUserOverride ? 0u : 0x80000000u));
    }

    [Fact]
    public void Nls_fields_map_to_the_date_format_without_changing_time_or_calendar()
    {
        var dates = (DateTimeFormatInfo)CultureInfo.InvariantCulture.DateTimeFormat.Clone();
        var calendar = dates.Calendar;
        var time = dates.LongTimePattern;
        WindowsDateLocale.Apply(dates, type => $"'field{type:X}'", () => throw new InvalidOperationException());

        dates.ShortDatePattern.Should().Be("'field1F'");
        dates.LongDatePattern.Should().Be("'field20'");
        dates.DateSeparator.Should().Be("'field1D'");
        for (var i = 0; i < 12; i++)
        {
            dates.MonthNames[i].Should().Be($"'field{0x38 + i:X}'");
            dates.AbbreviatedMonthNames[i].Should().Be($"'field{0x44 + i:X}'");
            dates.MonthGenitiveNames[i].Should().Be($"'field{0x10000038 + i:X}'");
            dates.AbbreviatedMonthGenitiveNames[i].Should().Be($"'field{0x10000044 + i:X}'");
        }
        dates.MonthNames[12].Should().BeEmpty();
        for (var i = 0; i < 7; i++)
        {
            dates.DayNames[(i + 1) % 7].Should().Be($"'field{0x2a + i:X}'");
            dates.AbbreviatedDayNames[(i + 1) % 7].Should().Be($"'field{0x31 + i:X}'");
        }
        dates.Calendar.Should().BeSameAs(calendar);
        dates.LongTimePattern.Should().Be(time);
    }

    [Theory]
    [InlineData("dd 'day''s' MMMM yyyy", "dd 'day\\'s' MMMM yyyy")]
    [InlineData("yyyy\\MM\\dd", "yyyy\\\\MM\\\\dd")]
    public void Nls_pattern_literals_survive_managed_formatting(string nls, string managed)
        => WindowsDateLocale.ManagedPattern(nls, () => throw new InvalidOperationException()).Should().Be(managed);

    [Theory]
    [InlineData("yyyy gg", "era", "yyyy 'era'")]
    [InlineData("g yyyy", "era", "'era' yyyy")]
    [InlineData("yyyy 'gg' gg", "era", "yyyy 'gg' 'era'")]
    [InlineData("yyyy gg", "era's name", "yyyy 'era\\'s name'")]
    public void Nls_era_runs_become_escaped_literals(string nls, string era, string managed)
    {
        // #3946 requests Windows' text, including spaces and escaped apostrophes.
        WindowsDateLocale.ManagedPattern(nls, () => era).Should().Be(managed);
    }

    [Fact]
    public void Apply_reads_the_era_once_for_both_patterns()
    {
        var dates = (DateTimeFormatInfo)CultureInfo.InvariantCulture.DateTimeFormat.Clone();
        var reads = 0;
        WindowsDateLocale.Apply(dates, type => type is 0x1f or 0x20 ? "yyyy gg g" : "fixture", () =>
        {
            reads++;
            return "era";
        });
        reads.Should().Be(1);
        dates.ShortDatePattern.Should().Be("yyyy 'era' 'era'");
        dates.LongDatePattern.Should().Be("yyyy 'era' 'era'");
    }

    [Fact]
    public void Writes_the_era_where_vb6_writes_gg()
    {
        // Deliberate divergence: vb6.exe writes literal gg (#3946, measuring PR #3979,
        // docs/vb6-fidelity-oracle/a-long-date-pattern-with-an-era.md). Do not change to match VB6.
        var dates = (DateTimeFormatInfo)CultureInfo.InvariantCulture.DateTimeFormat.Clone();
        WindowsDateLocale.Apply(dates, type => type is 0x1f or 0x20 ? "yyyy gg" : "fixture", () => "ኣድ");
        var text = new DateTime(2000, 12, 31).ToString("D", dates);
        text.Should().EndWith("ኣድ");
        text.Should().NotContain("gg");
    }

    [WindowsOnlyFact]
    public async Task Era_date_output_matches_GetDateFormatEx_and_reads_without_a_wrong_date()
    {
        // #3946: patterns vary by Windows version, so discover them from this host's NLS.
        foreach (var available in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            var culture = CultureInfo.GetCultureInfo(available.Name);
            if (!WindowsDateLocale.AppliesTo(culture))
                continue;
            var shortPattern = NativePattern(culture.Name, 0x1f);
            var longPattern = NativePattern(culture.Name, 0x20);
            if (!HasEra(shortPattern) && !HasEra(longPattern)
                && culture.Name is not ("byn-ER" or "tig-ER" or "wal-ET"))
                continue;
            var date = new SystemTime { Year = 2000, Month = 12, Day = 31 };
            var adapted = VbLocale.ForProgram(culture);
            foreach (var (flags, format) in new[] { (1u, "d"), (2u, "D") })
                new DateTime(2000, 12, 31).ToString(format, adapted)
                    .Should().Be(NativeDate(culture.Name, flags | 0x80000000, date, null), culture.Name);
            if (culture.Name == "byn-ER")
            {
                // The deliberate divergence above must also hold through the native reader and cache.
                new DateTime(2000, 12, 31).ToString("D", adapted)
                    .Should().EndWith(NativeDate(culture.Name, 0, date, "gg")).And.NotContain("gg");
            }
            using var locale = VbLocale.Use(culture);
            debug.Clear();
            await Run("On Error Resume Next\nDim d\nd = CDate(Format$(#12/31/2000#, \"Long Date\"))\n" +
                "If Err.Number = 0 Then\nDebug.Print d = #12/31/2000#\nElse\nDebug.Print Err.Number = 13\nEnd If\n");
            // #3946 permits either error 13 (measured VB6) or a correct round trip; never another date.
            AssertDebugLog([new Vb6Value(true)]);
        }
    }

    private static bool HasEra(string pattern) =>
        System.Text.RegularExpressions.Regex.Replace(pattern, "'([^']|'')*'", "").Contains('g');

    private static string NativePattern(string locale, uint type)
    {
        var output = new char[256];
        var length = GetLocaleInfoEx(locale, type | 0x80000000, output, output.Length);
        length.Should().BeGreaterThan(0, locale);
        return new string(output, 0, length - 1);
    }

    private static string NativeDate(string locale, uint flags, SystemTime date, string? format)
    {
        var output = new char[256];
        var length = GetDateFormatEx(locale, flags, in date, format, output, output.Length, null);
        length.Should().BeGreaterThan(0, locale);
        return new string(output, 0, length - 1);
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetLocaleInfoEx(string locale, uint type,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] char[] output, int count);

    [WindowsOnlyFact]
    public async Task Date_output_matches_GetDateFormatEx()
    {
        // Locale/date pairs measured in #2041; the expected text comes directly from NLS.
        foreach (var (name, flags) in new (string, uint)[]
                 { ("es-US", 1), ("nl-NL", 1), ("ko-KR", 1), ("vi-VN", 2), ("ru-RU", 2) })
        {
            using var locale = VbLocale.Use(CultureInfo.GetCultureInfo(name));
            var date = new SystemTime { Year = 2000, Month = 12, Day = 31 };
            var output = new char[256];
            var length = GetDateFormatEx(name, flags | 0x80000000, in date, null, output, output.Length, null);
            length.Should().BeGreaterThan(0, name);
            var expected = new string(output, 0, length - 1);
            debug.Clear();
            await Run($"Debug.Print Format$(#12/31/2000#, \"{(flags == 1 ? "Short" : "Long")} Date\")\n");
            AssertDebugLog([new Vb6Value(expected)]);
            if (flags == 1)
            {
                debug.Clear();
                await Run("Debug.Print CStr(#12/31/2000#)\n");
                AssertDebugLog([new Vb6Value(expected)]);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
    {
        public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetDateFormatEx(string locale, uint flags, in SystemTime date, string? format,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 5)] char[] output, int count, string? calendar);
}
