using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Regression tests for the three HIGH For…Next defects surfaced by the 2026-08-09 adversarial bug-hunt (all in
/// <c>VisitForNextStmt</c>) plus the sibling empty-body NRE in the Do loops. Every expectation is oracle-pinned
/// against real vb6.exe (see <c>docs/vb6-fidelity-oracle.md</c>). Before the fix: #1 hung the IDE, #2 threw an NRE,
/// #3 threw a spurious type-mismatch.
/// </summary>
public class ForLoopFixTests : BaseVBTestFixture
{
    // #1 — a step that never lands exactly on `to`. Old code used `i != to` and hung (i = 0,3,6,9,12,15,… never == 10).
    [Fact]
    public async Task NonDividingStep_TerminatesAndLeavesCounterPastLimit()
    {
        await Run("For i = 0 To 10 Step 3\nDebug.Print i\nNext\nDebug.Print i\n");
        AssertDebugLog([new Vb6Value(0), new Vb6Value(3), new Vb6Value(6), new Vb6Value(9), new Vb6Value(12)]);
    }

    // #1 (negative) — `5 To 1 Step -2` → 5,3,1 then post-loop -1. Old `i != to` never hit 1 exactly from 5 by -2? it
    // does (5,3,1) — but the trailing unconditional body pass double-counted; the `>=` form is clean.
    [Fact]
    public async Task NegativeNonDividingStep_Terminates()
    {
        await Run("For i = 5 To 1 Step -2\nDebug.Print i\nNext\nDebug.Print i\n");
        AssertDebugLog([new Vb6Value(5), new Vb6Value(3), new Vb6Value(1), new Vb6Value(-1)]);
    }

    // #2 — empty For body (the grammar makes the block optional). Old code did `await Visit(null)` → NRE. VB6 counts
    // fine; the counter is left one past the limit (oracle: 4).
    [Fact]
    public async Task EmptyForBody_CountsWithoutError()
    {
        await Run("For i = 1 To 3\nNext\nDebug.Print i\n");
        AssertDebugLog([new Vb6Value(4)]);
    }

    // #3 — a Long limit. Old TryUnpack<int> rejected any Long and threw "from/to/step is not an integer". VB6 runs it;
    // the counter ends at 50001, typed Long (oracle: 50001|Long).
    [Fact]
    public async Task LongBound_RunsAndCounterIsLong()
    {
        await Run("For i = 1 To 50000\nNext\nDebug.Print i\n");
        AssertDebugLog([new Vb6Value(50001L)]);
    }

    // #841: measured with #839's fifth probe, M1–M3, native and p-code.
    [Theory]
    [InlineData("Long")]
    [InlineData("Variant")]
    public async Task BodyIncrement_ChangesCounterAtNext(string counterType)
    {
        await Run($"Dim i As {counterType}\nFor i = 1 To 10\nDebug.Print i\ni = i + 1\nNext\nDebug.Print i\n");
        var value = (int n) => counterType == "Long" ? new Vb6Value((long)n) : new Vb6Value(n);
        AssertDebugLog([value(1), value(3), value(5), value(7), value(9), value(11)]);
    }

    [Fact]
    public async Task BodyReset_RestartsCounterAtNext()
    {
        await Run("Dim i As Long\nDim passes As Long\nFor i = 1 To 3\nDebug.Print i\npasses = passes + 1\nIf passes = 8 Then Exit For\nIf i = 3 Then i = 0\nNext\n");
        AssertDebugLog([new Vb6Value(1L), new Vb6Value(2L), new Vb6Value(3L), new Vb6Value(1L),
            new Vb6Value(2L), new Vb6Value(3L), new Vb6Value(1L), new Vb6Value(2L)]);
    }

    // NOTE: the sibling empty-body NRE in the Do loops (Do/Loop, Do While…/Loop) is fixed by the same null-guard as
    // ForEach uses, but isn't unit-testable in isolation — an empty Do/Loop is an infinite loop (would hang the
    // test), and the conditional forms either don't parse empty or return before reaching the guarded body Visit.
    // The guard is verified by inspection + matches VisitForEachStmt's established pattern.
}
