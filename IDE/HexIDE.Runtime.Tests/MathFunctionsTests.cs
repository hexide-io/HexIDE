using HexIDE.Runtime.Interpreter;

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

    [Fact]
    public async Task Exp_Sin_Cos()
    {
        await Run("Debug.Print Exp(0)\nDebug.Print Sin(0)\nDebug.Print Cos(0)\n");
        AssertDebugLog([new Vb6Value(1.0), new Vb6Value(0.0), new Vb6Value(1.0)]);
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
