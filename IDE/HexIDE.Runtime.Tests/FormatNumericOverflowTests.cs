using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Regression for the adversarial bug-hunt HIGH: Format() of a magnitude beyond decimal range (|x| &gt; ~7.9e28) cast
/// the double to decimal and threw an UNCATCHABLE OverflowException, crashing the program (not trappable with
/// On Error). VB6 doesn't error — it renders General Number / no-mask / Scientific in scientific notation. Those
/// cases are oracle-pinned against vb6.exe. (A fixed mask like "0.00" is fully EXPANDED by VB6; HexIDE approximates
/// that as scientific — documented divergence — the point is no crash.)
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

    [Fact]
    public async Task OutOfDecimalRange_FixedMask_ApproximatesAsScientific_NoCrash()
    {
        // VB6 fully expands this to "1000000000000000000000000000000.00"; HexIDE approximates out-of-decimal-range
        // fixed masks as scientific (documented). The regression point: it no longer throws an OverflowException.
        await Run("Debug.Print Format(1E+30, \"0.00\")\n");
        AssertDebugLog(["1E+30"]);
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
