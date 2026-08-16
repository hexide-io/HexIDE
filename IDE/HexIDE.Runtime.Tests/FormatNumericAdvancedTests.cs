using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Phase 3.7.2 — Format numeric (advanced): multi-section pos;neg;zero masks, scientific notation, and
/// trailing-comma scaling. Pinned against vb6.exe. Masks are quote-free so the assertions don't depend on the
/// interpreter's string-literal handling and stay culture-invariant on CI.
/// </summary>
public class FormatNumericAdvancedTests : BaseVBTestFixture
{
    [Fact]
    public async Task TwoSections_NegativeGetsAbsoluteValue()
    {
        await Run(
            "Debug.Print Format(1234.5, \"#,##0.00;(#,##0.00)\")\n" +   // 1,234.50
            "Debug.Print Format(-1234.5, \"#,##0.00;(#,##0.00)\")\n" +  // (1,234.50)  -- abs, no auto minus
            "Debug.Print Format(0, \"#,##0.00;(#,##0.00)\")\n" +        // 0.00 (zero uses the positive section)
            "Debug.Print Format(-5, \"0.00;0.00\")\n");                 // 5.00 (neg section has no minus)
        AssertDebugLog(["1,234.50", "(1,234.50)", "0.00", "5.00"]);
    }

    [Fact]
    public async Task ThreeSections_RouteBySign()
    {
        await Run(
            "Debug.Print Format(5, \"0.00;(0.00);0.0000\")\n" +   // 5.00
            "Debug.Print Format(-5, \"0.00;(0.00);0.0000\")\n" +  // (5.00)
            "Debug.Print Format(0, \"0.00;(0.00);0.0000\")\n" +   // 0.0000 (zero section)
            "Debug.Print Format(0, \"0;0;Empty\")\n" +            // Empty (literal zero section)
            "Debug.Print Format(7, \"0;0;Empty\")\n");            // 7
        AssertDebugLog(["5.00", "(5.00)", "0.0000", "Empty", "7"]);
    }

    [Fact]
    public async Task Scientific()
    {
        await Run(
            "Debug.Print Format(1234.5678, \"0.00E+00\")\n" +    // 1.23E+03
            "Debug.Print Format(1234.5678, \"0.00E-00\")\n" +    // 1.23E03  (E- shows sign only for negative exp)
            "Debug.Print Format(0.00012345, \"0.00E+00\")\n" +   // 1.23E-04
            "Debug.Print Format(1234.5678, \"0.0E+0\")\n" +      // 1.2E+3
            "Debug.Print Format(-1234.5678, \"0.00E+00\")\n" +   // -1.23E+03
            "Debug.Print Format(1234.5678, \"Scientific\")\n");  // 1.23E+03
        AssertDebugLog(["1.23E+03", "1.23E03", "1.23E-04", "1.2E+3", "-1.23E+03", "1.23E+03"]);
    }

    [Fact]
    public async Task TrailingCommaScaling()
    {
        await Run(
            "Debug.Print Format(1234567, \"#,##0,\")\n" +      // 1,235   (÷1000)
            "Debug.Print Format(1234567, \"0,\")\n" +          // 1235
            "Debug.Print Format(1234567890, \"#,##0,,\")\n" +  // 1,235   (÷1,000,000)
            "Debug.Print Format(1234567, \"#,##0.0,\")\n");    // 1,234,567.0  (comma after '.' does NOT scale)
        AssertDebugLog(["1,235", "1235", "1,235", "1,234,567.0"]);
    }
}
