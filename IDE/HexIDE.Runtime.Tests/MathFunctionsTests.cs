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
    public async Task Randomize_ReseedsDeterministically()
    {
        await Run("Randomize 42\nDebug.Print Rnd()\nRandomize 42\nDebug.Print Rnd()\n");
        debug.Should().HaveCount(2);
        debug[0].Should().Be(debug[1]);   // same seed -> same next value
    }
}
