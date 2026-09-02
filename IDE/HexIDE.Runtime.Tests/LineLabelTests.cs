using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Line labels, and the jump targets they are supposed to create.
///
/// <para>
/// Every form here is measured legal against <c>vb6.exe</c> — the corpus carries 38 label cases. But the
/// corpus gate only asks whether a module PARSES, and a lost label parses perfectly: the name simply
/// becomes a bare procedure call, and the failure surfaces later as <c>Label not defined</c> from a
/// <c>GoTo</c> somewhere else in the procedure. So these tests JUMP. A label that is not registered fails
/// here and nowhere else.
/// </para>
///
/// <para>
/// That gap is why this file exists as well as the corpus rows: an entire class of defect in this area is
/// invisible to a parse-only conformance check, and the one that prompted it — <c>Skip: Debug.Print "x"</c>,
/// about as ordinary as VB6 gets — had been shipping for as long as the grammar has existed.
/// </para>
/// </summary>
public class LineLabelTests : BaseVBTestFixture
{
    [Fact]
    public async Task ALabelSharingItsLineWithAStatement()
    {
        await Run("GoTo Skip\nDebug.Print \"missed\"\nSkip: Debug.Print \"x\"\n");
        AssertDebugLog([new Vb6Value("x")]);
    }

    [Fact]
    public async Task ALabelAloneOnItsLine()
    {
        await Run("GoTo Skip\nDebug.Print \"missed\"\nSkip:\nDebug.Print \"x\"\n");
        AssertDebugLog([new Vb6Value("x")]);
    }

    [Fact]
    public async Task NoSpaceBetweenTheColonAndTheStatement()
    {
        await Run("GoTo Skip\nDebug.Print \"missed\"\nSkip:Debug.Print \"x\"\n");
        AssertDebugLog([new Vb6Value("x")]);
    }

    [Fact]
    public async Task ASpaceBeforeTheColon()
    {
        // `Later : stmt`. The label rule demands the colon be adjacent to the name, so this parsed as a
        // call with a separator after it — the label silently vanished.
        await Run("GoTo Later\nDebug.Print \"missed\"\nLater : Debug.Print \"x\"\n");
        AssertDebugLog([new Vb6Value("x")]);
    }

    [Fact]
    public async Task ADoubleColonAfterTheLabel()
    {
        await Run("GoTo Skip\nDebug.Print \"missed\"\nSkip:: Debug.Print \"x\"\n");
        AssertDebugLog([new Vb6Value("x")]);
    }

    [Fact]
    public async Task ALabelHeadingAColonJoinedRun()
    {
        // The loop idiom: the label heads the line and the rest of the line is ordinary statements.
        await Run("Dim i\ni = 0\nRetry: i = i + 1 : If i < 2 Then GoTo Retry\nDebug.Print i\n");
        AssertDebugLog([new Vb6Value(2)]);
    }

    [Fact]
    public async Task ALabelWhoseNameCollidesWithAVariable()
    {
        // VB6 keeps labels and variables in separate namespaces, so this is legal and both are live.
        await Run("Dim Foo\nFoo = 7\nGoTo Foo\nDebug.Print \"missed\"\nFoo: Debug.Print Foo\n");
        AssertDebugLog([new Vb6Value(7)]);
    }

    [Fact]
    public async Task ALabelSplitFromItsColonByAContinuation()
    {
        await Run("GoTo Skip\nDebug.Print \"missed\"\nSkip _\n: Debug.Print \"x\"\n");
        AssertDebugLog([new Vb6Value("x")]);
    }

    [Fact]
    public async Task AContinuationBetweenTheColonAndTheStatement()
    {
        await Run("GoTo Skip\nDebug.Print \"missed\"\nSkip: _\nDebug.Print \"x\"\n");
        AssertDebugLog([new Vb6Value("x")]);
    }

