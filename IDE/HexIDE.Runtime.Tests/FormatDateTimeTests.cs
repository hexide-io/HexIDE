using System.Globalization;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Every row measured on vb6.exe SP6, native and p-code, in #2516.
public class FormatDateTimeTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("FormatDateTime(#2026-10-28 14:05:09#)", "28/10/2026 14:05:09", 0)]
    [InlineData("FormatDateTime(#2026-10-28 14:05:09#, 0)", "28/10/2026 14:05:09", 0)]
    [InlineData("FormatDateTime(#2026-10-28 14:05:09#, 1)", "28 October 2026", 0)]
    [InlineData("FormatDateTime(#2026-10-28 14:05:09#, 2)", "28/10/2026", 0)]
    [InlineData("FormatDateTime(#2026-10-28 14:05:09#, 3)", "14:05:09", 0)]
    [InlineData("FormatDateTime(#2026-10-28 14:05:09#, 4)", "14:05", 0)]
    [InlineData("FormatDateTime(#2026-10-28#)", "28/10/2026", 0)]
    [InlineData("FormatDateTime(#2026-10-28#, 0)", "28/10/2026", 0)]
    [InlineData("FormatDateTime(#2026-10-28#, 1)", "28 October 2026", 0)]
    [InlineData("FormatDateTime(#2026-10-28#, 2)", "28/10/2026", 0)]
    [InlineData("FormatDateTime(#2026-10-28#, 3)", "00:00:00", 0)]
    [InlineData("FormatDateTime(#2026-10-28#, 4)", "00:00", 0)]
    [InlineData("FormatDateTime(#14:05:09#)", "14:05:09", 0)]
    [InlineData("FormatDateTime(#14:05:09#, 0)", "14:05:09", 0)]
    [InlineData("FormatDateTime(#14:05:09#, 1)", "30 December 1899", 0)]
    [InlineData("FormatDateTime(#14:05:09#, 2)", "30/12/1899", 0)]
    [InlineData("FormatDateTime(#14:05:09#, 3)", "14:05:09", 0)]
    [InlineData("FormatDateTime(#14:05:09#, 4)", "14:05", 0)]
    [InlineData("FormatDateTime(0)", "00:00:00", 0)]
    [InlineData("FormatDateTime(0, 0)", "00:00:00", 0)]
    [InlineData("FormatDateTime(0, 1)", "30 December 1899", 0)]
    [InlineData("FormatDateTime(0, 2)", "30/12/1899", 0)]
    [InlineData("FormatDateTime(0, 3)", "00:00:00", 0)]
    [InlineData("FormatDateTime(0, 4)", "00:00", 0)]
    [InlineData("FormatDateTime(45000.5)", "15/03/2023 12:00:00", 0)]
    [InlineData("FormatDateTime(45000.5, 0)", "15/03/2023 12:00:00", 0)]
    [InlineData("FormatDateTime(45000.5, 1)", "15 March 2023", 0)]
    [InlineData("FormatDateTime(45000.5, 2)", "15/03/2023", 0)]
    [InlineData("FormatDateTime(45000.5, 3)", "12:00:00", 0)]
    [InlineData("FormatDateTime(45000.5, 4)", "12:00", 0)]
    [InlineData("FormatDateTime(-1.25)", "29/12/1899 06:00:00", 0)]
    [InlineData("FormatDateTime(-1.25, 0)", "29/12/1899 06:00:00", 0)]
    [InlineData("FormatDateTime(-1.25, 1)", "29 December 1899", 0)]
    [InlineData("FormatDateTime(-1.25, 2)", "29/12/1899", 0)]
    [InlineData("FormatDateTime(-1.25, 3)", "06:00:00", 0)]
    [InlineData("FormatDateTime(-1.25, 4)", "06:00", 0)]
    [InlineData("FormatDateTime(\"2026-10-28\")", "28/10/2026", 0)]
    [InlineData("FormatDateTime(\"2026-10-28\", 0)", "28/10/2026", 0)]
    [InlineData("FormatDateTime(\"2026-10-28\", 1)", "28 October 2026", 0)]
    [InlineData("FormatDateTime(\"2026-10-28\", 2)", "28/10/2026", 0)]
    [InlineData("FormatDateTime(\"2026-10-28\", 3)", "00:00:00", 0)]
    [InlineData("FormatDateTime(\"2026-10-28\", 4)", "00:00", 0)]
    [InlineData("FormatDateTime(\"abc\")", "", 13)]
    [InlineData("FormatDateTime(\"abc\", 0)", "", 13)]
    [InlineData("FormatDateTime(\"abc\", 1)", "", 13)]
    [InlineData("FormatDateTime(\"abc\", 2)", "", 13)]
    [InlineData("FormatDateTime(\"abc\", 3)", "", 13)]
    [InlineData("FormatDateTime(\"abc\", 4)", "", 13)]
    [InlineData("FormatDateTime(Null)", "", 0)]
    [InlineData("FormatDateTime(Null, 0)", "", 0)]
    [InlineData("FormatDateTime(Null, 1)", "", 0)]
    [InlineData("FormatDateTime(Null, 2)", "", 0)]
    [InlineData("FormatDateTime(Null, 3)", "", 0)]
    [InlineData("FormatDateTime(Null, 4)", "", 0)]
    [InlineData("FormatDateTime(Empty)", "00:00:00", 0)]
    [InlineData("FormatDateTime(Empty, 0)", "00:00:00", 0)]
    [InlineData("FormatDateTime(Empty, 1)", "30 December 1899", 0)]
    [InlineData("FormatDateTime(Empty, 2)", "30/12/1899", 0)]
    [InlineData("FormatDateTime(Empty, 3)", "00:00:00", 0)]
    [InlineData("FormatDateTime(Empty, 4)", "00:00", 0)]
    [InlineData("FormatDateTime(True)", "29/12/1899", 0)]
    [InlineData("FormatDateTime(True, 0)", "29/12/1899", 0)]
    [InlineData("FormatDateTime(True, 1)", "29 December 1899", 0)]
    [InlineData("FormatDateTime(True, 2)", "29/12/1899", 0)]
    [InlineData("FormatDateTime(True, 3)", "00:00:00", 0)]
    [InlineData("FormatDateTime(True, 4)", "00:00", 0)]
    [InlineData("FormatDateTime(#1899-12-30 00:00:01#)", "00:00:01", 0)]
    [InlineData("FormatDateTime(#1899-12-30 00:00:01#, 0)", "00:00:01", 0)]
    [InlineData("FormatDateTime(#1899-12-30 00:00:01#, 1)", "30 December 1899", 0)]
    [InlineData("FormatDateTime(#1899-12-30 00:00:01#, 2)", "30/12/1899", 0)]
    [InlineData("FormatDateTime(#1899-12-30 00:00:01#, 3)", "00:00:01", 0)]
    [InlineData("FormatDateTime(#1899-12-30 00:00:01#, 4)", "00:00", 0)]
    [InlineData("FormatDateTime(#2026-10-28 14:05:09#, 5)", "", 5)]
    [InlineData("FormatDateTime(#2026-10-28 14:05:09#, -1)", "", 5)]
    [InlineData("FormatDateTime(#2026-10-28 14:05:09#, 2.6)", "14:05:09", 0)]
    [InlineData("FormatDateTime(#2026-10-28 14:05:09#, Null)", "", 94)]
    [InlineData("FormatDateTime(#2026-10-28 14:05:09#, \"1\")", "28 October 2026", 0)]
    [InlineData("FormatDateTime(#2026-10-28 14:05:09#, \"x\")", "", 13)]
    public async Task Measured_row(string expression, string expected, int error)
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("en-GB").Clone();
        // Match the oracle VM's regional setting; ICU's en-GB default includes the weekday.
        culture.DateTimeFormat.LongDatePattern = "d MMMM yyyy";
        using var locale = VbLocale.Use(culture);
        await Run($"On Error Resume Next\nDim result As Variant\nresult = {expression}\nDebug.Print Err.Number\n" +
            (error == 0 ? "Debug.Print result\nDebug.Print TypeName(result)\n" : ""));
        if (error == 0)
            AssertDebugLog([new Vb6Value(0L), new Vb6Value(expected), new Vb6Value("String")]);
        else
            AssertDebugLog([new Vb6Value((long)error)]);
    }
}
