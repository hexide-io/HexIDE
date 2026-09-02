using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// #190 — two defects in the built-in call machinery, both oracle-pinned against vb6.exe (see "Omitted
/// optional arguments, and string operands to numeric intrinsics" in docs/vb6-fidelity-oracle.md).
///
/// A skipped optional argument arrives as <c>Vb6Value.Missing</c>, and the guard deciding whether to use a
/// default tested for <c>EmptyVariant</c>, so the default was never selected. And every intrinsic on the
/// <c>AsDouble</c> path rejected string operands outright, where VB6 accepts numeric ones.
///
/// The second is worth the comment it carries in the code: the defect was reported as "a String operand
/// raises Err 13", which VB6 also does. Implemented as reported it would have moved HexIDE AWAY from VB6.
/// The real boundary is numeric-string versus non-numeric-string.
/// </summary>
public class OptionalArgAndNumericStringTests : BaseVBTestFixture
{
    // ── Skipped optional arguments take their default ───────────────────────────────────────────────

    [Fact]
    public async Task Split_SkippedDelimiter_UsesTheSpaceDefault()
    {
        // VB6: n=1 [a][b c] — two elements on the default space delimiter, honouring limit 2.
        await Run("Dim p\np = Split(\"a b c\", , 2)\nDebug.Print UBound(p)\nDebug.Print p(0)\nDebug.Print p(1)\n");
        AssertDebugLog([new Vb6Value(1L), new Vb6Value("a"), new Vb6Value("b c")]);   // UBound is Long (#193)
    }

    [Fact]
    public async Task Split_SkippedLimit_DoesNotRaise()
    {
        // The documented way to ask for a case-insensitive split. Raised Err 13 before: the blank slot
        // satisfied the arity test and AsInt(Missing) fell through to a throw.
        await Run("Dim p\np = Split(\"a,b,c\", \",\", , 1)\nDebug.Print UBound(p)\nDebug.Print p(0)\n");
        AssertDebugLog([new Vb6Value(2L), new Vb6Value("a")]);   // UBound is Long (#193)
    }

    [Fact]
    public async Task Split_ExplicitEmptyDelimiter_IsSuppliedAndSplitsOnNothing()
    {
        // The distinction that decides the fix: an explicitly passed Empty IS supplied, so the delimiter
        // is "" and the whole string comes back as one element. Measured n=0 [a b c]. If `Supplied` tested
        // Empty rather than Missing, this would wrongly take the space default.
        await Run("Dim e\nDim p\np = Split(\"a b c\", e, 2)\nDebug.Print UBound(p)\nDebug.Print p(0)\n");
        AssertDebugLog([new Vb6Value(0L), new Vb6Value("a b c")]);   // UBound is Long (#193)
    }

    [Fact]
    public async Task Filter_SkippedInclude_DefaultsToTrue()
    {
        // VB6: n=0 [BANANA] — include defaults True, compare 1 is case-insensitive.
        await Run("Dim r\nr = Filter(Array(\"apple\", \"BANANA\", \"cherry\"), \"an\", , 1)\n" +
                  "Debug.Print UBound(r)\nDebug.Print r(0)\n");
        AssertDebugLog([new Vb6Value(0L), new Vb6Value("BANANA")]);   // UBound is Long (#193)
    }

    [Fact]
    public async Task Join_UsesTheSpaceDefault_WhenTheDelimiterIsAbsent()
    {
        // Join shares the helper Split and Filter use. VB6 rejects a trailing `Join(a, )` as a SYNTAX
        // error, so the only way to reach the default is to omit the argument entirely.
        await Run("Dim s\ns = Join(Array(\"a\", \"b\"))\nDebug.Print s\n");
        AssertDebugLog([new Vb6Value("a b")]);
    }

    [Fact]
    public async Task InStr_SkippedCompare_DoesNotRaise()
    {
        // Same class of bug, found by auditing every arity-based optional rather than only the reported
        // three. There were 21 such sites.
        await Run("Dim n\nn = InStr(1, \"hello\", \"L\", 1)\nDebug.Print n\n");
        AssertDebugLog([new Vb6Value(3L)]);   // InStr is Long (#193)
    }

