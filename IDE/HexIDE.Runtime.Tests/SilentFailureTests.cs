using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// #191 — unimplemented intrinsics used to fail SILENTLY: they ran, did nothing, and let the program
/// continue with a wrong value. By the harm model this project uses for serialization that is the worst
/// outcome available, worse than refusing to load the file: a module that will not open is obvious in a
/// second and the user blames the tool, while a <c>Shell</c> that launches nothing sends them hunting
/// through their own logic for a bug that is ours.
///
/// Two separate causes, both fixed here. An unregistered bare name fell through to VB6's
/// implicit-declaration rule and evaluated to Empty. And <c>Time = x</c> was grammar-shadowed: TIME is an
/// ambiguousKeyword and <c>letStmt</c> was matched ahead of <c>timeStmt</c>, so it silently created a
/// VARIABLE called Time and left the throw in <c>VisitTimeStmt</c> as unreachable dead code — while
/// <c>Date = x</c> threw correctly, the two differing only in grammar alternative order.
///
/// The guards at the bottom matter as much as the fixes: VB6 creates undeclared variables on first use,
/// and that must keep working.
/// </summary>
public class SilentFailureTests : BaseVBTestFixture
{
    // ── An unimplemented intrinsic now says so ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("CurDir")]
    [InlineData("Command")]
    [InlineData("FreeFile")]
    [InlineData("Erl")]
    [InlineData("Dir")]
    public async Task ReadingAnUnimplementedIntrinsic_Raises_InsteadOfYieldingEmpty(string name)
    {
        // Each of these returned Empty before, with no error at all.
        var act = async () => await Run("Dim s\ns = " + name + "\nDebug.Print s\n");
        (await act.Should().ThrowAsync<NotImplementedException>())
            .Which.Message.Should().Contain(name);
    }

    [Fact]
    public async Task ShellAsAStatement_Raises_InsteadOfLaunchingNothing()
    {
        // The headline case: this ran, launched nothing, and continued as if the process had started.
        var act = async () => await Run("Shell \"notepad.exe\", vbNormalFocus\nDebug.Print 1\n");
        (await act.Should().ThrowAsync<NotImplementedException>())
            .Which.Message.Should().Contain("Shell");
    }

    [Fact]
    public async Task AnUnknownBareStatement_IsSubOrFunctionNotDefined()
    {
        // Not a VB6 intrinsic at all, so it gets VB6's own error rather than the not-implemented one.
        // Previously this was also a silent no-op.
        var act = async () => await Run("NoSuchRoutine 1, 2\n");
        (await act.Should().ThrowAsync<VBCompileErrorException>())
            .Which.Message.Should().Contain("NoSuchRoutine");
    }

    [Fact]
    public async Task TimeAssignment_Raises_InsteadOfCreatingAVariableCalledTime()
    {
        // Was: silently created a variable named Time, shadowing the intrinsic, and `Debug.Print Time`
        // then printed whatever had been assigned. The grammar now matches timeStmt first, as it always
        // did for dateStmt.
        var act = async () => await Run("Time = #1:00:00 AM#\n");
        (await act.Should().ThrowAsync<NotImplementedException>())
            .Which.Message.Should().Contain("Time");
    }

    [Fact]
    public async Task DateAssignment_StillRaises_TheCaseThatAlreadyWorked()
    {
        // The control. Date = x threw before this change and must still throw — the grammar edit moved
        // timeStmt to sit beside dateStmt, and must not have displaced it.
        var act = async () => await Run("Date = \"1/1/2000\"\n");
        await act.Should().ThrowAsync<NotImplementedException>();
    }

    // ── Guards: none of the above may break VB6's ordinary rules ────────────────────────────────────

    [Fact]
    public async Task AnUndeclaredVariableStillWorks()
    {
        // VB6 creates a variable on first use when Option Explicit is off, which is the default — here as
        // it was there. The intrinsic check must sit AFTER that rule for ordinary names, not replace it.
        await Run("x = 5\nDebug.Print x\nDim y\nDebug.Print y\n");
        AssertDebugLog([new Vb6Value(5), Vb6Value.Variant]);
    }

    [Fact]
    public async Task ADeclaredVariableSharingAnIntrinsicName_StillWins()
    {
        // Resolution reaches the intrinsic check only when nothing else claims the name, so a real
        // variable called Command keeps working. Without this, the fix would break valid VB6.
        await Run("Dim Command As String\nCommand = \"hello\"\nDebug.Print Command\n");
        AssertDebugLog([new Vb6Value("hello")]);
    }

    [Fact]
    public async Task AnImplementedIntrinsic_IsUnaffected()
    {
        // The registry is consulted before the name list, so implementing a function needs no edit to it.
        // Timer and Now are implemented; they must not start raising.
        await Run("Dim t\nt = Timer\nDebug.Print (t >= 0)\nDebug.Print (Len(TypeName(Now)) > 0)\n");
        AssertDebugLog([new Vb6Value(true), new Vb6Value(true)]);
    }

    [Fact]
    public async Task AUserProcedureSharingAnIntrinsicName_StillWins()
    {
        // A user Sub named after an unimplemented intrinsic takes precedence, as it does in VB6.
        await Run("Sub Dir()\n    Debug.Print 42\nEnd Sub\n\nDir\n");
        AssertDebugLog([new Vb6Value(42)]);
    }
}
