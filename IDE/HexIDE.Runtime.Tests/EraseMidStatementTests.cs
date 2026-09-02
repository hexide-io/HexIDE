using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Regression for four interpreter "Missed (undocumented)" gaps that used to throw NotImplementedException:
/// the Mid statement, the Erase statement, Beep, and DoEvents. Every string/error result is oracle-pinned
/// against vb6.exe (see docs/vb6-fidelity-oracle.md).
/// </summary>
public class EraseMidStatementTests : BaseVBTestFixture
{
    // ── Mid statement: overwrite in place, length never changes ─────────────────────────────────────
    [Fact]
    public async Task Mid_ReplacementShorterThanLength_ReplacesReplacementLength()
    {
        await Run("Dim s As String\ns = \"ABCDEF\"\nMid(s, 2, 3) = \"xy\"\nDebug.Print s\n");
        AssertDebugLog([new Vb6Value("AxyDEF")]);
    }

    [Fact]
    public async Task Mid_ReplacementLongerThanLength_TruncatesToLength()
    {
        await Run("Dim s As String\ns = \"ABCDEF\"\nMid(s, 2, 3) = \"wxyz\"\nDebug.Print s\n");
        AssertDebugLog([new Vb6Value("AwxyEF")]);
    }

    [Fact]
    public async Task Mid_NoLength_ReplacesToEndOrReplacementLength()
    {
        await Run("Dim s As String\ns = \"ABCDEF\"\nMid(s, 3) = \"xyz\"\nDebug.Print s\n");
        AssertDebugLog([new Vb6Value("ABxyzF")]);
    }

    [Fact]
    public async Task Mid_ReplacementRunsPastEnd_ClampsToRemaining()
    {
        await Run("Dim s As String\ns = \"ABC\"\nMid(s, 2) = \"XYZ\"\nDebug.Print s\n");
        AssertDebugLog([new Vb6Value("AXY")]);
    }

    [Theory]
    [InlineData("10")]   // start past end
    [InlineData("0")]    // start < 1
    [InlineData("7")]    // start == Len + 1
    public async Task Mid_StartOutOfRange_RaisesInvalidProcedureCall(string start)
    {
        await Run("On Error Resume Next\nDim s As String\ns = \"ABCDEF\"\nMid(s, " + start + ") = \"x\"\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(5L)]);
    }

    // ── Erase: dynamic array is freed, fixed array keeps bounds with elements reset ──────────────────
    [Fact]
    public async Task Erase_DynamicArray_FreesIt_UBoundRaisesSubscriptOutOfRange()
    {
        await Run("On Error Resume Next\nDim d() As Integer\nReDim d(3)\nd(1) = 9\nErase d\nDim x\nx = UBound(d)\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(9L)]);
    }

    [Fact]
    public async Task Erase_DynamicArray_CanBeRedimedAgain()
    {
        await Run("Dim d() As Integer\nReDim d(3)\nErase d\nReDim d(2)\nd(0) = 5\nDebug.Print d(0)\n");
        AssertDebugLog([new Vb6Value(5)]);
    }

    [Fact]
    public async Task Erase_FixedArray_KeepsBounds_ResetsElements()
    {
        await Run("Dim fx(3) As Integer\nfx(1) = 9\nErase fx\nDebug.Print fx(1)\nDebug.Print UBound(fx)\nDebug.Print LBound(fx)\n");
        AssertDebugLog([new Vb6Value(0), new Vb6Value(3), new Vb6Value(0)]);
    }

    [Fact]
    public async Task Erase_FixedStringArray_ResetsElementsToEmpty()
    {
        await Run("Dim g(2) As String\ng(1) = \"hi\"\nErase g\nDebug.Print \"[\" & g(1) & \"]\"\n");
        AssertDebugLog([new Vb6Value("[]")]);
    }

    // ── Beep / DoEvents: never crash, never interrupt the program ──────────────────────────────────────────────────
    // Beep actually sounds the bell now (Console.Beep, guarded to the desktop platforms we ship to). What a
    // test can assert is the part that matters to a running program: it is fail-safe. On a build agent with no
    // audio device the call throws inside, and the program must carry on regardless — so the assertion is that
    // execution CONTINUES, not that anything was audible.
    [Fact]
    public async Task Beep_IsFailSafe_ExecutionContinues()
    {
        await Run("Beep\nDebug.Print 1\n");
        AssertDebugLog([new Vb6Value(1)]);
    }

    [Fact]
    public async Task Beep_RepeatedAndInsideALoop_StillContinues()
    {
        // The shape that would expose a Beep throwing only on its second call, or one that swallows its
        // exception but leaves the statement walker somewhere bad.
        await Run("Dim i\nFor i = 1 To 3\n    Beep\nNext i\nBeep\nDebug.Print 2\n");
        AssertDebugLog([new Vb6Value(2)]);
    }

    [Fact]
    public async Task Beep_DoesNotSetErr()
    {
        // If the internal failure ever escaped as a trappable VB6 error this would report a non-zero number.
        // Real VB6 raises nothing for an inaudible Beep, so neither may we.
        await Run("On Error Resume Next\nBeep\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(0L)]);
    }

    [Fact]
    public async Task DoEvents_AsStatementAndExpression_ReturnsZero()
    {
        await Run("DoEvents\nDim x\nx = DoEvents\nDebug.Print x\n");
        AssertDebugLog([new Vb6Value(0)]);
    }
}
