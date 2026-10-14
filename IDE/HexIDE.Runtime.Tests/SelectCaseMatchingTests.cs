using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// How <c>Select Case</c> decides that an arm matches — every expectation here measured against real
/// vb6.exe (see <i>Select Case matching</i> in docs/vb6-fidelity-oracle.md).
///
/// <para>
/// <b>The rule.</b> For a selector with a FIXED type, a declared variable, a literal, a typed Function or
/// parameter, or a typed field or array element, VB6 converts the CASE EXPRESSION into the selector's type and
/// then compares. It does not compare numerically, and it is not symmetric. A Variant selector is not converted
/// at all, and compares as VB6 compares Variants; that half was measured in #839's review and is at the end of
/// this file. These tests' first measurements were all of declared selectors, which is how the rule came to be
/// written for every selector. The comparison used to be
/// <c>Vb6Value.Equals</c> — type-identical — so a <c>Long</c> selector never matched a bare Integer
/// literal, and since a <c>Select Case</c> in which nothing matches legitimately does nothing, the whole
/// defect was silent. With a <c>Case Else</c> present it read as "the wrong branch was chosen"; without
/// one, as "nothing happened".
/// </para>
///
/// <para>
/// <b>Why the existing suite missed it for so long.</b> <c>StatementTests</c> selects on a bare literal, so
/// both sides are Integer; <c>WideningTests</c> uses <c>To</c> ranges, which went through
/// <c>TryCompareTo</c> and its cross-type path; and <c>SplitKeywordTests.AContinuationMaySplitSelectCase</c>
/// is one of the failing corpus cases character-for-character except that it declares <c>Dim n</c>, a
/// Variant holding an Integer, instead of <c>Dim n As Long</c>. One keyword away, and green. These tests
/// therefore vary the DECLARED TYPE deliberately — that is the axis the defect lived on.
/// </para>
/// </summary>
public class SelectCaseMatchingTests : BaseVBTestFixture
{
    [Fact]
    public async Task ALongSelectorMatchesABareIntegerLiteral()
    {
        // The base case of the whole cluster. `1` is an Integer literal; `n` is a Long. Type-identical
        // equality said no, VB6 says yes.
        await Run("Dim n As Long\nn = 1\nSelect Case n\nCase 1: Debug.Print \"MATCH\"\nCase Else: Debug.Print \"ELSE\"\nEnd Select");
        AssertDebugLog([new Vb6Value("MATCH")]);
    }

    [Fact]
    public async Task AnIntegerSelectorMatchesAnExplicitlyLongLiteral()
    {
        await Run("Dim n As Integer\nn = 1\nSelect Case n\nCase 1&: Debug.Print \"MATCH\"\nCase Else: Debug.Print \"ELSE\"\nEnd Select");
        AssertDebugLog([new Vb6Value("MATCH")]);
    }

    [Fact]
    public async Task ADoubleAndACurrencySelectorBothMatchABareIntegerLiteral()
    {
        await Run("Dim d As Double\nd = 1\nSelect Case d\nCase 1: Debug.Print \"D\"\nEnd Select\n"
                + "Dim c As Currency\nc = 1\nSelect Case c\nCase 1: Debug.Print \"C\"\nEnd Select");
        AssertDebugLog([new Vb6Value("D"), new Vb6Value("C")]);
    }

    [Fact]
    public async Task ALongSelectorMatchesAFractionalCaseByRoundingIt()
    {
        // THE case that proves the rule is "coerce toward the selector" and not "compare numerically":
        // CLng(1.7) is 2 and matches, where a numeric comparison of 2 against 1.7 would not.
        await Run("Dim n As Long\nn = 2\nSelect Case n\nCase 1.7: Debug.Print \"MATCH\"\nCase Else: Debug.Print \"ELSE\"\nEnd Select");
        AssertDebugLog([new Vb6Value("MATCH")]);
    }

    [Fact]
    public async Task AStringSelectorDoesNotMatchANumericallyEqualButTextuallyDifferentCase()
    {
        // The other end of the same rule, and the reason Select Case may NOT share a comparison helper with
        // the `=` operator: CStr(1) is "1", which is not "1.0" — while `"1.0" = 1` is True in VB6.
        await Run("Dim s As String\ns = \"1.0\"\nSelect Case s\nCase 1: Debug.Print \"MATCH\"\nCase Else: Debug.Print \"ELSE\"\nEnd Select");
        AssertDebugLog([new Vb6Value("ELSE")]);
    }

