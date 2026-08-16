using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Phase 2.2 — the new numeric subtypes (Long/Byte/Currency/Decimal) flow through the arithmetic and
/// comparison cascades and through Select Case ranges, without a Type Mismatch. Per-operator RESULT-type
/// fidelity (e.g. Long+Long -> Long, not Double) and overflow are 2.3's job; here we only prove they flow.
/// </summary>
public class WideningTests : BaseVBTestFixture
{
    [Fact]
    public async Task LongComparedToInteger()
    {
        await Run("Debug.Print 40000 > 1\n");
        AssertDebugLog([new Vb6Value(true)]);
    }

    [Fact]
    public async Task LongEqualsLong()
    {
        await Run("Debug.Print 40000 = 40000\n");
        AssertDebugLog([new Vb6Value(true)]);
    }

    [Fact]
    public async Task IntegerComparedToLong_ByValue()
    {
        // Cross-type equality: Integer 5 promoted to Long 5, compared to Long 40000.
        await Run("Debug.Print 5 = 40000\n");
        AssertDebugLog([new Vb6Value(false)]);
    }

    [Fact]
    public async Task LongFlowsThroughArithmetic_NoTypeMismatch()
    {
        // The value is correct; the result Type is a stopgap Double until 2.3 makes Long+Integer -> Long.
        await Run("Debug.Print (40000 + 5) > 40000\n");
        AssertDebugLog([new Vb6Value(true)]);
    }

    [Fact]
    public async Task SelectCase_OverLongSelector_MatchesToRange()
    {
        await Run(
            "Dim r As String\n" +
            "Select Case 40000\n" +
            "Case 1 To 30000\n" +
            "r = \"low\"\n" +
            "Case 30001 To 50000\n" +
            "r = \"high\"\n" +
            "End Select\n" +
            "Debug.Print r\n");
        AssertDebugLog([new Vb6Value("high")]);
    }
}
