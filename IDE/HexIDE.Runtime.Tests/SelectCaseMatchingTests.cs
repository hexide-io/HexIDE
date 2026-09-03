using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// How <c>Select Case</c> decides that an arm matches — every expectation here measured against real
/// vb6.exe (see <i>Select Case matching</i> in docs/vb6-fidelity-oracle.md).
///
/// <para>
/// <b>The rule.</b> VB6 coerces the CASE EXPRESSION to the SELECTOR's runtime type and then compares. It
/// does not compare numerically, and it is not symmetric. The comparison used to be
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
}