    [Fact]
    public async Task ANumericSelectorMatchesAStringCaseThatParsesToIt()
    {
        await Run("Dim n As Long\nn = 1\nSelect Case n\nCase \"1.0\": Debug.Print \"MATCH\"\nCase Else: Debug.Print \"ELSE\"\nEnd Select");
        AssertDebugLog([new Vb6Value("MATCH")]);
    }

    [Fact]
    public async Task ABooleanSelectorMatchesItsNumericValue()
    {
        await Run("Dim b As Boolean\nb = True\nSelect Case b\nCase -1: Debug.Print \"MATCH\"\nCase Else: Debug.Print \"ELSE\"\nEnd Select");
        AssertDebugLog([new Vb6Value("MATCH")]);
    }

    [Fact]
    public async Task AnUnassignedVariantMatchesZero()
    {
        // Empty has not decided what it is yet, so it takes its partner's zero — the same rule `=` applies.
        await Run("Dim v\nSelect Case v\nCase 0: Debug.Print \"ZERO\"\nCase Else: Debug.Print \"ELSE\"\nEnd Select");
        AssertDebugLog([new Vb6Value("ZERO")]);
    }

    [Fact]
    public async Task ASelectorExpressionIsJudgedOnItsRuntimeTypeNotItsDeclaredOne()
    {
        // Arithmetic promotes, so `n * 1` need not still be a Long. A fix keyed to declared types would
        // pass every other test here and fail this one.
        await Run("Dim n As Long\nn = 1\nSelect Case n * 1\nCase 1: Debug.Print \"MATCH\"\nCase Else: Debug.Print \"ELSE\"\nEnd Select");
        AssertDebugLog([new Vb6Value("MATCH")]);
    }

    [Fact]
    public async Task CaseIsEqualsCoercesTheSameWay()
    {
        // A different code path from a plain `Case v` — it used the same type-strict Equals and so failed
        // the same way, but had to be measured rather than assumed to follow.
        await Run("Dim n As Long\nn = 1\nSelect Case n\nCase Is = 1: Debug.Print \"MATCH\"\nCase Else: Debug.Print \"ELSE\"\nEnd Select");
        AssertDebugLog([new Vb6Value("MATCH")]);
    }

    [Fact]
    public async Task CaseIsNotEqualsDoesNotFireWhenTheValuesAreEqual()
    {
        // The one arm where type-strict equality was a false POSITIVE: `Is <> 1` fired for a Long 1 because
        // the types differed. It fails in the opposite direction to every other case, so a fix aimed only
        // at things that failed to match would leave it broken.
        await Run("Dim n As Long\nn = 1\nSelect Case n\nCase Is <> 1: Debug.Print \"NOT EQUAL\"\nCase Else: Debug.Print \"EQUAL\"\nEnd Select");
        AssertDebugLog([new Vb6Value("EQUAL")]);
    }

    [Fact]
    public async Task AMatchingArmWithAnEmptyBodyDoesNothingAndDoesNotFallThrough()
    {
        // Latent until the comparison was widened: no arm ever matched, so an absent body was never
        // reached. `block()` is null for `Case 1` with nothing under it, and visiting null throws.
        await Run("Dim n As Long\nn = 1\nSelect Case n\nCase 1\nCase 2: Debug.Print \"TWO\"\nEnd Select\nDebug.Print \"AFTER\"");
        AssertDebugLog([new Vb6Value("AFTER")]);
    }

    [Fact]
    public async Task AMatchingArmWithAnEmptyBodyDoesNotFallThroughToCaseElse()
    {
        // The same trap with a Case Else present: an implementation treating an empty body as "no match"
        // would print ELSE here, which the previous test alone cannot detect.
        await Run("Dim n As Long\nn = 1\nSelect Case n\nCase 1\nCase Else: Debug.Print \"ELSE\"\nEnd Select\nDebug.Print \"AFTER\"");
        AssertDebugLog([new Vb6Value("AFTER")]);
    }

