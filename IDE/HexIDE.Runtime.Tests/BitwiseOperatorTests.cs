using System.Threading.Tasks;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// VB6's bitwise operators — And, Or, Xor, Eqv, Imp, Not.
///
/// Every expectation here is a row measured against vb6.exe, recorded under "Bitwise And / Or / Xor / Not —
/// the result-type ladder" in docs/vb6-fidelity-oracle.md. None of it is inferred, because most of it is
/// not guessable: two Bytes stay a Byte, a Byte beside a Boolean becomes an Integer, and `Not CByte(5)` is
/// 250 rather than -6.
///
/// The defect these pin (#166): the operators unpacked to `int` or to `bool` and rejected everything else,
/// so a Long or floating operand — which is to say any `&amp;H…&amp;` mask, and therefore most real code
/// that touches the Windows API — raised a spurious Err 13.
/// </summary>
public class BitwiseOperatorTests : BaseVBTestFixture
{
    // ── the operand types that used to be rejected outright ──────────────────

    [Fact]
    public async Task ALongOperandIsNotATypeMismatch()
    {
        // The bug, in one line. `flags And &HFF00&` is ordinary VB6.
        await Run("Debug.Print CStr(&HFF00& And &H0FF0&)");
        AssertDebugLog(["3840"]);
    }

    [Fact]
    public async Task AFloatingOperandRoundsToLong()
    {
        await Run("Debug.Print CStr(CDbl(2.4) And 3)");
        AssertDebugLog(["2"]);
    }

    [Theory]
    // Half-to-even, and 3.5 is the row that proves it: rounding up to 4 gives 0, truncating to 3 gives 3.
    [InlineData("CDbl(2.5) And 3", "2")]
    [InlineData("CDbl(3.5) And 3", "0")]
    [InlineData("CDbl(-2.5) And 3", "2")]
    [InlineData("CSng(2.5) And 3", "2")]
    public async Task AFloatingOperandRoundsHalfToEven(string expression, string expected)
    {
        await Run($"Debug.Print CStr({expression})");
        AssertDebugLog([expected]);
    }

    [Fact]
    public async Task ANumericStringIsAccepted()
    {
        await Run("Debug.Print CStr(\"12\" And 10)");
        AssertDebugLog(["8"]);
    }

    // ── the result-type ladder ───────────────────────────────────────────────

    [Theory]
    [InlineData("CInt(5) And CInt(3)", "Integer")]
    [InlineData("CLng(5) And CInt(3)", "Long")]
    [InlineData("CLng(5) And CLng(3)", "Long")]
    [InlineData("CDbl(5) And CLng(3)", "Long")]
    [InlineData("&HFF00& And &H0FF0&", "Long")]
    // A numeric string reports Long, not Integer.
    [InlineData("\"12\" And 10", "Long")]
    // Two Bytes STAY a Byte — arithmetic would promote them to Integer, bitwise does not.
    [InlineData("CByte(5) And CByte(3)", "Byte")]
    // ...but a Byte beside anything else does promote, including beside a Boolean.
    [InlineData("CByte(5) And CInt(3)", "Integer")]
    [InlineData("CByte(5) And CLng(3)", "Long")]
    [InlineData("CByte(5) And True", "Integer")]
    // Two Booleans stay Boolean; a Boolean beside a number does not.
    [InlineData("True And True", "Boolean")]
    [InlineData("CInt(5) And True", "Integer")]
    [InlineData("CLng(5) And True", "Long")]
    public async Task TheResultTypeFollowsTheLadder(string expression, string expectedType)
    {
        await Run($"Debug.Print TypeName({expression})");
        AssertDebugLog([expectedType]);
    }

    // ── Boolean operands stay bitwise ────────────────────────────────────────

    [Fact]
    public async Task ABooleanBesideANumberIsStillABitOperation()
    {
        // True is -1, so this is `-1 And 2` = 2. These operators never become logical short-circuits.
        await Run("Debug.Print CStr(True And 2)");
        AssertDebugLog(["2"]);
    }

    [Theory]
    [InlineData("True And True", "True")]
    [InlineData("True Or False", "True")]
    [InlineData("Not True", "False")]
    public async Task TwoBooleansStillReadAsBooleans(string expression, string expected)
    {
        await Run($"Debug.Print CStr({expression})");
        AssertDebugLog([expected]);
    }

    // ── Not keeps its operand's width ────────────────────────────────────────

    [Theory]
    // The one nobody guesses: Byte complements at EIGHT bits, so 255 - 5.
    [InlineData("Not CByte(5)", "250")]
    [InlineData("Not 5", "-6")]
    [InlineData("Not CLng(5)", "-6")]
    [InlineData("Not CDbl(5)", "-6")]
    [InlineData("Not \"12\"", "-13")]
    public async Task NotComplementsAtItsOperandsWidth(string expression, string expected)
    {
        await Run($"Debug.Print CStr({expression})");
        AssertDebugLog([expected]);
    }

    [Theory]
    [InlineData("Not CByte(5)", "Byte")]
    [InlineData("Not 5", "Integer")]
    [InlineData("Not CLng(5)", "Long")]
    [InlineData("Not True", "Boolean")]
    [InlineData("Not CDbl(5)", "Long")]
    public async Task NotReportsItsOperandsType(string expression, string expectedType)
    {
        await Run($"Debug.Print TypeName({expression})");
        AssertDebugLog([expectedType]);
    }

    // ── the other operators share the ladder ─────────────────────────────────

    [Theory]
    [InlineData("5 Or 3", "7")]
    [InlineData("5 Xor 3", "6")]
    [InlineData("&HFF00& Or &H00FF&", "65535")]
    [InlineData("CLng(5) Xor CLng(3)", "6")]
    public async Task OrAndXorTakeTheSameOperands(string expression, string expected)
    {
        await Run($"Debug.Print CStr({expression})");
        AssertDebugLog([expected]);
    }

    [Fact]
    public async Task EqvAndImpTakeALongOperand()
    {
        await Run("Debug.Print CStr(CLng(5) Eqv CLng(3))\r\nDebug.Print CStr(CLng(5) Imp CLng(3))");
        AssertDebugLog(["-7", "-5"]);
    }
}
