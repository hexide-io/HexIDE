using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Regression for the adversarial bug-hunt HIGH: Format() of a magnitude beyond decimal range (|x| &gt; ~7.9e28) cast
/// the double to decimal and threw an UNCATCHABLE OverflowException, crashing the program (not trappable with
/// On Error). VB6 doesn't error — it renders General Number / no-mask / Scientific in scientific notation. Those
/// cases and the 15-significant-digit expansion under fixed masks are oracle-pinned against vb6.exe.
/// </summary>
public class FormatNumericOverflowTests : BaseVBTestFixture
{
    [Fact]
    public async Task OutOfDecimalRange_GeneralAndScientific_MatchVb6()
    {
        await Run(
            "Debug.Print Format(1E+30)\n" +                       // scientific default
            "Debug.Print Format(-2.5E+29)\n" +
            "Debug.Print Format(1E+30, \"General Number\")\n");
        AssertDebugLog(["1E+30", "-2.5E+29", "1E+30"]);
    }

    [Theory]
    [InlineData("1E+30", "0.00", "1000000000000000000000000000000.00")]
    [InlineData("1.23456789012345E+30", "0.00", "1234567890123450000000000000000.00")]
    [InlineData("-1E+30", "0.00", "-1000000000000000000000000000000.00")]
    [InlineData("1.23456789012345E+30", "#,##0", "1,234,567,890,123,450,000,000,000,000,000")]
    [InlineData("-1E+30", "#,##0", "-1,000,000,000,000,000,000,000,000,000,000")]
    [InlineData("1.2345678901234567E+30", "0", "1234567890123460000000000000000")]
    [InlineData("2# ^ 100", "0", "1267650600228230000000000000000")]
    [InlineData("1.23456789012345E+30", "Standard", "1,234,567,890,123,450,000,000,000,000,000.00")]
    [InlineData("-1.23456789012345E+30", "#,##0.00;(#,##0.00)", "(1,234,567,890,123,450,000,000,000,000,000.00)")]
    public async Task OutOfDecimalRange_FixedMask_MatchesVb6(string expression, string mask, string expected)
    {
        await Run($"Debug.Print Format({expression}, \"{mask}\")\n");
        AssertDebugLog([expected]);
    }

    [Fact]
    public async Task OutOfDecimalRange_HundredsOfDigits_MatchesVb6()
    {
        await Run("Debug.Print Format(1.5E+300, \"0\")\n");
        AssertDebugLog(["15" + new string('0', 299)]);
    }

    [Fact]
    public async Task LongFractionalMask_ZeroPadsBeyondPrecision_NoCrash()
    {
        // A fixed mask with more fractional placeholders than decimal can round to (Math.Round's 28-digit limit) or
        // than fits a long when scaling crashed with ArgumentOutOfRangeException/OverflowException. VB6 zero-pads a
        // fixed mask beyond the value's precision and never errors — oracle-verified against vb6.exe.
        await Run(
            "Debug.Print Format(0.5, \"0." + new string('0', 20) + "\")\n" +
            "Debug.Print Format(1.5, \"0." + new string('0', 30) + "\")\n");
        AssertDebugLog(["0.5" + new string('0', 19), "1.5" + new string('0', 29)]);
    }
}
