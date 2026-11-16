using System.Globalization;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class WindowsDateTableTests : BaseVBTestFixture
{
    // Windows GetDateFormatEx measurements on build 10.0.26100, issue #3928 (not vb6.exe measurements).
    [Theory]
    [InlineData("en-GB", "31 December 2000", "31/12/2000")]
    [InlineData("es-US", "domingo, diciembre 31, 2000", "12/31/2000")]
    [InlineData("ko-KR", "2000년 12월 31일 일요일", "2000-12-31")]
    [InlineData("vi-VN", "31 Tháng Mười Hai 2000", "31/12/2000")]
    [InlineData("nl-NL", "zondag 31 december 2000", "31-12-2000")]
    public async Task Named_dates_use_Windows_data(string name, string longDate, string shortDate)
    {
        using var locale = VbLocale.Use(new CultureInfo(name, false));
        await Run("Debug.Print Format$(#12/31/2000#, \"Long Date\")\n" +
                  "Debug.Print Format$(#12/31/2000#, \"Short Date\")\n");
        // The generated Vietnamese names carry decomposed accents; compare canonical Unicode text.
        debug.Should().HaveCount(2);
        ((string)debug[0].Value!).Normalize().Should().Be(longDate.Normalize());
        ((string)debug[1].Value!).Normalize().Should().Be(shortDate.Normalize());
    }

    [Fact]
    public async Task Es_US_short_date_round_trips()
    {
        using var locale = VbLocale.Use(new CultureInfo("es-US", false));
        await Run("Debug.Print CDate(Format$(#12/31/2000#, \"Short Date\")) = #12/31/2000#\n");
        AssertDebugLog([new Vb6Value(true)]);
    }

    [Fact]
    public void Table_records_its_build_and_complete_name_arrays()
    {
        WindowsDateTable.Data.GetProperty("windowsBuild").GetString().Should().NotBeNullOrEmpty();
        var entries = WindowsDateTable.Data.GetProperty("locales").EnumerateObject().ToArray();
        entries.Should().NotBeEmpty();
        foreach (var entry in entries)
        {
            foreach (var field in new[] { "months", "abbreviatedMonths" })
                entry.Value.GetProperty(field).GetArrayLength().Should().Be(12, entry.Name);
            foreach (var field in new[] { "days", "abbreviatedDays" })
                entry.Value.GetProperty(field).GetArrayLength().Should().Be(7, entry.Name);
            for (uint i = 0; i < 12; i++)
            {
                WindowsDateTable.Read(entry.Value, 0x10000038 + i).Should().Be(
                    entry.Value.GetProperty(entry.Value.TryGetProperty("genitiveMonths", out _) ? "genitiveMonths" : "months")[(int)i].GetString());
                WindowsDateTable.Read(entry.Value, 0x10000044 + i).Should().Be(
                    entry.Value.GetProperty(entry.Value.TryGetProperty("abbreviatedGenitiveMonths", out _) ? "abbreviatedGenitiveMonths" : "abbreviatedMonths")[(int)i].GetString());
            }
        }
    }

    [Fact]
    public void Table_era_uses_the_shared_literal_mapping()
    {
        // The table owns this Windows measurement; #3946 owns the deliberate VB6 divergence.
        var culture = new CultureInfo("byn-ER", false);
        WindowsDateTable.Apply(culture);
        var era = WindowsDateTable.Data.GetProperty("locales").GetProperty("byn-ER").GetProperty("era").GetString();
        new DateTime(2000, 12, 31).ToString("D", culture).Should().EndWith(era).And.NotContain("gg");
    }

    [Fact]
    public void Missing_and_non_Gregorian_entries_do_not_apply()
    {
        var absent = CultureInfo.GetCultureInfo("en");
        WindowsDateTable.AppliesTo(absent).Should().BeFalse();
        var unchanged = (CultureInfo)absent.Clone();
        WindowsDateTable.Apply(unchanged);
        unchanged.DateTimeFormat.ShortDatePattern.Should().Be(absent.DateTimeFormat.ShortDatePattern);
        unchanged.DateTimeFormat.LongDatePattern.Should().Be(absent.DateTimeFormat.LongDatePattern);
        // Windows uses live NLS even for locales absent from the portable table.
        if (!OperatingSystem.IsWindows())
        {
            var retained = VbLocale.ForProgram(absent).DateTimeFormat;
            retained.ShortDatePattern.Should().Be(absent.DateTimeFormat.ShortDatePattern.Replace('\u00a0', ' ').Replace('\u202f', ' '));
            retained.LongDatePattern.Should().Be(absent.DateTimeFormat.LongDatePattern.Replace('\u00a0', ' ').Replace('\u202f', ' '));
            retained.MonthNames.Should().Equal(absent.DateTimeFormat.MonthNames);
            retained.DayNames.Should().Equal(absent.DateTimeFormat.DayNames);
        }
        var arabic = CultureInfo.GetCultureInfo("ar-SA");
        WindowsDateTable.Data.GetProperty("locales").GetProperty("ar-SA").GetProperty("calendar").GetString().Should().Be("23");
        WindowsDateTable.AppliesTo(arabic).Should().BeFalse();
        var expected = (CultureInfo)arabic.Clone();
        expected.DateTimeFormat.Calendar = new GregorianCalendar();
        // Existing Gregorian/sign/space adaptation remains; the Windows table must not replace ICU's names.
        VbLocale.ForProgram(arabic).DateTimeFormat.MonthNames.Should().Equal(expected.DateTimeFormat.MonthNames);
    }
}
