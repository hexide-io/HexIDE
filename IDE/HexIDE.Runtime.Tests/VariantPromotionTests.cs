using System.Threading.Tasks;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// VB6 treats a DECLARED type as a ceiling and a Variant as having none: overflowing the first is Err 6,
/// overflowing the second widens Integer → Long → Double.
///
/// Every expectation is measured — see docs/vb6-fidelity-oracle.md. The rule is not "driven by the result"
/// as #122 originally recorded: it is driven by **operand provenance**, and a literal counts as fixed, not
/// as a Variant. Two rows below exist purely to pin that, because getting them wrong would silently promote
/// `i = i + 1` past a declared Integer's ceiling — the common shape, not a rare one.
/// </summary>
public class VariantPromotionTests : BaseVBTestFixture
{
    // ── a Variant has no ceiling ─────────────────────────────────────────────

    [Theory]
    [InlineData("a = 30000 : b = 3 : Debug.Print CStr(a * b)", "90000")]
    [InlineData("a = 30000 : b = 30000 : Debug.Print CStr(a + b)", "60000")]
    [InlineData("a = 2000000000 : b = 3 : Debug.Print CStr(a * b)", "6000000000")]
    public async Task VariantArithmeticWidensInsteadOfOverflowing(string code, string expected)
    {
        await Run(code);
        AssertDebugLog([expected]);
    }

    [Theory]
    [InlineData("a = 30000 : b = 3 : Debug.Print TypeName(a * b)", "Long")]
    [InlineData("a = 30000 : b = 30000 : Debug.Print TypeName(a + b)", "Long")]
    [InlineData("a = 2000000000 : b = 3 : Debug.Print TypeName(a * b)", "Double")]
    public async Task TheWidenedResultReportsItsNewType(string code, string expected)
    {
        await Run(code);
        AssertDebugLog([expected]);
    }

    [Fact]
    public async Task AVariantOnEitherSideLiftsTheCeiling()
    {
        // Measured both ways round: a declared Integer beside a Variant widens.
        await Run("Dim a As Integer\r\na = 30000\r\nb = 3\r\nDebug.Print CStr(a * b)\r\nDebug.Print CStr(b * a)");
        AssertDebugLog(["90000", "90000"]);
    }

    // ── a declared type IS a ceiling ─────────────────────────────────────────

    [Fact]
    public async Task DeclaredOperandsStillOverflow()
    {
        var act = async () => await Run(
            "Dim a As Integer\r\nDim b As Integer\r\na = 30000\r\nb = 3\r\nDebug.Print CStr(a * b)");
        await act.Should().ThrowAsync<HexIDE.Runtime.Interpreter.VBRunTimeException>();
    }

    [Fact]
    public async Task ALiteralIsFixedNotAVariant()
    {
        // `Dim i As Integer : i = i + 1` must still overflow at 32767. Treating the literal as a Variant
        // would silently promote it — the reason this test exists.
        var act = async () => await Run("Dim a As Integer\r\na = 30000\r\nDebug.Print CStr(a + 30000)");
        await act.Should().ThrowAsync<HexIDE.Runtime.Interpreter.VBRunTimeException>();
    }

    [Fact]
    public async Task TwoLiteralsOverflowRatherThanWidening()
    {
        var act = async () => await Run("Debug.Print CStr(30000 * 3)");
        await act.Should().ThrowAsync<HexIDE.Runtime.Interpreter.VBRunTimeException>();
    }

    [Fact]
    public async Task FixednessPropagatesThroughASubExpression()
    {
        // The measurement that forced this onto the value rather than the operator: `(a + 0)` keeps its
        // ceiling, so multiplying it still overflows.
        var act = async () => await Run("Dim a As Integer\r\na = 30000\r\nDebug.Print CStr((a + 0) * 3)");
        await act.Should().ThrowAsync<HexIDE.Runtime.Interpreter.VBRunTimeException>();
    }

    // ── the case this was filed from ─────────────────────────────────────────

    [Fact]
    public async Task AnUntypedRecursiveFactorialWidensAsItGrows()
    {
        await Run(
            "Function Fact(n)\r\n  If n <= 1 Then\r\n    Fact = 1\r\n  Else\r\n    Fact = n * Fact(n - 1)\r\n" +
            "  End If\r\nEnd Function\r\n" +
            "Debug.Print CStr(Fact(10))\r\nDebug.Print TypeName(Fact(10))\r\nDebug.Print TypeName(Fact(13))");
        AssertDebugLog(["3628800", "Long", "Double"]);
    }
}
