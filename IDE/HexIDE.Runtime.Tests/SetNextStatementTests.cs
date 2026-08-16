using System;
using System.Linq;
using HexIDE.Runtime.Debugging;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Interpreter-debugger v2·P7b — Set Next Statement: move the execution point to a target line without running the
/// statements in between. TOP-LEVEL-body granularity only — a target nested inside an If/For/Do block, or a move
/// requested while paused inside such a block, is refused (a documented divergence from VB6, which allows nested
/// moves within a procedure).
/// </summary>
public class SetNextStatementTests : BaseVBTestFixture
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(15);

    private static Task<StoppedInfo> NextStop(DebugController dbg)
    {
        var tcs = new TaskCompletionSource<StoppedInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(StoppedInfo info) { dbg.Stopped -= Handler; tcs.TrySetResult(info); }
        dbg.Stopped += Handler;
        return tcs.Task;
    }

    [Fact]
    public async Task SetNext_Forward_SkipsInterveningStatements()
    {
        var (vb, dbg) = NewDebuggable(
            "Go\nSub Go()\nDebug.Print 1\nDebug.Print 2\nDebug.Print 3\nDebug.Print 4\nEnd Sub\n", "M");
        dbg.SetBreakpoints("M", new[] { 3 });
        var stop = NextStop(dbg);
        var run = vb.Execute();
        (await stop.WaitAsync(Guard)).Line.Should().Be(3);
        debug.Should().BeEmpty();

        dbg.SetNextStatement("M", 5).Should().BeTrue();   // move to Debug.Print 3 — skips Print 1 & 2
        dbg.Continue();
        await run.WaitAsync(Guard);
        debug.Select(v => v.Value).Should().Equal(3, 4);   // 1 & 2 never ran
    }

    [Fact]
    public async Task SetNext_Backward_ReRunsFromTarget()
    {
        var (vb, dbg) = NewDebuggable(
            "Go\nSub Go()\nDebug.Print 1\nDebug.Print 2\nEnd Sub\n", "M");
        dbg.SetBreakpoints("M", new[] { 4 });   // paused at Debug.Print 2 (1 already printed)
        var stop = NextStop(dbg);
        var run = vb.Execute();
        (await stop.WaitAsync(Guard)).Line.Should().Be(4);
        debug.Select(v => v.Value).Should().Equal(1);

        dbg.SetNextStatement("M", 3).Should().BeTrue();   // move back to Debug.Print 1 — re-runs it
        dbg.SetBreakpoints("M", Array.Empty<int>());       // clear the line-4 breakpoint so it doesn't re-trap on the re-run
        dbg.Continue();
        await run.WaitAsync(Guard);
        debug.Select(v => v.Value).Should().Equal(1, 1, 2);   // 1 re-printed, then 2
    }

    [Fact]
    public async Task SetNext_NestedTarget_Refused_TopLevelTarget_Accepted()
    {
        var (vb, dbg) = NewDebuggable(
            "Go\nSub Go()\nIf True Then\nDebug.Print 1\nEnd If\nDebug.Print 2\nEnd Sub\n", "M");
        dbg.SetBreakpoints("M", new[] { 3 });   // the If — a top-level statement
        var stop = NextStop(dbg);
        var run = vb.Execute();
        (await stop.WaitAsync(Guard)).Line.Should().Be(3);

        dbg.SetNextStatement("M", 4).Should().BeFalse();   // line 4 is nested inside the If — refused
        dbg.SetNextStatement("M", 6).Should().BeTrue();    // line 6 is top-level — accepted (skips the whole If)
        dbg.Continue();
        await run.WaitAsync(Guard);
        debug.Select(v => v.Value).Should().Equal(2);   // the If's body (Print 1) skipped
    }

    [Fact]
    public async Task SetNext_WhilePausedInsideBlock_Refused()
    {
        var (vb, dbg) = NewDebuggable(
            "Go\nSub Go()\nIf True Then\nDebug.Print 1\nEnd If\nEnd Sub\n", "M");
        dbg.SetBreakpoints("M", new[] { 4 });   // Debug.Print 1 — INSIDE the If (nested)
        var stop = NextStop(dbg);
        var run = vb.Execute();
        (await stop.WaitAsync(Guard)).Line.Should().Be(4);

        dbg.SetNextStatement("M", 4).Should().BeFalse();   // paused in a nested block — refused
        dbg.Continue();
        await run.WaitAsync(Guard);
    }
}
