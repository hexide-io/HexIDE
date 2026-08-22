using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// VB6 has no Infinity and no NaN. IEEE-754 produces them; VB6 raises instead.
///
/// <para>
/// Six expressions used to hand one back as a <i>value</i>. That is worse than an error, because it is a
/// wrong ANSWER: <c>1E+308 * 10</c> printed as "∞", and then propagated through every comparison and every
/// later sum as something no VB6 program can test for or recover from. `On Error` cannot catch a number.
/// </para>
///
/// <para>
/// The rule, measured against vb6.exe (see <i>Infinity and NaN</i> in docs/vb6-fidelity-oracle.md): an
/// infinite result is <b>Err 6</b> (Overflow) and a NaN is <b>Err 5</b> (Invalid procedure call). <c>^</c>
/// carries one extra case that no result inspection can infer — <c>0 ^ -1</c> is infinite but VB6 calls it
/// Err 5, because the cause is its domain rather than its magnitude.
/// </para>
///
/// <para>
/// Found while investigating Variant overflow promotion. These are independent of that: they hold whether
/// the operands are declared or Variant, which is why they could be fixed while the promotion rule — which
/// needs a declared-type notion HexIDE does not yet have — stays open.
/// </para>
/// </summary>
public class InfinityAndNaNTests : BaseVBTestFixture
{
    private const string Backslash = "\\";

    private async Task<long> ErrFrom(string expression)
    {
        await Run(
            "On Error Resume Next\n" +
            "Dim v, a, b\n" +
            expression + "\n" +
            "Debug.Print Err.Number\n");
        return Convert.ToInt64(debug[^1].Value);
    }

    // ── overflow → Err 6 ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MultiplicationThatOverflowsADouble_IsError6() =>
        (await ErrFrom("a = 1E+308 : v = a * 10")).Should().Be(6);

    [Fact]
    public async Task DivisionThatOverflowsADouble_IsError6() =>
        // The Double path of `/` never went through the narrowing check at all, only the Single path did.
        (await ErrFrom("a = 1E+308 : v = a / 0.0001")).Should().Be(6);

    [Fact]
    public async Task PowerThatOverflowsADouble_IsError6() =>
        (await ErrFrom("a = 1E+308 : v = a ^ 2")).Should().Be(6);

    [Fact]
    public async Task ExpThatOverflowsADouble_IsError6() =>
        (await ErrFrom("v = Exp(1000)")).Should().Be(6);

    [Fact]
    public async Task SingleOverflow_IsStillError6() =>
        // This one was already right, via a guard that read `float.IsInfinity(f) && !double.IsInfinity(res)`.
        // The second half of that condition deliberately let an already-infinite double through, which is the
        // case that needed raising — so the guard was correct only for the operands it had been tried with.
        (await ErrFrom("a = CSng(3E+38) : v = a * CSng(10)")).Should().Be(6);

    // ── NaN and domain → Err 5 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ANegativeBaseToAFractionalPower_IsError5() =>
        // .NET returns NaN; VB6 refuses. It printed as the string "NaN" before.
        (await ErrFrom("a = -2 : v = a ^ 0.5")).Should().Be(5);

    [Fact]
    public async Task ZeroToANegativePower_IsError5_NotError6() =>
        // The case a result inspection cannot get right: the value is infinite, so the obvious rule says
        // Overflow, and vb6.exe says Invalid procedure call. Measured, not reasoned.
        (await ErrFrom("a = 0 : v = a ^ -1")).Should().Be(5);

    // ── the ones that were already correct, kept so the change is pinned as narrow ────────────────

    [Fact]
    public async Task SqrOfANegative_IsError5() =>
        (await ErrFrom("v = Sqr(-1)")).Should().Be(5);

    [Fact]
    public async Task LogOfZeroOrNegative_IsError5()
    {
        (await ErrFrom("v = Log(0)")).Should().Be(5);
        (await ErrFrom("v = Log(-1)")).Should().Be(5);
    }

    [Fact]
    public async Task IntegerDivisionAndModByZero_AreError11() =>
        // `\` and `Mod` by zero really are Division by zero, unlike `/`, whose by-zero code depends on
        // whether the operands were declared — a divergence that is still open because it needs a
        // declared-type notion. Pinned here so this change is not mistaken for having settled it.
        (await ErrFrom($"a = 1 : b = 0 : v = a {Backslash} b")).Should().Be(11);

    // ── arithmetic that does NOT overflow is untouched ───────────────────────────────────────────

    [Fact]
    public async Task OrdinaryArithmeticStillWorks()
    {
        await Run(
            "Debug.Print 2 ^ 10\n" +
            "Debug.Print 1E+300 / 1E+100\n" +
            "Debug.Print Exp(1)\n" +
            "Debug.Print Sqr(16)\n");
        Convert.ToDouble(debug[0].Value).Should().Be(1024);
        Convert.ToDouble(debug[1].Value).Should().Be(1E+200);
        Convert.ToDouble(debug[2].Value).Should().BeApproximately(2.718281828, 1e-9);
        Convert.ToDouble(debug[3].Value).Should().Be(4);
    }
}