    [Fact]
    public async Task ADeeplyIndentedLabel()
    {
        await Run("GoTo Skip\nDebug.Print \"missed\"\n            Skip: Debug.Print \"x\"\n");
        AssertDebugLog([new Vb6Value("x")]);
    }

    [Fact]
    public async Task ALabelWithAnUnderscoreInItsName()
    {
        await Run("GoTo my_label\nDebug.Print \"missed\"\nmy_label:\nDebug.Print \"x\"\n");
        AssertDebugLog([new Vb6Value("x")]);
    }

    [Fact]
    public async Task ALabelImmediatelyBeforeTheEndOfTheProcedure()
    {
        await Run("Go\nSub Go()\n    Debug.Print \"a\"\n    GoTo Fin\n    Debug.Print \"missed\"\nFin:\nEnd Sub\n");
        AssertDebugLog([new Vb6Value("a")]);
    }

    [Fact]
    public async Task ALabelOnAnOnErrorHandler()
    {
        await Run("On Error GoTo Handler\nErr.Raise 5\nExit Sub\nHandler: Debug.Print \"handled\"\n");
        AssertDebugLog([new Vb6Value("handled")]);
    }

    [Fact]
    public async Task ALineNumberAndANamedLabelMayShareAHead()
    {
        // `10 Skip: stmt` — measured legal, and both names reach the same statement. The reverse order,
        // `Skip: 10 stmt`, is a syntax error in VB6, which is why the head is a sequence and not a set.
        await Run("Dim s\ns = \"\"\nGoTo Skip\ns = s & \"X\"\n10 Skip: s = s & \"a\"\nDebug.Print s\n");
        AssertDebugLog([new Vb6Value("a")]);
    }

    [Fact]
    public async Task StackedLabelsOnConsecutiveLinesBothReachTheSameStatement()
    {
        await Run("Dim s\ns = \"\"\nGoTo First\ns = s & \"X\"\nFirst:\nSecond:\ns = s & \"a\"\nDebug.Print s\n");
        AssertDebugLog([new Vb6Value("a")]);
    }

    [Fact]
    public async Task ALabelSharingItsLineWithALoopTerminator_IsNotYetAJumpTarget()
    {
        // `Cont: Next i` is legal VB6 — measured — and this documents that HexIDE does not yet reach it.
        //
        // It is NOT the defect the rest of this file is about, and that distinction is the point. A label
        // sharing its line with a STATEMENT is a label-registration problem, and it is fixed. A label
        // sharing its line with a construct TERMINATOR is something else: `Next` is not a statement in the
        // body block, it is the tail of the For rule, so jumping to `Cont` means resuming in the middle of
        // a statement the walk is already inside. That is the tree-walking limit — a position in a linear
        // sequence, which a stack of visitor frames does not have — and the same wall as GoSub/Return.
        //
        // The construct PARSES, so a whole module still loads; only the jump is refused. See
        // docs/interpreter-gaps.md.
        var act = async () => await Run("Dim i\nFor i = 1 To 3\n    If i = 2 Then GoTo Cont\nCont: Next i\n");
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task ANameAfterAColonIsACallAndNotALabel()
    {
        // The guard on the other side. `z = 1: Here: z = 2` is NOT a label in VB6 — measured, and the
        // compiler says so in the most informative way available: "Sub or Function not defined", meaning
        // it read `Here` as a CALL. Registering it as a jump target would invent a destination the
        // program does not have, which is worse than the error we raise.
        var act = async () => await Run("Dim z\nz = 1: Here: z = 2\nDebug.Print z\n");
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task ANumericLabelStillWorksAlongsideNamedOnes()
    {
        // The numeric prefix and the named label go through different grammar paths; a change to one
        // must not disturb the other.
        await Run("Dim s\ns = \"\"\nGoTo 10\ns = s & \"X\"\n10 s = s & \"a\"\nGoTo Done\ns = s & \"Y\"\nDone: Debug.Print s\n");
        AssertDebugLog([new Vb6Value("a")]);
    }
}
