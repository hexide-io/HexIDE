using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Interpreter.ConditionalCompilation;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Phase 3 — Math intrinsics. Verified against vb6.exe: Abs preserves type, Int(floor)/Fix(truncate),
/// Round banker's, Sgn→Integer, Sqr/trig→Double, and Rnd's exact 24-bit LCG sequence.
/// </summary>
public class MathFunctionsTests : BaseVBTestFixture
{
    [Fact]
    public async Task Abs_PreservesType()
    {
        await Run("Debug.Print Abs(-4)\nDebug.Print Abs(-4.5)\n");
        AssertDebugLog([new Vb6Value(4), new Vb6Value(4.5)]);   // Integer, Double
    }

    [Fact]
    public async Task IntFloors_FixTruncates()
    {
        await Run("Debug.Print Int(-2.5)\nDebug.Print Fix(-2.5)\nDebug.Print Int(2.7)\nDebug.Print Fix(2.7)\n");
        AssertDebugLog([new Vb6Value(-3.0), new Vb6Value(-2.0), new Vb6Value(2.0), new Vb6Value(2.0)]);
    }

    [Fact]
    public async Task Sgn_Sqr_Round()
    {
        await Run(
            "Debug.Print Sgn(-5)\nDebug.Print Sgn(0)\nDebug.Print Sgn(3)\n" +
            "Debug.Print Sqr(9)\n" +
            "Debug.Print Round(2.5)\nDebug.Print Round(3.5)\nDebug.Print Round(2.567, 2)\n");
        AssertDebugLog([
            new Vb6Value(-1), new Vb6Value(0), new Vb6Value(1),
            new Vb6Value(3.0),
            new Vb6Value(2.0), new Vb6Value(4.0), new Vb6Value(2.57),
        ]);
    }

    // Round keeps a Decimal's, a Currency's and a Single's type, and rounds a Decimal or a Currency on its exact value
    // (#4085). vb6.exe SP6, native and p-code agreeing, on a-double-or-a-single-converted-to-decimal.md (oracle PR #4017).
    // The Double rows were already right; they pin that a Double 2.345 still gives 2.35 beside the Decimal's 2.34.
    [Theory]
    [InlineData("CDec(2.345), 2", "2.34|Decimal")]
    [InlineData("CCur(2.345), 2", "2.34|Currency")]
    [InlineData("CDec(0.575), 2", "0.58|Decimal")]
    [InlineData("CSng(0.545), 2", "0.55|Single")]
    [InlineData("2.345, 2", "2.35|Double")]
    [InlineData("0.575, 2", "0.57|Double")]
    [InlineData("2.675, 2", "2.68|Double")]
    [InlineData("2.5", "2|Double")]
    [InlineData("3.5", "4|Double")]
    public async Task Round_keeps_its_arguments_type_and_rounds_a_Decimal_or_Currency_exactly(string args, string expected)
    {
        await Run($"Debug.Print CStr(Round({args})) & \"|\" & TypeName(Round({args}))\n");
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Fact]
    public async Task Exp_Sin_Cos()
    {
        await Run("Debug.Print Exp(0)\nDebug.Print Sin(0)\nDebug.Print Cos(0)\n");
        AssertDebugLog([new Vb6Value(1.0), new Vb6Value(0.0), new Vb6Value(1.0)]);
    }

    public static TheoryData<string, ulong, ulong> Vb6TrigRows
    {
        get
        {
            var rows = new TheoryData<string, ulong, ulong>();
            foreach (var (argument, sin, cos, tan, atn) in X87MathOracle.Trig)
            {
                rows.Add("Sin", argument, sin);
                rows.Add("Cos", argument, cos);
                rows.Add("Tan", argument, tan);
                rows.Add("Atn", argument, atn);
            }

            return rows;
        }
    }

    [Theory]
    [MemberData(nameof(Vb6TrigRows))]
    public Task Vb6_trig_matches_x87_bits(string function, ulong argument, ulong expected) =>
        CheckDoubleBits(function, argument, expected, VbaBitness.None);

    public static TheoryData<ulong, ulong> X87SqrTieRows =>
        new(X87MathOracle.SquareRootTies.Select(row => (row.Argument, row.X87)));