    // ── Numeric strings are valid operands ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("\"5\"", 5.0)]        // plain
    [InlineData("\" 5 \"", 5.0)]      // surrounding whitespace tolerated
    [InlineData("\"5.5\"", 5.5)]      // decimal
    [InlineData("\"-7\"", 7.0)]       // signed (Abs of it)
    [InlineData("\"+7\"", 7.0)]
    [InlineData("\"&H10\"", 16.0)]    // hex string
    [InlineData("\"1e2\"", 100.0)]    // exponent
    public async Task Abs_OfANumericString_Works(string literal, double expected)
    {
        await Run("Debug.Print Abs(" + literal + ")\n");
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Fact]
    public async Task Abs_OfANumericString_IsADouble()
    {
        // Measured: TypeName(Abs("5")) is "Double" — a string operand coerces to Double, not to the
        // "natural" type of the digits.
        await Run("Debug.Print TypeName(Abs(\"5\"))\n");
        AssertDebugLog([new Vb6Value("Double")]);
    }

    [Theory]
    [InlineData("Abs(\"abc\")")]
    [InlineData("Abs(\"\")")]
    [InlineData("Abs(\"5abc\")")]     // strict: unlike Val, trailing garbage is NOT tolerated
    public async Task Abs_OfANonNumericString_IsTypeMismatch(string call)
    {
        await Run("On Error Resume Next\nDim x\nx = " + call + "\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(13L)]);
    }

    // These cover the AsInt path rather than AsDouble. Added because mutation testing proved nothing
    // exercised it: breaking AsInt's string branch left the whole suite green.
    [Fact]
    public async Task IntegerValuedArguments_AlsoAcceptNumericStrings()
    {
        // Measured: Left("hello","3")="hel", Mid("hello","2","3")="ell", InStr("1","hello","l")=3,
        // Round(2.345,"2")=2.35, and a hex string works here too — Left("hello","&H3")="hel".
        await Run("Debug.Print Left(\"hello\", \"3\")\n" +
                  "Debug.Print Mid(\"hello\", \"2\", \"3\")\n" +
                  "Debug.Print InStr(\"1\", \"hello\", \"l\")\n" +
                  "Debug.Print Round(2.345, \"2\")\n" +
                  "Debug.Print Left(\"hello\", \"&H3\")\n");
        // InStr is Long — fixed in #193, which this comment used to point forward to while asserting the
        // wrong Integer. Left as a note because the pointer worked: the divergence was recorded at the
        // exact spot someone would otherwise have "corrected" the test to match the bug.
        AssertDebugLog([new Vb6Value("hel"), new Vb6Value("ell"), new Vb6Value(3L),
                        new Vb6Value(2.35), new Vb6Value("hel")]);
    }

    [Fact]
    public async Task IntegerValuedArgument_OfANonNumericString_IsTypeMismatch()
    {
        await Run("On Error Resume Next\nDim s\ns = Left(\"hello\", \"abc\")\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(13L)]);
    }

    [Fact]
    public async Task TheWholeAsDoubleFamily_AcceptsNumericStrings()
    {
        // One shared path, so one fix covers all of them. Sqr("9") measured as 3.
        await Run("Debug.Print Sqr(\"9\")\nDebug.Print Sgn(\"-3\")\nDebug.Print Hex(\"255\")\n");
        AssertDebugLog([new Vb6Value(3.0), new Vb6Value(-1), new Vb6Value("FF")]);
    }

    [Fact]
    public async Task HexAndOctalStrings_CoerceEverywhere_NotJustInIntrinsics()
    {
        // ToNum is shared with CDbl and coercion-on-store, so extending it there covers all three —
        // measured: CDbl("&H10") is 16, `Dim i As Integer : i = "&H10"` stores 16, "&O17" is 15.
        await Run("Debug.Print CDbl(\"&H10\")\n" +
                  "Dim i As Integer\ni = \"&H10\"\nDebug.Print i\n" +
                  "Dim j As Integer\nj = \"&O17\"\nDebug.Print j\n");
        AssertDebugLog([new Vb6Value(16.0), new Vb6Value(16), new Vb6Value(15)]);
    }
}