    [Fact]
    public async Task TheFirstOfSeveralMatchingArmsWins()
    {
        await Run("Dim n As Long\nn = 1\nSelect Case n\nCase 1: Debug.Print \"FIRST\"\nCase 1: Debug.Print \"SECOND\"\nEnd Select");
        AssertDebugLog([new Vb6Value("FIRST")]);
    }

    [Fact]
    public async Task ACoercionThatOverflowsRaisesRatherThanFailingToMatch()
    {
        // CInt(40000) cannot succeed, and VB6 raises rather than treating the arm as a non-match. Measured
        // twice: unhandled it puts up a modal (the capture harness recorded it as `hung`), and under
        // On Error Resume Next Err.Number is 6.
        var act = async () => await Run("Dim n As Integer\nn = 1\nSelect Case n\nCase 40000: Debug.Print \"MATCH\"\nEnd Select");
        (await act.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(6);
    }

    [Fact]
    public async Task ACoercionOfUnparseableTextRaisesTypeMismatch()
    {
        var act = async () => await Run("Dim n As Long\nn = 1\nSelect Case n\nCase \"abc\": Debug.Print \"MATCH\"\nEnd Select");
        (await act.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(13);
    }

    [Fact]
    public async Task CoercionTowardAStringSelectorNeverRaises()
    {
        // CStr cannot fail, so this direction has no error path at all — it simply does not match. That
        // asymmetry is what lets the implementation skip a guard on the String side.
        await Run("Dim s As String\ns = \"x\"\nSelect Case s\nCase 40000: Debug.Print \"MATCH\"\nCase Else: Debug.Print \"ELSE\"\nEnd Select");
        AssertDebugLog([new Vb6Value("ELSE")]);
    }

    [Fact]
    public async Task TheLayoutOfTheStatementIsIrrelevantToWhichArmIsChosen()
    {
        // The seven corpus cases that exposed all of this looked like a layout defect — colon-joined,
        // continuation-split, blank line before the first Case — and every one of them was really this
        // same type-strict comparison. Kept as a regression guard for the misdiagnosis as much as the bug.
        await Run("Dim n As Long\nn = 1\nSelect Case n : Case 1 : Debug.Print \"A\" : Case Else : Debug.Print \"B\" : End Select");
        AssertDebugLog([new Vb6Value("A")]);
    }

    [Fact]
    public async Task ABlankLineBeforeTheFirstCaseDoesNotChangeTheChosenArm()
    {
        await Run("Dim x As Long\nx = 2\nSelect Case x\n\nCase 2\nDebug.Print \"two\"\nEnd Select");
        AssertDebugLog([new Vb6Value("two")]);
    }

    // Round two of #839's review (2026-10-13). Every row below is a program built as native code and as p-code, the two
    // agreeing on each, and generated from those probes and their logs rather than written out by hand; the probes are
    // in docs/vb6-fidelity-oracle.md, "A typed selector and a Variant one". A selector with a fixed type converts the
    // Case value into its type. A Variant selector does not, and compares as VB6 compares a Variant with a typed or a
    // Variant value. Round one converted into whatever type the selector's value had, so a Variant 5 was not
    // `Case Is < 5.4` and a Variant against `Is > "a"` stopped the run with 13.
    private const string Helpers = """
        Type T
            n As Integer
        End Type
        Type TV
            x As Variant
        End Type
        Function F() As Integer
            F = 5
        End Function
        Function FV()
            FV = 5
        End Function
        Function FS() As String
            FS = "10"
        End Function
        """;

    private static string CaseProgram(string declarations, string setup, string selector, string test) =>
        $"S\nSub S()\n    On Error Resume Next\n    {declarations}\n    Dim r As String\n    {setup}\n    r = \"none\"\n"
        + $"    Select Case {selector}\n    Case {test}\n        r = \"case\"\n    Case Else\n        r = \"else\"\n    End Select\n"
        + $"    Debug.Print r & \" | \" & Err.Number\nEnd Sub\n" + Helpers;

    [Theory]
    [InlineData("Dim v As Integer", "v = 5", "v", "Is < 5.4", "else | 0")]
    [InlineData("Dim v As Double", "v = 5.5", "v", "Is > \"a\"", "case | 13")]
    [InlineData("Dim b As Boolean", "b = False", "b", "Is < 1", "else | 0")]
    [InlineData("Dim b As Boolean", "b = True", "b", "Is < 0", "case | 0")]
    [InlineData("Dim b As Boolean", "b = True", "b", "3 To 6", "case | 0")]
    [InlineData("Dim b As Boolean", "b = True", "b", "Is >= 5.6", "case | 0")]
    [InlineData("Dim b As Boolean", "b = True", "b", "Is <= 4.6", "case | 0")]
    [InlineData("Dim b As Boolean", "b = True", "b", "4.6 To 5.4", "case | 0")]
    [InlineData("Dim b As Boolean", "b = True", "b", "5", "case | 0")]
    [InlineData("Dim b As Boolean", "b = True", "b", "Is > \"a\"", "case | 13")]
    [InlineData("Dim b As Boolean", "b = True", "b", "Is < False", "case | 0")]
    [InlineData("Dim b As Boolean", "b = False", "b", "Is > True", "case | 0")]
    [InlineData("Dim d", "", "F()", "Is < 5.4", "else | 0")]
    [InlineData("Dim a(1) As Integer", "a(0) = 5", "a(0)", "Is < 5.4", "else | 0")]
    [InlineData("Dim u As T", "u.n = 5", "u.n", "Is < 5.4", "else | 0")]
    [InlineData("Dim i As Integer", "i = 4", "i + 1", "Is < 5.4", "else | 0")]
    [InlineData("Dim v", "v = 5", "CInt(v)", "Is < 5.4", "else | 0")]
    [InlineData("Dim d", "", "5", "Is < 5.4", "else | 0")]
    [InlineData("Dim d", "", "\"10\"", "Is > 9", "else | 0")]
    [InlineData("Dim d", "", "FS()", "Is > 9", "else | 0")]
    [InlineData("Dim v", "v = \"10\"", "CStr(v)", "Is > 9", "else | 0")]
    [InlineData("Dim n As Integer, w", "n = 5: w = \"5\"", "n", "w", "case | 0")]
    [InlineData("Dim s As String, w", "s = \"5\": w = 5", "s", "w", "case | 0")]
    public async Task A_typed_selector_converts_each_Case_value_into_its_type(string declarations, string setup, string selector, string test, string expected)
    {
        await Run(CaseProgram(declarations, setup, selector, test));

        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("Dim v", "v = 5", "v", "Is < 5.4", "case | 0")]
    [InlineData("Dim v", "v = CLng(5)", "v", "Is < 5.4", "case | 0")]
    [InlineData("Dim v", "v = 0", "v", "Is < 0.5", "case | 0")]
    [InlineData("Dim v", "v = 5", "v", "Is <= 4.6", "else | 0")]
    [InlineData("Dim v", "v = 5", "v", "5.4 To 5.6", "else | 0")]
    [InlineData("Dim v", "v = 5", "v", "Is > \"a\"", "else | 0")]
    [InlineData("Dim v", "v = 5", "v", "\"a\" To \"c\"", "else | 0")]
    [InlineData("Dim v", "v = 5.5", "v", "Is > \"a\"", "else | 0")]
    [InlineData("Dim v", "v = \"10\"", "v", "Is < 9", "else | 0")]
    [InlineData("Dim v", "v = \"10\"", "v", "Is < 5.4", "else | 0")]
    [InlineData("Dim v", "v = \"abc\"", "v", "Is > 5", "case | 0")]
    [InlineData("Dim v", "v = 5", "v", "Is = \"5\"", "case | 0")]
    [InlineData("Dim v", "v = True", "v", "Is < 0", "case | 0")]
    [InlineData("Dim v", "v = False", "v", "Is < 1", "case | 0")]
    [InlineData("Dim v", "v = 5", "v", "Is < \"10\"", "else | 0")]
    [InlineData("Dim v", "v = 10", "v", "Is > \"9\"", "else | 0")]
    [InlineData("Dim v, w", "v = 10: w = \"9\"", "v", "Is > w", "else | 0")]
    [InlineData("Dim v, w", "v = 5: w = \"10\"", "v", "Is < w", "case | 0")]
    [InlineData("Dim v, w", "v = 5: w = \"abc\"", "v", "Is < w", "case | 0")]
    [InlineData("Dim v", "v = \"3\"", "v", "Is < 9", "case | 0")]
    [InlineData("Dim v", "v = \"abc\"", "v", "Is < 5", "else | 0")]
    [InlineData("Dim v", "v = \"10\"", "v", "Is > 9", "case | 0")]
    [InlineData("Dim v, w", "v = \"10\": w = 9", "v", "Is > w", "case | 0")]
    [InlineData("Dim v, w", "v = \"abc\": w = 5", "v", "Is > w", "case | 0")]
    [InlineData("Dim v", "v = 5", "v", "Is < \"a\"", "case | 0")]
    [InlineData("Dim v", "v = 100", "v", "Is > \"9\"", "else | 0")]
    [InlineData("Dim v", "v = 2", "v", "1.7", "else | 0")]
    [InlineData("Dim v", "v = 1", "v", "\"1.0\"", "else | 0")]
    [InlineData("Dim v", "v = \"1.0\"", "v", "1", "case | 0")]
    [InlineData("Dim v", "v = \"abc\"", "v", "5", "else | 0")]
    [InlineData("Dim v", "v = 5", "v", "\"abc\"", "else | 0")]
    [InlineData("Dim v", "v = True", "v", "1", "else | 0")]
    [InlineData("Dim v", "v = True", "v", "-1", "case | 0")]
    [InlineData("Dim v", "v = True", "v", "\"True\"", "case | 0")]
    [InlineData("Dim v", "v = 5", "v", "Is <> \"abc\"", "case | 0")]
    [InlineData("Dim v", "v = 2", "v", "Is = 1.7", "else | 0")]
    [InlineData("Dim v", "v = 4", "v + 1", "Is < 5.4", "case | 0")]
    [InlineData("Dim i As Integer, v", "i = 4: v = 1", "i + v", "Is < 5.4", "case | 0")]
    [InlineData("Dim v(1)", "v(0) = 5", "v(0)", "Is < 5.4", "case | 0")]
    [InlineData("Dim d", "", "FV()", "Is < 5.4", "case | 0")]
    [InlineData("Dim v", "v = 5.5", "v", "Is < 6", "case | 0")]
    [InlineData("Dim v", "v = 2.5", "v", "2", "else | 0")]
    [InlineData("Dim v", "v = 2.5", "v", "2 To 2", "else | 0")]
    [InlineData("Dim d", "", "Mid(\"10\", 1)", "Is > 9", "case | 0")]
    [InlineData("Dim v, w", "v = 5: w = \"5\"", "v", "w", "else | 0")]
    [InlineData("Dim v, w", "v = \"5\": w = 5", "v", "w", "else | 0")]
    [InlineData("Dim v, w", "v = 5: w = 5", "v", "w", "case | 0")]
    [InlineData("Dim v, w", "v = 5: w = Empty", "v", "Is > w", "case | 0")]
    [InlineData("Dim v, w", "v = \"a\": w = Empty", "v", "Is > w", "case | 0")]
    [InlineData("Dim v, w", "v = 0: w = Empty", "v", "w", "case | 0")]
    [InlineData("Dim v", "v = Null", "v", "Null", "else | 0")]
    [InlineData("Dim v", "v = Null", "v", "Is < 1", "else | 0")]
    [InlineData("Dim v", "v = 5", "v", "Is > Empty", "case | 0")]
    [InlineData("Dim v", "v = \"abc\"", "v", "Is > Empty", "case | 0")]
    [InlineData("Dim v", "v = 5", "v", "Is > True", "case | 0")]
    [InlineData("Dim v", "v = \"True\"", "v", "True", "case | 0")]
    [InlineData("Dim v", "v = \"-1\"", "v", "True", "case | 0")]
    [InlineData("Dim v", "v = #1/2/1900#", "v", "Is > 2", "case | 0")]
    [InlineData("Dim v", "v = \"2.5\"", "v", "2", "else | 0")]
    [InlineData("Dim v", "v = \"2.6\"", "v", "Is < 3", "case | 0")]
    [InlineData("Dim v", "v = \"70000\"", "v", "Is > 5", "case | 0")]
    [InlineData("Dim v", "v = \"True\"", "v", "-1", "else | 0")]
    [InlineData("Dim v", "v = \"True\"", "v", "Is < 0", "else | 0")]
    [InlineData("Dim v", "v = \"abc\"", "v", "True", "else | 0")]
    [InlineData("Dim v", "v = \"1/2/1900\"", "v", "Is < 5", "else | 0")]
    [InlineData("Dim v", "v = True", "v", "\"-1\"", "else | 0")]
    [InlineData("Dim v", "v = \"2.5\"", "v", "2.5", "case | 0")]
    [InlineData("Dim v", "v = \"5\"", "v", "5&", "case | 0")]
    [InlineData("Dim v", "v = \"0.5\"", "v", "Is < 1", "case | 0")]
    [InlineData("Dim v", "v = \"1.5\"", "v", "Is > 1", "case | 0")]
    [InlineData("Dim v, w As String", "v = 5: w = \"10\"", "v", "Is < w", "else | 0")]
    [InlineData("Dim v, k As Integer", "v = \"3\": k = 9", "v", "Is > k", "else | 0")]
    [InlineData("Dim v", "v = 5", "v", "Is > FS()", "case | 0")]
    [InlineData("Dim v, w", "v = \"3\": w = 9", "v", "Is < w", "else | 0")]
    [InlineData("Dim u As TV", "u.x = 5", "u.x", "Is < 5.4", "case | 0")]
    [InlineData("Dim v", "v = Null", "v", "Is <> 1", "else | 0")]
    [InlineData("Dim v", "v = \"5\"", "v", "True", "case | 0")]
    [InlineData("Dim v", "v = 0", "v", "Is <> \"abc\"", "case | 0")]
    [InlineData("Dim v", "", "v", "\"\"", "case | 0")]
    [InlineData("Dim v", "", "v", "Is < \" \"", "case | 0")]
    [InlineData("Dim v, w", "v = \"\"", "v", "w", "case | 0")]
    [InlineData("Dim v, w", "v = \" \"", "v", "Is > w", "case | 0")]
    [InlineData("Dim v", "v = \"\"", "v", "Is < Empty", "else | 0")]
    [InlineData("Dim v", "v = \" \"", "v", "Is > Empty", "case | 0")]
    public async Task A_Variant_selector_takes_each_Case_value_as_it_is(string declarations, string setup, string selector, string test, string expected)
    {
        await Run(CaseProgram(declarations, setup, selector, test));

        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("Dim o As Foo", "Set o = New Foo: o.V = 5", "o.V", "Is < 5.4", "case | 0")]
    [InlineData("Dim o As Foo", "Set o = New Foo: o.N = 5", "o.N", "Is < 5.4", "else | 0")]
    public async Task A_class_field_selector_is_typed_as_the_field_is_declared(string declarations, string setup, string selector, string test, string expected)
    {
        await RunClasses(CaseProgram(declarations, setup, selector, test), ("Foo", "Public V As Variant\nPublic N As Integer\n"));

        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("Integer", "5", "Is < 5.4", "else | 0")]
    [InlineData("String", "\"10\"", "Is > 9", "else | 0")]
    [InlineData("Boolean", "False", "Is < 1", "else | 0")]
    public async Task A_typed_parameter_is_a_typed_selector(string type, string argument, string test, string expected)
    {
        await Run($"P {argument}\nSub P(ByVal p As {type})\n    On Error Resume Next\n    Dim r As String\n    r = \"none\"\n"
            + $"    Select Case p\n    Case {test}\n        r = \"case\"\n    Case Else\n        r = \"else\"\n    End Select\n"
            + "    Debug.Print r & \" | \" & Err.Number\nEnd Sub\n");

        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("F() * 7000", "6 ")]
    [InlineData("a(0) * 7000", "6 ")]
    [InlineData("u.n * 7000", "6 ")]
    [InlineData("CInt(v) * 7000", "6 ")]
    [InlineData("v(0) * 7000", "0 35000")]
    [InlineData("u.x * 7000", "0 35000")]
    public async Task A_value_is_typed_by_what_holds_it_in_arithmetic_too(string expression, string expected)
    {
        // The other observer of the same flag: an Integer overflows at 32767, where a Variant widens (#122). 6 with no
        // value, or 0 and the widened value.
        await Run("On Error Resume Next\nDim a(1) As Integer, u As T, v, x, w(1), uv As TV\n"
            + "a(0) = 5: u.n = 5: v = 5: w(0) = 5: uv.x = 5\n"
            + $"x = {expression.Replace("v(0)", "w(0)").Replace("u.x", "uv.x")}\nDebug.Print Err.Number & \" \" & x\n" + Helpers);

        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("o.V * 7000", "0 35000")]
    [InlineData("o.N * 7000", "6 ")]
    public async Task A_class_field_is_typed_as_declared_in_arithmetic_too(string expression, string expected)
    {
        await RunClasses($"On Error Resume Next\nDim o As Foo, x\nSet o = New Foo: o.V = 5: o.N = 5\nx = {expression}\nDebug.Print Err.Number & \" \" & x\n",
            ("Foo", "Public V As Variant\nPublic N As Integer\n"));

        AssertDebugLog([new Vb6Value(expected)]);
    }

    // #987: which intrinsics VB6 types and which return a Variant, generated from the probe and its log (native and p-code
    // agreeing; the oracle's "Which intrinsics are typed" has the table). A typed Integer or Long result converts
    // `Case Is < N.4` into N and takes Case Else, and overflows past its ceiling; a Variant takes the Case and widens.
    // Not here: `IIf`, `Choose` and `Switch`, measured as Variants and not implemented; `Len` of a typed Integer, measured
    // typed, whose value HexIDE gets wrong (#780); the `$` forms (#742).
    private const string IntrinsicSetup =
        "Dim vs, vi, vn, v5, va, v52, n As Integer, n5 As Integer, arr(5), arr5(5 To 6)\n"
        + "vs = \"abcde\": vi = \"abcd5\": vn = -5: v5 = 5: va = \"A\": v52 = 5.2: n = -5: n5 = 5\n";

    [Theory]
    [InlineData("Len(\"abcde\")", "5.4", "500000000", "Else err 0; *500000000: err 6 Empty")]
    [InlineData("Len(vs)", "5.4", "500000000", "Case err 0; *500000000: err 0 Double")]
    [InlineData("InStr(\"abcd5\", \"5\")", "5.4", "500000000", "Else err 0; *500000000: err 6 Empty")]
    [InlineData("InStr(vi, \"5\")", "5.4", "500000000", "Case err 0; *500000000: err 0 Double")]
    [InlineData("Abs(-5)", "5.4", "7000", "Else err 0; *7000: err 6 Empty")]
    [InlineData("Abs(vn)", "5.4", "7000", "Case err 0; *7000: err 0 Long")]
    [InlineData("Abs(n)", "5.4", "7000", "Else err 0; *7000: err 6 Empty")]
    [InlineData("Sgn(5)", "1.4", "7000", "Else err 0; *7000: err 0 Integer")]
    [InlineData("Sgn(v5)", "1.4", "7000", "Else err 0; *7000: err 0 Integer")]
    [InlineData("Asc(\"A\")", "65.4", "7000", "Else err 0; *7000: err 6 Empty")]
    [InlineData("Asc(va)", "65.4", "7000", "Else err 0; *7000: err 6 Empty")]
    [InlineData("UBound(arr)", "5.4", "500000000", "Else err 0; *500000000: err 6 Empty")]
    [InlineData("LBound(arr5)", "5.4", "500000000", "Else err 0; *500000000: err 6 Empty")]
    [InlineData("CInt(5.2)", "5.4", "7000", "Else err 0; *7000: err 6 Empty")]
    [InlineData("CInt(v52)", "5.4", "7000", "Else err 0; *7000: err 6 Empty")]
    [InlineData("CLng(5.2)", "5.4", "500000000", "Else err 0; *500000000: err 6 Empty")]
    [InlineData("CLng(v52)", "5.4", "500000000", "Else err 0; *500000000: err 6 Empty")]
    [InlineData("CByte(5.2)", "5.4", "7000", "Else err 0; *7000: err 6 Empty")]
    [InlineData("Hour(#5:00:00 AM#)", "5.4", "7000", "Case err 0; *7000: err 0 Long")]
    [InlineData("Minute(#12:05:00 AM#)", "5.4", "7000", "Case err 0; *7000: err 0 Long")]
    [InlineData("Second(#12:00:05 AM#)", "5.4", "7000", "Case err 0; *7000: err 0 Long")]
    [InlineData("Day(#1/5/2000#)", "5.4", "7000", "Case err 0; *7000: err 0 Long")]
    [InlineData("Month(#5/1/2000#)", "5.4", "7000", "Case err 0; *7000: err 0 Long")]
    [InlineData("Year(#1/1/2000#)", "2000.4", "7000", "Case err 0; *7000: err 0 Long")]
    [InlineData("Weekday(#1/6/2000#)", "5.4", "7000", "Case err 0; *7000: err 0 Long")]
    [InlineData("Int(5.7)", "5.4", "7000", "Case err 0; *7000: err 0 Double")]
    [InlineData("Fix(5.7)", "5.4", "7000", "Case err 0; *7000: err 0 Double")]
    [InlineData("Int(n5)", "5.4", "7000", "Else err 0; *7000: err 6 Empty")]
    [InlineData("Fix(n5)", "5.4", "7000", "Else err 0; *7000: err 6 Empty")]
    [InlineData("Round(5.2)", "5.4", "7000", "Case err 0; *7000: err 0 Double")]
    [InlineData("Val(\"5\")", "5.4", "7000", "Case err 0; *7000: err 0 Double")]
    public async Task A_numeric_intrinsic_is_typed_or_a_Variant_as_VB6_returns_it(string expression, string bound, string multiplier, string expected)
    {
        await Run(IntrinsicSetup + "P\nSub P()\n    On Error Resume Next\n    Dim r As String, x\n    r = \"?\"\n"
            + $"    Select Case {expression}\n        Case Is < {bound}\n            r = \"Case\"\n        Case Else\n            r = \"Else\"\n    End Select\n"
            + "    r = r & \" err \" & Err.Number\n    Err.Clear\n"
            + $"    x = {expression} * {multiplier}\n    r = r & \"; *{multiplier}: err \" & Err.Number & \" \" & TypeName(x)\n    Debug.Print r\nEnd Sub\n");

        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("Mid(\"10\", 1)", "Case err 0 String")]
    [InlineData("Left(\"10\", 2)", "Case err 0 String")]
    [InlineData("Right(\"10\", 2)", "Case err 0 String")]
    [InlineData("Trim(\" 10 \")", "Case err 0 String")]
    [InlineData("LCase(\"10\")", "Case err 0 String")]
    [InlineData("UCase(\"10\")", "Case err 0 String")]
    [InlineData("Hex(16)", "Case err 0 String")]
    [InlineData("Oct(8)", "Case err 0 String")]
    [InlineData("String(2, \"1\")", "Case err 0 String")]
    [InlineData("Format(10)", "Case err 0 String")]
    [InlineData("CStr(10)", "Else err 0 String")]
    [InlineData("Replace(\"1x\", \"x\", \"0\")", "Else err 0 String")]
    [InlineData("StrReverse(\"01\")", "Else err 0 String")]
    [InlineData("LTrim(\"10\")", "Case err 0 String")]
    [InlineData("Chr(49) & \"0\"", "Case err 0 String")]
    [InlineData("\"1\" & \"0\"", "Else err 0 String")]
    public async Task A_text_intrinsic_is_typed_or_a_Variant_as_VB6_returns_it(string expression, string expected)
    {
        // A typed String converts 9 into "9" and compares text, so "10" is not above it; a Variant is read as a number.
        await Run("P\nSub P()\n    On Error Resume Next\n    Dim r As String\n    r = \"?\"\n"
            + $"    Select Case {expression}\n        Case Is > 9\n            r = \"Case\"\n        Case Else\n            r = \"Else\"\n    End Select\n"
            + $"    r = r & \" err \" & Err.Number & \" \" & TypeName({expression})\n    Debug.Print r\nEnd Sub\n");

        AssertDebugLog([new Vb6Value(expected)]);
    }
}