    [Theory]
    [MemberData(nameof(X87SqrTieRows))]
    public Task Vb6_sqr_rounds_twice_at_ties(ulong argument, ulong x87) =>
        CheckDoubleBits("Sqr", argument, x87, VbaBitness.None);

    // #5259 and oracle PR #5294. Three 32-bit Atn rows diverge from the model.
    [Theory]
    [InlineData("Sin", 0x400921FB54442D18UL, 0x3CA1A60000000000UL)]
    [InlineData("Cos", 0x3FF921FB54442D18UL, 0x3C91A60000000000UL)]
    [InlineData("Tan", 0x3FF921FB54442D18UL, 0x434D02D546D3EAB2UL)]
    [InlineData("Sin", 0x41AC802088A3DF03UL, 0xBFA13E9EBF84E15AUL)]
    public Task Vba32_trig_matches_x87_bits(string function, ulong argument, ulong expected) =>
        CheckDoubleBits(function, argument, expected, VbaBitness.Bit32);

    public static TheoryData<ulong> TrigArguments => new(X87MathOracle.Trig.Select(row => row.Argument));

    [Theory]
    [MemberData(nameof(TrigArguments))]
    public Task Vba64_tan_divides_double_sin_by_double_cos(ulong argument)
    {
        double x = BitConverter.UInt64BitsToDouble(argument);
        return CheckDoubleBits("Tan", argument, BitConverter.DoubleToUInt64Bits(Math.Sin(x) / Math.Cos(x)),
            VbaBitness.Bit64);
    }

    [Theory]
    [MemberData(nameof(TrigArguments))]
    public async Task Vba64_sin_cos_and_atn_use_math(ulong argument)
    {
        double x = BitConverter.UInt64BitsToDouble(argument);
        SetMathProject(VbaBitness.Bit64, x);
        await Run("Debug.Print Sin(oracleInput)\nDebug.Print Cos(oracleInput)\nDebug.Print Atn(oracleInput)");
        AssertDoubleBits([Math.Sin(x), Math.Cos(x), Math.Atan(x)]);
    }

    [Theory]
    [MemberData(nameof(TrigArguments))]
    public async Task Sqr_matches_math_on_ordinary_arguments(ulong argument)
    {
        double x = Math.Abs(BitConverter.UInt64BitsToDouble(argument));
        ulong expected = BitConverter.DoubleToUInt64Bits(Math.Sqrt(x));
        foreach (var bitness in Enum.GetValues<VbaBitness>())
        {
            debug.Clear();
            await CheckDoubleBits("Sqr", BitConverter.DoubleToUInt64Bits(x), expected, bitness);
        }
    }

    public static TheoryData<ulong, ulong> IeeeSqrTieRows =>
        new(X87MathOracle.SquareRootTies.Select(row => (row.Argument, row.Ieee)));

    [Theory]
    [MemberData(nameof(IeeeSqrTieRows))]
    public Task Vba64_sqr_rounds_once_at_ties(ulong argument, ulong ieee) =>
        CheckDoubleBits("Sqr", argument, ieee, VbaBitness.Bit64);

    [Theory]
    [InlineData(VbaBitness.None)]
    [InlineData(VbaBitness.Bit32)]
    [InlineData(VbaBitness.Bit64)]
    public async Task Sqr_of_a_negative_number_raises_error_5(VbaBitness bitness)
    {
        SetMathProject(bitness, -1);
        await Run("On Error Resume Next\nDebug.Print Sqr(oracleInput)\nDebug.Print Err.Number");
        AssertDebugLog([new Vb6Value(5L)]);
    }

    private async Task CheckDoubleBits(string function, ulong argument, ulong expected, VbaBitness bitness)
    {
        SetMathProject(bitness, BitConverter.UInt64BitsToDouble(argument));
        await Run($"Debug.Print {function}(oracleInput)");
        AssertDoubleBits([BitConverter.UInt64BitsToDouble(expected)]);
    }

