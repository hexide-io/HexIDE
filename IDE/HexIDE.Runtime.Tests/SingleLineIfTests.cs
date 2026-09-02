using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// The single-line <c>If</c>, and the question the conformance corpus was built to ask: in
/// <c>If x Then a = 1 : b = 2</c>, does <c>b</c> belong to the <c>Then</c> branch or run unconditionally?
///
/// <para>
/// <b>It belongs to the branch.</b> Measured against vb6.exe — <c>If False Then A : B</c> runs NEITHER
/// statement. That is the opposite of the intuitive reading, and wrong in the dangerous direction: a
/// parser that treats the tail as unconditional silently executes code the program said not to, with no
/// error and nothing to debug from.
/// </para>
///
/// <para>
/// This is why the corpus cases were written to produce distinguishing output rather than merely to
/// compile. A legality oracle can say the line is accepted; only running it says what it means.
/// </para>
/// </summary>
public class SingleLineIfTests : BaseVBTestFixture
{
    [Fact]
    public async Task AColonJoinedTail_BelongsToTheThenBranch()
    {
        // Measured "[]" — neither statement runs. If the tail were unconditional this would print "[B]".
        await Run("Dim s\ns = \"\"\nIf False Then s = s & \"A\" : s = s & \"B\"\nDebug.Print \"[\" & s & \"]\"\n");
        AssertDebugLog([new Vb6Value("[]")]);
    }

    [Fact]
    public async Task AColonJoinedTail_RunsEntirelyWhenTheConditionHolds()
    {
        // Measured "[AB]" — the whole tail runs, in order.
        await Run("Dim s\ns = \"\"\nIf True Then s = s & \"A\" : s = s & \"B\"\nDebug.Print \"[\" & s & \"]\"\n");
        AssertDebugLog([new Vb6Value("[AB]")]);
    }

    [Fact]
    public async Task Else_AttachesAfterTheWholeJoinedTail()
    {
        // Measured "[C]" — Else binds to the If, not to the last statement of the tail.
        await Run("Dim s\ns = \"\"\n" +
                  "If False Then s = s & \"A\" : s = s & \"B\" Else s = s & \"C\"\n" +
                  "Debug.Print \"[\" & s & \"]\"\n");
        AssertDebugLog([new Vb6Value("[C]")]);
    }

    [Fact]
    public async Task AColonMayPrecedeElse()
    {
        // Measured "[C]". A trailing colon before Else is legal and changes nothing.
        await Run("Dim s\ns = \"\"\nIf False Then s = s & \"A\" : Else s = s & \"C\"\nDebug.Print \"[\" & s & \"]\"\n");
        AssertDebugLog([new Vb6Value("[C]")]);
    }

    [Fact]
    public async Task AColonMayFollowThenImmediately()
    {
        // `If x Then : stmt` — a colon with no statement before it. The branch body allows leading colons.
        await Run("Dim s\ns = \"\"\nIf True Then : s = \"ran\"\nDebug.Print s\n");
        AssertDebugLog([new Vb6Value("ran")]);
    }

    [Fact]
    public async Task ControlLeavingTheBranch_StopsTheRestOfTheTail()
    {
        // The reason the branch is run as a loop with a flow check rather than blindly: an Exit For inside
        // a joined tail must not let the statements after it run.
        await Run("Dim i\nDim s\ns = \"\"\n" +
                  "For i = 1 To 3\n" +
                  "    If i = 2 Then Exit For : s = s & \"X\"\n" +
                  "    s = s & \"a\"\n" +
                  "Next i\n" +
                  "Debug.Print \"[\" & s & \"]\"\n");
        AssertDebugLog([new Vb6Value("[a]")]);
    }

    [Fact]
    public async Task TheBlockFormIsUnaffected()
    {
        // The single-line and block forms are distinguished by the newline, so widening the inline body
        // must not have let a block If collapse into one.
        await Run("Dim s\ns = \"\"\n" +
                  "If False Then\n    s = s & \"A\"\n    s = s & \"B\"\nElse\n    s = s & \"C\"\nEnd If\n" +
                  "Debug.Print \"[\" & s & \"]\"\n");
        AssertDebugLog([new Vb6Value("[C]")]);
    }
}
