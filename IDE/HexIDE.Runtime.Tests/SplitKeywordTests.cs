using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A line continuation may fall INSIDE a multi-word keyword. <c>End _ Sub</c>, <c>Select _ Case</c> and
/// <c>Exit _ For</c> are all legal VB6, and so is <c>End  Sub</c> with the two words merely aligned apart.
///
/// <para>
/// The grammar spells these as single lexer tokens, which is right — <c>End</c> and <c>Sub</c> mean
/// something else apart than together. But it spelled the join as one literal space, so anything a real
/// listing puts between the words broke the token, and the failure surfaced as a parse error a long way
/// from the continuation that caused it. The fix is a <c>KWSEP</c> fragment covering whitespace and the
/// continuation alike.
/// </para>
///
/// <para>
/// The conformance corpus proves these lines PARSE. These prove they still MEAN what they meant: a
/// keyword that survives being split has to keep its behaviour, not merely its acceptance.
/// </para>
/// </summary>
public class SplitKeywordTests : BaseVBTestFixture
{
    [Fact]
    public async Task AContinuationMaySplitEndSub()
    {
        await Run("Go\nSub Go()\n    Debug.Print \"ran\"\nEnd _\nSub\n");
        AssertDebugLog([new Vb6Value("ran")]);
    }

    [Fact]
    public async Task AContinuationMaySplitEndIf()
    {
        await Run("If True Then\n    Debug.Print \"yes\"\nEnd _\nIf\n");
        AssertDebugLog([new Vb6Value("yes")]);
    }

    [Fact]
    public async Task AContinuationMaySplitSelectCase()
    {
        await Run("Dim n\nn = 2\nSelect _\nCase n\n    Case 2\n        Debug.Print \"two\"\nEnd _\nSelect\n");
        AssertDebugLog([new Vb6Value("two")]);
    }

    [Fact]
    public async Task AContinuationMaySplitExitFor_AndTheExitStillExits()
    {
        // The point of the runtime check: a split keyword that parses but no longer branches would look
        // fixed to the corpus and be broken here.
        await Run("Dim i\nDim s\ns = \"\"\nFor i = 1 To 3\n    If i = 2 Then Exit _\n        For\n    s = s & \"a\"\nNext i\nDebug.Print \"[\" & s & \"]\"\n");
        AssertDebugLog([new Vb6Value("[a]")]);
    }

    [Fact]
    public async Task ExtraWhitespaceMaySeparateTheWords()
    {
        // Alignment, not continuation. Real listings pad keywords apart and VB6 does not mind.
        await Run("Dim i\nFor i = 1 To 2\n    If i = 1 Then Exit  For\nNext i\nDebug.Print \"done\"\n");
        AssertDebugLog([new Vb6Value("done")]);
    }

    [Fact]
    public async Task AContinuationMaySplitResumeNext()
    {
        await Run("On Error Resume _\nNext\nDebug.Print CInt(\"x\")\nDebug.Print \"survived\"\n");
        AssertDebugLog([new Vb6Value("survived")]);
    }

    [Fact]
    public async Task AContinuationMaySplitForEach()
    {
        await Run("Dim c\nDim s\ns = \"\"\nFor _\nEach c In Array(1, 2)\n    s = s & c\nNext c\nDebug.Print s\n");
        AssertDebugLog([new Vb6Value("12")]);
    }

    [Fact]
    public async Task ATabMayFollowRem()
    {
        // COMMENT shared the same single-space spelling, so `Rem` + tab was not a comment. Widening the
        // keyword separator fixed this one incidentally, and it is a real form.
        await Run("Debug.Print \"before\"\nRem\tthis is a comment\nDebug.Print \"after\"\n");
        AssertDebugLog([new Vb6Value("before"), new Vb6Value("after")]);
    }

    [Theory]
    [InlineData("Dim n\nSelect\nCase n\n    Case 1\nEnd Select\n")]
    [InlineData("Dim c\nFor\nEach c In Array(1)\nNext c\n")]
    public async Task TheSeparatorDoesNotSwallowANewline(string code)
    {
        // Both separators — the KWSEP fragment and the relaxed WS? — admit whitespace and continuations,
        // NOT a bare line break. A newline is a real token the parser still has to match, so these stay
        // syntax errors; if either relaxation had reached too far they would quietly parse.
        var act = async () => await Run(code);
        await act.Should().ThrowAsync<VBCompileErrorException>();
    }
}
