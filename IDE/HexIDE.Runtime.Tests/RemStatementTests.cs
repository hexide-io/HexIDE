using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// <c>Rem</c>, VB6's other spelling of a comment, and the rule the documentation gets wrong.
///
/// <para>
/// The reference says "Rem followed by a space", and the grammar encoded exactly that. vb6.exe disagrees:
/// <c>Rem</c>, <c>Rem:</c>, <c>Rem=1</c>, <c>Rem'x</c> and <c>Rem"x"</c> are ALL comments. <b>Rem takes no
/// separator at all</b> — it is reserved, and it begins a comment the moment it stands as a whole word.
/// </para>
///
/// <para>
/// The half that makes this delicate is the other side of "whole word". <c>RemX</c> and <c>Rem1</c> are
/// ordinary identifiers, so a rule that starts a comment too eagerly turns <c>RemX = 5</c> into a comment
/// and DELETES the assignment. That is a wrong value rather than a late error, which is the one outcome
/// this project never accepts — so the eager cases and the identifier cases are tested together, here,
/// rather than trusting the corpus to catch a regression in only one of them.
/// </para>
/// </summary>
public class RemStatementTests : BaseVBTestFixture
{
    [Fact]
    public async Task ABareRemIsAComment()
    {
        await Run("Debug.Print \"A\"\nRem\nDebug.Print \"B\"\n");
        AssertDebugLog([new Vb6Value("A"), new Vb6Value("B")]);
    }

    [Fact]
    public async Task RemFollowedByAColon_TakesTheRestOfTheLineAsText()
    {
        // Measured "A" — B does NOT run. This is the case that matters most in the group: before the fix
        // the line lexed as REM, COLON, and a live statement, so a parser that accepted it would have
        // executed code VB6 treats as a remark.
        await Run("Dim s\ns = \"A\"\nRem: s = s & \"B\"\nDebug.Print s\n");
        AssertDebugLog([new Vb6Value("A")]);
    }

    [Fact]
    public async Task RemSwallowsTheRestOfAColonJoinedLine()
    {
        // Measured "A". The remark runs to the end of the PHYSICAL line; the colons inside it are text.
        await Run("Dim s\ns = \"A\": Rem a remark: s = s & \"B\"\nDebug.Print s\n");
        AssertDebugLog([new Vb6Value("A")]);
    }

    [Theory]
    [InlineData("Rem=1")]
    [InlineData("Rem'quoted")]
    [InlineData("Rem\"text\"")]
    [InlineData("Rem:")]
    [InlineData("Rem\t")]
    public async Task RemNeedsNoSeparatorBeforeItsText(string remLine)
    {
        await Run($"Debug.Print \"A\"\n{remLine}\nDebug.Print \"B\"\n");
        AssertDebugLog([new Vb6Value("A"), new Vb6Value("B")]);
    }

    [Fact]
    public async Task AnIdentifierMayBeginWithRem()
    {
        // The hazard. If this ever prints nothing, the comment rule has started eating assignments.
        await Run("Dim RemX\nRemX = 5\nDebug.Print RemX\n");
        AssertDebugLog([new Vb6Value(5)]);
    }

    [Fact]
    public async Task AnIdentifierMayBeRemFollowedByADigit()
    {
        await Run("Dim Rem1\nRem1 = 7\nDebug.Print Rem1\n");
        AssertDebugLog([new Vb6Value(7)]);
    }

    [Fact]
    public async Task ATrailingUnderscoreExtendsARemCommentOntoTheNextLine()
    {
        // Measured "AC" — the B line is swallowed by the remark. Surprising, and deliberately preserved:
        // it is also why `Rem a remark _` above an `End Sub` makes vb6.exe report "Expected End Sub".
        await Run("Dim s\ns = \"A\"\nRem a remark _\ns = s & \"B\"\ns = s & \"C\"\nDebug.Print s\n");
        AssertDebugLog([new Vb6Value("AC")]);
    }

    [Fact]
    public async Task RemMayBeTheElseBranchOfASingleLineIf()
    {
        // `Else Rem` is the idiom for a deliberately empty alternative, and it is legal — while
        // `Then Rem` is a syntax error. The asymmetry is measured, not assumed.
        await Run("Dim s\ns = \"A\"\nIf False Then s = s & \"T\" Else Rem nothing to do\ns = s & \"B\"\nDebug.Print s\n");
        AssertDebugLog([new Vb6Value("AB")]);
    }

    [Fact]
    public async Task ALineNumberMayCarryOnlyARemark_AndStillBeAJumpTarget()
    {
        // `10 Rem x` leaves a line number with no statement on its line. It labels the NEXT executable
        // statement — measured by jumping to it and observing which statements ran.
        await Run("Dim s\ns = \"A\"\nGoTo 10\ns = s & \"X\"\n10 Rem arrived here\ns = s & \"B\"\nDebug.Print s\n");
        AssertDebugLog([new Vb6Value("AB")]);
    }

    [Fact]
    public async Task ALineNumberMayStandAloneOnItsLine()
    {
        // The same grammar shape without the remark, which is what the fix actually generalises.
        await Run("Dim s\ns = \"A\"\nGoTo 20\ns = s & \"X\"\n20\ns = s & \"B\"\nDebug.Print s\n");
        AssertDebugLog([new Vb6Value("AB")]);
    }

    [Fact]
    public async Task SeveralBareLineNumbersAllLabelTheNextStatement()
    {
        // A run of them queues up, and every one is a live target. Two jumps, same destination.
        await Run("Dim s\ns = \"\"\nGoTo 30\ns = s & \"X\"\n30\n40 Rem still nothing here\ns = s & \"B\"\n" +
                  "Debug.Print s\n");
        AssertDebugLog([new Vb6Value("B")]);
    }
}
