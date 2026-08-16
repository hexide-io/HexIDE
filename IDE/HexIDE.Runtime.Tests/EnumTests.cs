using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Interpreter-advanced Phase 2 — Enum. Members are Long compile-time constants (auto-increment from 0, or an
/// explicit integer literal), accessible unqualified, qualified (MyEnum.Member), and as a variable type.
/// </summary>
public class EnumTests : BaseVBTestFixture
{
    [Fact]
    public async Task Unqualified_AutoIncrement()
    {
        await Run(
            "Enum Fruit\nApple\nBanana\nCherry\nEnd Enum\n" +
            "Debug.Print Apple\n" +
            "Debug.Print Banana\n" +
            "Debug.Print Cherry\n");
        AssertDebugLog([new Vb6Value(0L), new Vb6Value(1L), new Vb6Value(2L)]);
    }

    [Fact]
    public async Task ExplicitValues_ThenAutoIncrementResumes()
    {
        await Run(
            "Enum Code\nA = 10\nB = 20\nC\nEnd Enum\n" +
            "Debug.Print A\n" +
            "Debug.Print B\n" +
            "Debug.Print C\n");   // 21 (auto-increment from B + 1)
        AssertDebugLog([new Vb6Value(10L), new Vb6Value(20L), new Vb6Value(21L)]);
    }

    [Fact]
    public async Task QualifiedMember()
    {
        await Run(
            "Enum Fruit\nApple\nBanana\nEnd Enum\n" +
            "Debug.Print Fruit.Banana\n");
        AssertDebugLog([new Vb6Value(1L)]);
    }

    [Fact]
    public async Task AsVariableType()
    {
        await Run(
            "Enum Fruit\nApple\nBanana\nEnd Enum\n" +
            "Dim f As Fruit\n" +
            "f = Banana\n" +
            "Debug.Print f\n");
        AssertDebugLog([new Vb6Value(1L)]);
    }

    [Fact]
    public async Task NegativeExplicitValue()
    {
        await Run(
            "Enum Sign\nNeg = -1\nZero = 0\nPos = 1\nEnd Enum\n" +
            "Debug.Print Neg\n" +
            "Debug.Print Pos\n");
        AssertDebugLog([new Vb6Value(-1L), new Vb6Value(1L)]);
    }

    [Fact]
    public async Task EnumReturningFunction_DefaultsToLongZero()
    {
        // Regression (review): an Enum-returning Function with no explicit assignment returned a null
        // UserDefinedType; a VB6 Enum is a Long, so an unassigned Enum return is 0.
        await Run(
            "Enum Color\nRed\nGreen\nEnd Enum\n" +
            "Function GetColor() As Color\n" +
            "End Function\n" +
            "Debug.Print GetColor()\n" +      // 0
            "Debug.Print GetColor() + 1\n");  // 1
        AssertDebugLog([new Vb6Value(0L), new Vb6Value(1L)]);
    }
}
