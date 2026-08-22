using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Issue #124 — a variable declared <c>As T</c> holds a T.
///
/// <para>
/// <c>Dim</c> always seeded a correctly-typed zero; the ASSIGNMENT then overwrote the slot with whatever
/// subtype the right-hand side happened to have, and the declared type was gone. So <c>Dim l As Long : l =
/// 3</c> left <c>TypeName(l)</c> reporting "Integer", and every rule VB6 draws from declaredness drew it
/// from the wrong side.
/// </para>
///
/// <para>
/// It is the keystone under three separate behaviours, which is why it was worth doing before any of them:
/// the arithmetic ladder (<c>declInt * declLong</c> is a Long and does not overflow), Variant overflow
/// promotion (#122 — a declared type is a ceiling, a Variant is not), and the division-by-zero error number
/// (Err 11 declared, Err 6 on the Variant path). Only the first is settled here.
/// </para>
///
/// <para>
/// Every expectation is vb6.exe output — see <i>Declared type, Variant subtype, and overflow promotion</i>
/// and <i>Coercion-on-store</i> in docs/vb6-fidelity-oracle.md.
/// </para>
/// </summary>
public class DeclaredTypeTests : BaseVBTestFixture
{
    private async Task<string> TypeOf(string declaration, string assignment)
    {
        await Run($"Dim x As {declaration}\n{assignment}\nDebug.Print TypeName(x)\n");
        return (string)debug[^1].Value!;
    }

    // ── the declared type survives assignment ────────────────────────────────────────────────────

    [Theory]
    [InlineData("Integer",  "Integer")]
    [InlineData("Long",     "Long")]
    [InlineData("Double",   "Double")]
    [InlineData("Single",   "Single")]
    [InlineData("Byte",     "Byte")]
    [InlineData("Currency", "Currency")]
    [InlineData("Boolean",  "Boolean")]
    [InlineData("String",   "String")]
    public async Task ADeclaredVariable_ReportsItsDeclaredType(string declared, string expected)
    {
        // Integer was the only one of these that passed before, and only by coincidence — it is the subtype
        // a small literal already has, so nothing had to be retained for it to look right.
        (await TypeOf(declared, "x = 3")).Should().Be(expected);
    }

    [Fact]
    public async Task AVariantIsStillUntyped()
    {
        // The other half: `Dim v` and `Dim v As Variant` must stay exactly as untyped as they read, taking
        // the assigned value's own subtype. A declared type where there is no declaration would be the same
        // defect pointing the other way.
        await Run(
            "Dim v\nDim w As Variant\n" +
            "v = 3 : w = 3&\n" +
            "Debug.Print TypeName(v)\nDebug.Print TypeName(w)\n");
        debug[0].Value.Should().Be("Integer");
        debug[1].Value.Should().Be("Long");
    }

    // ── coercion-on-store ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AStringVariableTakesAnything() =>
        (await TypeOf("String", "x = 5")).Should().Be("String");

    [Fact]
    public async Task StoringIntoAStringConvertsTheValue()
    {
        await Run("Dim s As String\ns = 5\nDebug.Print s\ns = True\nDebug.Print s\n");
        debug[0].Value.Should().Be("5");
        debug[1].Value.Should().Be("True");
    }

    [Fact]
    public async Task StoringIntoABooleanIsCBool()
    {
        await Run(
            "Dim b As Boolean\n" +
            "b = 5 : Debug.Print b\n" +      // any non-zero is True
            "b = 0 : Debug.Print b\n" +
            "b = \"True\" : Debug.Print b\n");
        debug[0].Value.Should().Be(true);
        debug[1].Value.Should().Be(false);
        debug[2].Value.Should().Be(true);
    }

    [Fact]
    public async Task StoringAStringIntoANumberParsesIt()
    {
        // ToDouble answers 0.0 for a String rather than parsing it, so this silently stored 0 before —
        // a wrong answer, not an error.
        await Run("Dim i As Integer\ni = \"12\"\nDebug.Print i\n");
        Convert.ToInt64(debug[0].Value).Should().Be(12);
    }

    [Fact]
    public async Task StoringAnUnparseableStringIntoANumberIsError13()
    {
        await Run(
            "On Error Resume Next\nDim i As Integer\n" +
            "i = \"abc\"\nDebug.Print Err.Number\n");
        Convert.ToInt64(debug[0].Value).Should().Be(13);
    }

    [Fact]
    public async Task StoringADoubleIntoAnIntegerRoundsHalfToEven()
    {
        // 12.5 → 12, not 13. Banker's rounding, which Narrow already did — it just was not being reached.
        await Run("Dim i As Integer\ni = 12.6\nDebug.Print i\ni = 12.5\nDebug.Print i\n");
        Convert.ToInt64(debug[0].Value).Should().Be(13);
        Convert.ToInt64(debug[1].Value).Should().Be(12);
    }

    [Fact]
    public async Task StoringOutOfRangeIsError6()
    {
        await Run(
            "On Error Resume Next\nDim b As Byte\n" +
            "b = 300\nDebug.Print Err.Number\n");
        Convert.ToInt64(debug[0].Value).Should().Be(6);
    }