    private void SetMathProject(VbaBitness bitness, double input)
    {
        Compilation = CompilationConstants.ForProject(new ProjectDefinition(VBProjectType.EXE, "Test")
            { VbaBitness = bitness });
        // Inject the measured bytes directly, as the oracle probe did.
        rootEnv.DefineVariable("oracleInput", context.State.Alloc(new Vb6Value(input)));
    }

    private void AssertDoubleBits(double[] expected)
    {
        debug.Should().Equal(expected, (actual, value) =>
            actual.Type == Vb6Value.ValueType.Double && actual.Value is double result &&
            BitConverter.DoubleToUInt64Bits(result) == BitConverter.DoubleToUInt64Bits(value));
    }

    [Fact]
    public async Task Rnd_MatchesVb6Sequence()
    {
        // Fresh seed 0x50000; the first three values are bit-identical to real vb6.exe (Single).
        await Run("Debug.Print Rnd()\nDebug.Print Rnd()\nDebug.Print Rnd()\n");
        AssertDebugLog([new Vb6Value(0.7055475f), new Vb6Value(0.533424f), new Vb6Value(0.5795186f)]);
    }

    [Fact]
    public async Task RndZero_ReturnsLastWithoutAdvancing()
    {
        await Run("Debug.Print Rnd()\nDebug.Print Rnd(0)\n");
        AssertDebugLog([new Vb6Value(0.7055475f), new Vb6Value(0.7055475f)]);
    }

    [Fact]
    public async Task Randomize_FoldsIntoCurrentState()
    {
        await Run("Randomize 42\nDebug.Print Rnd()\nRandomize 42\nDebug.Print Rnd()\n" +
            "x = Rnd(-1)\nRandomize 42\nDebug.Print Rnd()\nx = Rnd(-1)\nRandomize 42\nDebug.Print Rnd()\n");
        AssertExactRandomLog([new Vb6Value(5689283 / 16777216f), new Vb6Value(4299130 / 16777216f),
            new Vb6Value(15230513 / 16777216f), new Vb6Value(15230513 / 16777216f)]);
    }

    [Theory]
    [InlineData("42", 15230513, 10252336, 2821683)]
    [InlineData("7", 319793, 15925040, 2318643)]
    [InlineData("1000", 5212209, 7277104, 10174515)]
    public async Task Randomize_AfterNegativeRnd_MatchesVb6Sequence(string seed, int first, int second, int third)
    {
        await Run($"x = Rnd(-1)\nRandomize {seed}\nDebug.Print Rnd()\nDebug.Print Rnd()\nDebug.Print Rnd()\n");
        AssertExactRandomLog([new Vb6Value(first / 16777216f), new Vb6Value(second / 16777216f),
            new Vb6Value(third / 16777216f)]);
    }

    [Theory]
    [InlineData("0.1", 14865713)]
    [InlineData("123456789.123", 11352625)]
    [InlineData("123456789", 11352625)]
    public async Task Randomize_FoldsHighDoubleBits(string seed, int expected)
    {
        await Run($"x = Rnd(-1)\nRandomize {seed}\nDebug.Print Rnd()\n");
        AssertExactRandomLog([new Vb6Value(expected / 16777216f)]);
    }

    [Fact]
    public async Task Randomize_WithoutDrawBetween_IsIdempotent()
    {
        await Run("x = Rnd(-1)\nRandomize 42\nRandomize 42\nDebug.Print Rnd()\n");
        AssertExactRandomLog([new Vb6Value(15230513 / 16777216f)]);
    }

    [Theory]
    [InlineData("-1", 3758214)]
    [InlineData("-2", 11967619)]
    [InlineData("-0.5", 12146822)]
    public async Task Rnd_NegativeArgument_FoldsSingleBits(string argument, int expected)
    {
        await Run($"Debug.Print Rnd({argument})\n");
        AssertExactRandomLog([new Vb6Value(expected / 16777216f)]);
    }

    // Compare the Single payload exactly; the shared helper allows a numeric tolerance.
    private void AssertExactRandomLog(List<Vb6Value> expected)
    {
        debug.Should().Equal(expected, (actual, value) =>
            actual.Type == value.Type && Equals(actual.Value, value.Value));
    }

}
