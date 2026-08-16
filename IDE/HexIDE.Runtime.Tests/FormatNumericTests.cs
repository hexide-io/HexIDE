using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Phase 3.7.1 — Format numeric masks. Every expected value pinned against vb6.exe. Kept to culture-invariant
/// masks (the '.' decimal and ',' group are the same across en-* and Invariant; '$' here is a mask literal, not
/// the locale currency symbol) so the assertions hold on CI. Format rounds HALF-AWAY-FROM-ZERO on the decimal
/// value — distinct from CInt's banker's rounding.
/// </summary>
public class FormatNumericTests : BaseVBTestFixture
{
    [Fact]
    public async Task NamedNumericFormats()
    {
        await Run(
            "Debug.Print Format(1234.5678, \"General Number\")\n" +
            "Debug.Print Format(1234.5678, \"Fixed\")\n" +
            "Debug.Print Format(1234.5678, \"Standard\")\n" +
            "Debug.Print Format(0.4567, \"Percent\")\n" +
            "Debug.Print Format(0.5, \"Fixed\")\n");
        AssertDebugLog(["1234.5678", "1234.57", "1,234.57", "45.67%", "0.50"]);
    }

    [Fact]
    public async Task DigitPlaceholders_ZeroForces_HashOmits()
    {
        await Run(
            "Debug.Print Format(1234.5678, \"0\")\n" +
            "Debug.Print Format(1234.5678, \"0.00\")\n" +
            "Debug.Print Format(1234.5678, \"0.0\")\n" +
            "Debug.Print Format(1234.5678, \"#,##0\")\n" +
            "Debug.Print Format(1234.5678, \"#,##0.00\")\n" +
            "Debug.Print Format(0.5, \"#.##\")\n" +
            "Debug.Print Format(0.5, \"0.##\")\n" +
            "Debug.Print Format(5, \"000\")\n" +
            "Debug.Print Format(0, \"#\")\n" +
            "Debug.Print Format(0, \"0\")\n" +
            "Debug.Print Format(0, \"#,##0\")\n" +
            "Debug.Print Format(5, \"#,##0.00\")\n");
        AssertDebugLog(["1235", "1234.57", "1234.6", "1,235", "1,234.57", ".5", "0.5", "005", "", "0", "0", "5.00"]);
    }

    [Fact]
    public async Task Percent_And_Negative()
    {
        await Run(
            "Debug.Print Format(0.5, \"0%\")\n" +
            "Debug.Print Format(0.4567, \"0.0%\")\n" +
            "Debug.Print Format(-1234.5678, \"0.00\")\n" +
            "Debug.Print Format(-1234.5678, \"#,##0.00\")\n");
        AssertDebugLog(["50%", "45.7%", "-1234.57", "-1,234.57"]);
    }

    [Fact]
    public async Task Rounding_IsHalfAwayFromZero_NotBankers()
    {
        await Run(
            "Debug.Print Format(0.5, \"0\")\n" +
            "Debug.Print Format(1.5, \"0\")\n" +
            "Debug.Print Format(2.5, \"0\")\n" +
            "Debug.Print Format(2.345, \"0.00\")\n" +
            "Debug.Print Format(2.355, \"0.00\")\n");
        AssertDebugLog(["1", "2", "3", "2.35", "2.36"]);
    }

    [Fact]
    public async Task LiteralCharactersInMask()
    {
        await Run(
            "Debug.Print Format(1234.5, \"$#,##0.00\")\n" +   // $ is a mask literal
            "Debug.Print Format(50, \"0 units\")\n");
        AssertDebugLog(["$1,234.50", "50 units"]);
    }
}
