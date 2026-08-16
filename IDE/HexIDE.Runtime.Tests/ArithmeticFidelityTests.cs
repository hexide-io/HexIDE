using System.Threading.Tasks;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Phase 2.3 — VB6 numeric fidelity via VbNumeric: the arithmetic result-type table, overflow (Err 6),
/// integer <c>\</c>/<c>Mod</c> with operand rounding, division-by-zero (Err 11), and coercion-on-store.
/// Every expectation here was verified against real vb6.exe.
/// </summary>
public class ArithmeticFidelityTests : BaseVBTestFixture
{
    // ----- result types (+ - *) -----

    [Fact]
    public async Task IntegerPlusInteger_StaysInteger()
    {
        await Run("Debug.Print 5 + 3\n");
        AssertDebugLog([new Vb6Value(8)]);            // Integer
    }

    [Fact]
    public async Task IntegerPlusLong_IsLong()
    {
        await Run("Debug.Print 40000 + 5\n");
        AssertDebugLog([new Vb6Value(40005)]);        // 40005 > Int16 -> Long
    }

    [Fact]
    public async Task LongTimesInteger_IsLong()
    {
        await Run("Debug.Print 40000 * 2\n");
        AssertDebugLog([new Vb6Value(80000)]);        // Long
    }

    // ----- overflow (Err 6) -----

    [Fact]
    public async Task IntegerPlusInteger_Overflow_RaisesErr6()
    {
        Func<Task> act = () => Run("Debug.Print 30000 + 30000\n");
        (await act.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(6);
    }

    [Fact]
    public async Task IntegerTimesInteger_Overflow_RaisesErr6()
    {
        Func<Task> act = () => Run("Debug.Print 200 * 200\n");   // 40000 exceeds Int16
        (await act.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(6);
    }

    // ----- integer division \ (operands round half-to-even; Long when any operand is Single/Double) -----

    [Fact]
    public async Task IntegerDivision_BothInteger_IsInteger()
    {
        await Run("Debug.Print 7 \\ 2\n");
        AssertDebugLog([new Vb6Value(3)]);            // Integer
    }

    [Theory]
    [InlineData("7.6", 4L)]      // 7.6 rounds to 8; 8 \ 2 = 4 (Long, Double operand)
    [InlineData("7.5", 4L)]      // 7.5 rounds half-to-even to 8; 8 \ 2 = 4
    [InlineData("6.4", 3L)]      // 6.4 rounds to 6; 6 \ 2 = 3
    public async Task IntegerDivision_FractionalOperand_RoundsThenDivides_AsLong(string lhs, long expected)
    {
        await Run($"Debug.Print {lhs} \\ 2\n");
        AssertDebugLog([new Vb6Value(expected)]);
    }

    // ----- division by zero (Err 11) -----

    [Theory]
    [InlineData("5 / 0")]
    [InlineData("5 \\ 0")]
    [InlineData("5 Mod 0")]
    public async Task DivisionByZero_RaisesErr11(string expr)
    {
        Func<Task> act = () => Run($"Debug.Print {expr}\n");
        (await act.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(11);
    }

    // ----- real division / keeps its (already VB6-correct) result types -----

    [Fact]
    public async Task RealDivision_IntegerByInteger_IsDouble()
    {
        await Run("Debug.Print 5 / 2\n");
        AssertDebugLog([new Vb6Value(2.5)]);          // Double (verified against vb6.exe)
    }

    // ----- coercion-on-store: function return -----

    [Fact]
    public async Task FunctionReturn_CoercesToDeclaredType_BankersRound()
    {
        await Run(
            "Function F() As Integer\n" +
            "F = 5.7\n" +
            "End Function\n" +
            "Debug.Print F()\n");
        AssertDebugLog([new Vb6Value(6)]);            // 5.7 -> Integer 6
    }

    [Fact]
    public async Task FunctionReturn_OverflowingDeclaredType_RaisesErr6()
    {
        Func<Task> act = () => Run(
            "Function G() As Integer\n" +
            "G = 40000\n" +
            "End Function\n" +
            "Debug.Print G()\n");
        (await act.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(6);
    }

    // ----- coercion-on-store: ByVal parameter -----

    [Fact]
    public async Task ByValParameter_CoercesToDeclaredType()
    {
        await Run(
            "Sub S(ByVal n As Integer)\n" +
            "Debug.Print n\n" +
            "End Sub\n" +
            "S 5.7\n");
        AssertDebugLog([new Vb6Value(6)]);            // 5.7 -> Integer 6 on the ByVal copy
    }
}