    [Fact]
    public async Task StoringNullIntoAnyDeclaredVariableIsError94()
    {
        // Including a String one, which is the case that would have looked like "" if the String branch had
        // been written before the Null check.
        foreach (var declared in new[] { "Integer", "String", "Boolean" })
        {
            await Run(
                "On Error Resume Next\n" +
                $"Dim x As {declared}\nx = Null\nDebug.Print Err.Number\n");
            Convert.ToInt64(debug[^1].Value).Should().Be(94, "storing Null into a declared {0}", declared);
        }
    }

    [Fact]
    public async Task StoringAnUninitialisedVariantIsZeroOrEmptyString()
    {
        // Through an un-assigned Variant rather than the `Empty` literal, which HexIDE's grammar has no
        // token for at all — a separate gap, filed. The value and therefore the coercion are the same.
        await Run(
            "Dim e\nDim i As Integer\nDim s As String\n" +
            "i = e\ns = e\nDebug.Print i\nDebug.Print \"[\" & s & \"]\"\n");
        Convert.ToInt64(debug[0].Value).Should().Be(0);
        debug[1].Value.Should().Be("[]");
    }

    // ── what the declared type is FOR: the arithmetic ladder ─────────────────────────────────────

    [Fact]
    public async Task IntegerTimesLong_IsALong_AndDoesNotOverflow()
    {
        // The row that showed the cost. Nothing was missing from the ladder — "widest wins" already makes
        // this a Long and 90000 fits — it raised Err 6 only because it believed the Long was an Integer.
        await Run(
            "Dim i As Integer, l As Long\n" +
            "i = 30000 : l = 3\n" +
            "Debug.Print i * l\nDebug.Print TypeName(i * l)\n");
        Convert.ToInt64(debug[0].Value).Should().Be(90000);
        debug[1].Value.Should().Be("Long");
    }

    [Fact]
    public async Task IntegerTimesDouble_IsADouble()
    {
        await Run("Dim i As Integer, d As Double\ni = 3 : d = 2\nDebug.Print TypeName(i * d)\n");
        debug[0].Value.Should().Be("Double");
    }

    [Fact]
    public async Task ByteTimesInteger_IsAnInteger()
    {
        await Run("Dim b As Byte, i As Integer\nb = 100 : i = 3\nDebug.Print TypeName(b * i)\n");
        debug[0].Value.Should().Be("Integer");
    }

    [Fact]
    public async Task BytePlusByte_StaysByte_AndOverflows()
    {
        // The complement of the row above, and the one that proves the ladder is not just widening
        // everything: Byte + Byte is a Byte, so 200 + 100 is Err 6 rather than 300.
        await Run(
            "On Error Resume Next\nDim b As Byte, c As Byte\n" +
            "b = 200 : c = 100\nDim v\nv = b + c\nDebug.Print Err.Number\n");
        Convert.ToInt64(debug[0].Value).Should().Be(6);
    }

    [Fact]
    public async Task ADeclaredSingleStaysSingleThroughArithmetic()
    {
        await Run("Dim s As Single\ns = 1.5\nDebug.Print TypeName(s + 1)\nDebug.Print TypeName(s * s)\n");
        debug[0].Value.Should().Be("Single");
        debug[1].Value.Should().Be("Single");
    }

    [Fact]
    public async Task AnArithmeticResultStoredBackKeepsTheDeclaredType()
    {
        await Run(
            "Dim l As Long, b As Byte\n" +
            "l = 3 : l = l + 1 : Debug.Print TypeName(l)\n" +
            "b = 3 : b = b + 1 : Debug.Print TypeName(b)\n");
        debug[0].Value.Should().Be("Long");
        debug[1].Value.Should().Be("Byte");
    }

    // ── arrays carry their element type too ──────────────────────────────────────────────────────

    [Fact]
    public async Task ArrayElementsCarryTheDeclaredElementType()
    {
        await Run(
            "Dim a(1 To 3) As Long\n" +
            "a(1) = 5\nDebug.Print TypeName(a(1))\n" +
            "a(2) = 30000\nDebug.Print a(2) * 3\n");
        debug[0].Value.Should().Be("Long");
        Convert.ToInt64(debug[1].Value).Should().Be(90000);   // widens as a Long, not overflowing an Integer
    }

    [Fact]
    public async Task AVariantArraysElementsAreStillUntyped()
    {
        await Run("Dim a(1 To 3)\na(1) = 3&\nDebug.Print TypeName(a(1))\n");
        debug[0].Value.Should().Be("Long");
    }

    // ── ByRef ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AByRefParameterSeesTheDeclaredType_AndWritesThrough()
    {
        // The declared type is keyed by SLOT, so a ByRef alias inherits the caller's with nothing to copy.
        // VB6 makes that safe by refusing at COMPILE time to bind a `ByRef x As Long` to a Variant argument
        // ("ByRef argument type mismatch" — measured while probing this), so the two sides always agree.
        await Run(
            "Dim l As Long\nl = 3\nCall Bump(l)\n" +
            "Debug.Print TypeName(l)\nDebug.Print l\n" +
            "Sub Bump(ByRef x As Long)\n  Debug.Print TypeName(x)\n  x = x + 1\nEnd Sub\n");
        debug[0].Value.Should().Be("Long");   // inside the callee
        debug[1].Value.Should().Be("Long");
        Convert.ToInt64(debug[2].Value).Should().Be(4);
    }
}
