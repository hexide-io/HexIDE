using System.Globalization;
using System.Threading.Tasks;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// vb6.exe SP6, native and p-code: PR #4017,
// docs/vb6-fidelity-oracle/a-double-or-a-single-converted-to-decimal.md.
public class DecimalConversionTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("CDec(0.1)", "0.1")]
    [InlineData("CDec(1.00005)", "1.00005")]
    [InlineData("CDec(1234567890123445#)", "1234567890123440")]
    [InlineData("CDec(-1234567890123445#)", "-1234567890123440")]
    [InlineData("CDec(1234567890123465#)", "1234567890123460")]
    [InlineData("CDec(1234567890123455#)", "1234567890123460")]
    [InlineData("CDec(1 / 3)", "0.333333333333333")]
    [InlineData("CDec(CSng(12345675))", "12345680")]
    [InlineData("CDec(CSng(16777217))", "16777220")]
    [InlineData("CDec(CSng(0.1))", "0.1")]
    [InlineData("CDec(CSng(12345665))", "12345660")]
    [InlineData("CDec(CSng(-12345665))", "-12345660")]
    [InlineData("CDec(1) * 0.1", "0.1")]
    public async Task Floating_point_to_Decimal(string expression, string expected)
    {
        await Run($"Debug.Print {expression}\n");
        AssertDebugLog([Vb6Value.NewDecimal(decimal.Parse(expected, CultureInfo.InvariantCulture))]);
    }

    [Theory]
    [InlineData("FormatNumber(1.005, 2)", "1.01")]
    [InlineData("Format$(0.1, \"0.00000000000000000000\")", "0.10000000000000000000")]
    [InlineData("FormatNumber(2.675, 2)", "2.68")]
    [InlineData("FormatNumber(0.125, 2)", "0.13")]
    [InlineData("FormatPercent(0.00015, 2)", "0.02%")]
    [InlineData("Format$(2.355, \"0.00\")", "2.36")]
    [InlineData("Format$(1.005, \"0.00\")", "1.01")]
    [InlineData("Format$(0.015, \"0.00\")", "0.02")]
    [InlineData("FormatNumber(1234.5678, 20)", "1,234.56780000000000000000")]
    [InlineData("FormatNumber(0.1, 20)", "0.10000000000000000000")]
    public async Task Formatting_sees_the_rounded_Decimal(string expression, string expected)
    {
        using var locale = VbLocale.Use(CultureInfo.GetCultureInfo("en-GB"));
        await Run($"Debug.Print {expression}\n");
        AssertDebugLog([new Vb6Value(expected)]);
    }

    // The printed text, not only the value: decimal equality ignores scale, so 0.1 and 0.100000000000000 compare equal.
    [Theory]
    [InlineData("CStr(CDec(0.1))", "0.1")]
    [InlineData("CStr(CDec(1 / 3))", "0.333333333333333")]
    [InlineData("CStr(CDec(1E-20))", "0.00000000000000000001")]
    [InlineData("CStr(CDec(1) * 0.1)", "0.1")]
    [InlineData("FormatNumber(0.015)", "0.02")]
    [InlineData("CStr(Round(2.675, 2))", "2.68")]
    [InlineData("CStr(Round(0.015, 2))", "0.02")]
    // Not measured on vb6.exe for this mask: it pins what main gave on .NET 10, by the fixed masks' 15-digit rule.
    [InlineData("Format$(2.675, \"0.00E+00\")", "2.68E+00")]
    public async Task Printed_text_matches_vb6(string expression, string expected)
    {
        using var locale = VbLocale.Use(CultureInfo.GetCultureInfo("en-GB"));
        await Run($"Debug.Print {expression}\n");
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Fact]
    public async Task Numeric_date_string_is_a_serial()
    {
        using var locale = VbLocale.Use(CultureInfo.GetCultureInfo("en-GB"));
        await Run("Debug.Print CDbl(CDate(\"24.0\"))\nDebug.Print IsDate(\"24.0\")\n");
        AssertDebugLog([new Vb6Value(24.0), new Vb6Value(false)]);
    }
}
