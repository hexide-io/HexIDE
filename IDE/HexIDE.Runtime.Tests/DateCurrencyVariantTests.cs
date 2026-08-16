using System;
using System.Threading.Tasks;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Phase 2.5 — Date / Currency / Variant runtime semantics. Every expectation was verified against vb6.exe:
/// Date literals + arithmetic (Date+n -> Date, Date-Date -> Double); Currency dominates the + - * ladder
/// (10@ + 1.5 -> Currency) with 4-dp banker's rounding and Overflow (Err 6); Empty/Null concatenate as "".
/// </summary>
public class DateCurrencyVariantTests : BaseVBTestFixture
{
    // ----- Date -----

    [Fact]
    public async Task DateLiteral_ParsesInvariantUsFormat()
    {
        await Run("Debug.Print #1/1/2000#\n");
        AssertDebugLog([new Vb6Value(new DateTime(2000, 1, 1))]);
    }

    [Fact]
    public async Task DatePlusNumber_IsDate()
    {
        await Run("Debug.Print #1/1/2000# + 31\n");
        AssertDebugLog([new Vb6Value(new DateTime(2000, 2, 1))]);
    }

    [Fact]
    public async Task DateMinusNumber_IsDate()
    {
        await Run("Debug.Print #1/1/2000# - 1\n");
        AssertDebugLog([new Vb6Value(new DateTime(1999, 12, 31))]);
    }

    [Fact]
    public async Task DateMinusDate_IsDoubleDayDifference()
    {
        await Run("Debug.Print #1/10/2000# - #1/1/2000#\n");
        AssertDebugLog([new Vb6Value(9.0)]);
    }

    [Fact]
    public async Task DateComparison_IsBoolean()
    {
        await Run("Debug.Print #1/1/2001# > #1/1/2000#\n");
        AssertDebugLog([new Vb6Value(true)]);
    }

    // ----- Currency -----

    [Fact]
    public async Task CurrencyPlusLong_IsCurrency()
    {
        await Run("Debug.Print 10@ + 5\n");
        AssertDebugLog([Vb6Value.NewCurrency(15m)]);
    }

    [Fact]
    public async Task CurrencyPlusDouble_IsCurrency_NotDouble()
    {
        // The surprise verified against vb6.exe: Currency dominates Double in + - *.
        await Run("Debug.Print 10@ + 1.5\n");
        AssertDebugLog([Vb6Value.NewCurrency(11.5m)]);
    }

    [Fact]
    public async Task CurrencyTimesInteger_IsCurrency()
    {
        await Run("Debug.Print 10@ * 3\n");
        AssertDebugLog([Vb6Value.NewCurrency(30m)]);
    }

    [Fact]
    public async Task CurrencyDivided_IsDouble()
    {
        await Run("Debug.Print 10@ / 4\n");
        AssertDebugLog([new Vb6Value(2.5)]);
    }

    [Fact]
    public async Task CurrencyLiteral_RoundsToFourDp_BankersEven()
    {
        await Run("Debug.Print 1.23455@\nDebug.Print 1.23445@\n");
        AssertDebugLog([Vb6Value.NewCurrency(1.2346m), Vb6Value.NewCurrency(1.2344m)]);
    }

    [Fact]
    public async Task CurrencyOverflow_RaisesErr6()
    {
        Func<Task> act = () => Run("Debug.Print 900000000000000@ + 100000000000000@\n");
        (await act.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(6);
    }

    // ----- Variant: Empty / Null -----

    [Fact]
    public async Task EmptyConcatenatesAsEmptyString()
    {
        await Run("Dim v\nDebug.Print v & \"x\"\n");   // Empty & "x" -> "x" (previously NPE'd)
        AssertDebugLog([new Vb6Value("x")]);
    }

    [Fact]
    public async Task EmptyActsAsZeroInArithmetic()
    {
        await Run("Dim v\nDebug.Print v + 5\n");
        AssertDebugLog([new Vb6Value(5)]);            // Integer 5
    }

    [Fact]
    public async Task NullPropagatesThroughArithmetic()
    {
        await Run("Debug.Print Null + 1\n");
        AssertDebugLog([Vb6Value.Null]);
    }

    [Fact]
    public async Task NullConcatenatesAsEmptyString()
    {
        await Run("Debug.Print Null & \"x\"\n");
        AssertDebugLog([new Vb6Value("x")]);
    }
}
