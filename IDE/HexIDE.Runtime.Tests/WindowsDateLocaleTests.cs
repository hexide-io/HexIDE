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
        WindowsDateLocale.Apply(dates, type => $"'field{type:X}'");

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
        => WindowsDateLocale.ManagedPattern(nls).Should().Be(managed);

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
