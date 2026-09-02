using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// #193 — the integer-returning intrinsics have a FIXED declared return type in VB6, and it is not the
/// same one for all of them. Every row here is oracle-pinned against vb6.exe (see "Return widths of the
/// integer-returning intrinsics" in docs/vb6-fidelity-oracle.md).
///
/// The bug was that none of these chose a type: they built their result from a C# <c>int</c>, and
/// <c>Vb6Value(int)</c> applies a MAGNITUDE rule — anything fitting Int16 is reported as an Integer. That
/// rule is right for an arithmetic literal, whose type genuinely does follow its magnitude, and wrong for
/// a function with a declared return type.
///
/// Asserted through <c>TypeName</c> rather than through a value comparison, because the value was never
/// wrong — only the subtype, which is invisible until something asks.
/// </summary>
public class IntrinsicReturnWidthTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("Len(\"hi\")")]
    [InlineData("InStr(\"hello\", \"l\")")]
    [InlineData("InStrRev(\"hello\", \"l\")")]
    [InlineData("LBound(Array(1, 2))")]
    [InlineData("UBound(Array(1, 2))")]
    [InlineData("DateDiff(\"d\", #1/1/2020#, #1/2/2020#)")]
    [InlineData("VarType(1)")]
    public async Task TheseReturnLong_HoweverSmallTheAnswer(string expr)
    {
        await Run("Debug.Print TypeName(" + expr + ")\n");
        AssertDebugLog([new Vb6Value("Long")]);
    }

    // Not one rule applied everywhere — these really are Integer, so a blanket "widen them all" fix would
    // have been just as wrong in the other direction. Kept as tests so that stays true.
    [Theory]
    [InlineData("Asc(\"A\")")]
    [InlineData("Sgn(-3)")]
    [InlineData("Year(#1/2/2020#)")]
    [InlineData("Month(#1/2/2020#)")]
    [InlineData("Day(#1/2/2020#)")]
    [InlineData("Hour(#1/2/2020 3:04:05 AM#)")]
    [InlineData("Minute(#1/2/2020 3:04:05 AM#)")]
    [InlineData("Second(#1/2/2020 3:04:05 AM#)")]
    [InlineData("Weekday(#1/2/2020#)")]
    [InlineData("DatePart(\"d\", #1/2/2020#)")]
    public async Task TheseReturnInteger(string expr)
    {
        await Run("Debug.Print TypeName(" + expr + ")\n");
        AssertDebugLog([new Vb6Value("Integer")]);
    }

    [Fact]
    public async Task DateDiffAndDatePart_DisagreeWithEachOther()
    {
        // Worth its own test because it is the pair most likely to be "tidied" into consistency later.
        // They are genuinely different in VB6: DateDiff is Long, DatePart is Integer. Measured.
        await Run("Debug.Print TypeName(DateDiff(\"d\", #1/1/2020#, #1/2/2020#))\n" +
                  "Debug.Print TypeName(DatePart(\"d\", #1/2/2020#))\n");
        AssertDebugLog([new Vb6Value("Long"), new Vb6Value("Integer")]);
    }

    [Fact]
    public async Task IntAndFix_PreserveTheirOperandSubtype_RatherThanHavingAFixedOne()
    {
        // The control for the whole change: Int and Fix are NOT fixed-return-type functions, so they must
        // keep going through the magnitude rule. Int(3) is Integer and Int(3.5) is Double — the operand
        // decides, which is exactly the behaviour the fix must not have broken.
        await Run("Debug.Print TypeName(Int(3))\nDebug.Print TypeName(Int(3.5))\nDebug.Print TypeName(Fix(3))\n");
        AssertDebugLog([new Vb6Value("Integer"), new Vb6Value("Double"), new Vb6Value("Integer")]);
    }

    [Fact]
    public async Task TheWidthSurvivesArithmetic()
    {
        // Why the subtype matters at all: it feeds the result-type ladder. Len is Long, so Len("hi") + 1
        // is a Long — where an Integer Len would have produced an Integer and, at the edges, a different
        // overflow behaviour.
        await Run("Debug.Print TypeName(Len(\"hi\") + 1)\n");
        AssertDebugLog([new Vb6Value("Long")]);
    }
}
