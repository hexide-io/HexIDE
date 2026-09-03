using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// `""` inside a string literal is VB6's escape for one quote — and the only escape the language has.
/// Every expectation measured against real vb6.exe (`corpus/conformance/string-literal-escapes.json`).
///
/// <para>
/// <b>What was wrong.</b> The interpreter stripped the delimiters with <c>Substring(1, len - 2)</c> and did
/// nothing else, so `"he said ""hi"""` evaluated to `he said ""hi""`. A wrong value wherever a quoted
/// quotation appears — `MsgBox "He said ""hello"""` showed the doubles — and it had been recorded as
/// STRING-ESCAPE against a single corpus case about line continuations, which made it look like a
/// continuation quirk rather than what it was: no string literal was ever unescaped, anywhere. There is
/// exactly one place in the interpreter that reads a <c>STRINGLITERAL</c>, so exactly one place it was wrong.
/// </para>
///
/// <para>
/// <b>Order matters, and the degenerate literals are what prove it.</b> The delimiters must come off
/// before the unescape: applied to the raw token text, `""` — the EMPTY string — would become a single
/// quote. Measured, <c>Len("")</c> is 0 and <c>Len("""")</c> is 1, and those two are indistinguishable
/// until the outer pair is gone. Every case below asserts a LENGTH as well as, or instead of, the text,
/// because a printed quote is easy to misread and a length is not.
/// </para>
/// </summary>
public class StringLiteralEscapeTests : BaseVBTestFixture
{
    [Fact]
    public async Task AnEscapedQuoteInsideALiteralBecomesOneQuote()
    {
        await Run("Debug.Print \"he said \"\"hi\"\"\"\nDebug.Print Len(\"he said \"\"hi\"\"\")");
        AssertDebugLog([new Vb6Value("he said \"hi\""), new Vb6Value(12L)]);
    }

    [Fact]
    public async Task ALiteralThatIsOnlyAnEscapedQuoteIsOneCharacter()
    {
        // Four quote characters denoting one. The minimal escaped literal, and the case an off-by-one
        // unescape gets wrong while handling longer ones correctly.
        await Run("Debug.Print Len(\"\"\"\")\nDebug.Print Asc(\"\"\"\")");
        AssertDebugLog([new Vb6Value(1L), new Vb6Value(34)]);
    }

    [Fact]
    public async Task TheEmptyLiteralIsEmptyAndNotAnEscapedQuote()
    {
        // The pair that fixes the ORDER of operations: `""` is the empty string, so the unescape cannot
        // run before the delimiters come off.
        await Run("Debug.Print Len(\"\")");
        AssertDebugLog([new Vb6Value(0L)]);
    }

    [Fact]
    public async Task TwoEscapedQuotesAndNothingElseAreTwoCharacters()
    {
        // Six quote characters denoting two — confirms the unescape replaces every occurrence, which a
        // literal containing a single escape cannot show.
        await Run("Debug.Print Len(\"\"\"\"\"\")");
        AssertDebugLog([new Vb6Value(2L)]);
    }

    [Fact]
    public async Task AnEscapedQuoteMayAbutTheDelimiterAtEitherEnd()
    {
        // `"""a"""` is a quote, an a, a quote. The position where mistaking a delimiter for an escape is
        // most likely.
        await Run("Debug.Print Len(\"\"\"a\"\"\")\nDebug.Print \"\"\"a\"\"\"");
        AssertDebugLog([new Vb6Value(3L), new Vb6Value("\"a\"")]);
    }

    [Fact]
    public async Task AnEscapedQuoteMayAbutTextOnBothSides()
    {
        await Run("Debug.Print \"a\"\"b\"\nDebug.Print Len(\"a\"\"b\")");
        AssertDebugLog([new Vb6Value("a\"b"), new Vb6Value(3L)]);
    }

    [Fact]
    public async Task EachLiteralInAConcatenationIsUnescapedSeparately()
    {
        // `"a"""` is `a"` and `"""b"` is `"b`, so the join is `a""b` — two REAL adjacent quotes, which is
        // not an escape. A textual replacement over the whole statement would also produce the right
        // length here but for the wrong reason; the Chr(34) comparison below is what pins the character.
        await Run("Debug.Print \"a\"\"\" & \"\"\"b\"\nDebug.Print Len(\"a\"\"\" & \"\"\"b\")");
        AssertDebugLog([new Vb6Value("a\"\"b"), new Vb6Value(4L)]);
    }

    [Fact]
    public async Task AnEscapedQuoteIsTheSameCharacterAsChr34()
    {
        // Correct in KIND and not merely in length.
        await Run("Debug.Print (\"\"\"\" = Chr(34))");
        AssertDebugLog([new Vb6Value(true)]);
    }

    [Fact]
    public async Task AnEscapedQuoteSurvivesInALiteralEndingWithAContinuationCharacter()
    {
        // The shape of the case that first exposed this, kept so a regression here is attributed to
        // escaping rather than to line continuations — which is how it was misread the first time.
        await Run("Debug.Print \"he said \"\"hi\"\" _\"\nDebug.Print Len(\"he said \"\"hi\"\" _\")");
        AssertDebugLog([new Vb6Value("he said \"hi\" _"), new Vb6Value(14L)]);
    }
}
