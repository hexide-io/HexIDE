using System.Globalization;
using System.Threading.Tasks;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class FormatAmPmTests : BaseVBTestFixture
{
    // Measured on vb6.exe SP6, native and p-code (#2040's final measurement comment).
    [Theory]
    [InlineData(0, 0, 1, "hh:nn:ss AMPM", "12:00:01 AM")]
    [InlineData(9, 5, 3, "hh:nn:ss AMPM", "09:05:03 AM")]
    [InlineData(9, 5, 3, "h AMPM", "9 AM")]
    [InlineData(12, 0, 1, "hh:nn:ss AMPM", "12:00:01 PM")]
    [InlineData(13, 5, 3, "h:nn:ss AMPM", "1:05:03 PM")]
    [InlineData(13, 5, 3, "hh AMPM", "01 PM")]
    [InlineData(13, 5, 3, "ampm", "PM")]
    [InlineData(13, 5, 3, "AmPm", "PM")]
    [InlineData(13, 5, 3, "aMpM", "PM")]
    [InlineData(23, 59, 59, "h:nn AMPM", "11:59 PM")]
    public async Task AmPm_uses_the_designator_and_twelve_hour_clock(int hour, int minute, int second, string mask, string expected)
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("en-GB").Clone();
        culture.DateTimeFormat.AMDesignator = "AM";
        culture.DateTimeFormat.PMDesignator = "PM";
        using var locale = VbLocale.Use(culture);
        await Run($"Debug.Print Format$(TimeSerial({hour}, {minute}, {second}), \"{mask}\")\n" +
                  $"Debug.Print Format(TimeSerial({hour}, {minute}, {second}), \"{mask}\")\n");
        AssertDebugLog([new Vb6Value(expected), new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("xAM", "yPM", "09:05:03 xAM", "9 xAM", "yPM")]
    [InlineData("", "", "09:05:03 ", "9 ", "")]
    public async Task AmPm_reads_user_overrides_even_when_empty(string am, string pm, string morning, string hour, string afternoon)
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("de-DE").Clone();
        culture.DateTimeFormat.AMDesignator = am;
        culture.DateTimeFormat.PMDesignator = pm;
        using var locale = VbLocale.Use(culture);
        await Run("Debug.Print Format$(TimeSerial(9, 5, 3), \"hh:nn:ss AMPM\")\n" +
                  "Debug.Print Format$(TimeSerial(9, 5, 3), \"h AMPM\")\n" +
                  "Debug.Print Format$(TimeSerial(13, 5, 3), \"ampm\")\n" +
                  "Debug.Print Format$(TimeSerial(13, 5, 3), \"h AM/PM\")\n");
        AssertDebugLog([new Vb6Value(morning), new Vb6Value(hour), new Vb6Value(afternoon), new Vb6Value("1 PM")]);
    }
}
