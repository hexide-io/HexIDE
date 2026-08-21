using System;
using System.Linq;
using HexIDE.Runtime.Debugging;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Interpreter-debugger v2·P6b — the VB6 break-type watches evaluated at the pause-gate: "Break When Value Is True"
/// (level-triggered) and "Break When Value Has Changed" (edge-triggered). Drive a real run with break-watches pushed
/// via <see cref="IDebugController.SetWatchBreaks"/> and assert the walk stops at the right statement with
/// <see cref="StopReason.Watch"/>.
/// </summary>
public class WatchBreakTests : BaseVBTestFixture
{

    private static Task<StoppedInfo> NextStop(DebugController dbg)
    {
        var tcs = new TaskCompletionSource<StoppedInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(StoppedInfo info) { dbg.Stopped -= Handler; tcs.TrySetResult(info); }
        dbg.Stopped += Handler;
        return tcs.Task;
    }

    [Fact]
    public async Task BreakWhenTrue_PausesWhenExpressionIsTrue()
    {
        // Loop 1..10 inside a proc (a frame — module top-level has none, so watches need a called Sub).
        var (vb, dbg) = NewDebuggable(
            "Go\nSub Go()\nDim i As Integer\nFor i = 1 To 10\nDebug.Print i\nNext\nEnd Sub\n", "M");
        dbg.SetWatchBreaks(new[] { new WatchBreakSpec("i = 5", BreakOnChange: false) });

        var stop = NextStop(dbg);
        var run = vb.Execute();
        var info = await stop.Guarded();

        info.Reason.Should().Be(StopReason.Watch);
        info.Line.Should().Be(5);                                   // at the body, with i == 5
        debug.Select(v => v.Value).Should().Equal(1, 2, 3, 4);      // 5 not printed yet — broke before it

        // Clear the watch and run free to completion (level-triggered would otherwise re-break while i stays 5).
        dbg.SetWatchBreaks(Array.Empty<WatchBreakSpec>());
        dbg.Continue();
        await run.Guarded();
        debug.Select(v => v.Value).Should().Contain(10);
    }

    [Fact]
    public async Task BreakWhenChanged_PausesWhenValueChanges()
    {
        var (vb, dbg) = NewDebuggable(
            "Go\nSub Go()\nDim x As Integer\nx = 1\nx = 2\nx = 3\nEnd Sub\n", "M");
        dbg.SetWatchBreaks(new[] { new WatchBreakSpec("x", BreakOnChange: true) });

        var stop = NextStop(dbg);
        var run = vb.Execute();
        var info = await stop.Guarded();

        info.Reason.Should().Be(StopReason.Watch);
        info.Line.Should().Be(5);   // x changed 0 -> 1 after line 4; break before line 5

        dbg.SetWatchBreaks(Array.Empty<WatchBreakSpec>());
        dbg.Continue();
        await run.Guarded();
    }

    [Fact]
    public async Task BreakWhenTrue_NeverTrue_RunsToCompletion()
    {
        var (vb, dbg) = NewDebuggable(
            "Go\nSub Go()\nDim i As Integer\nFor i = 1 To 3\nDebug.Print i\nNext\nEnd Sub\n", "M");
        dbg.SetWatchBreaks(new[] { new WatchBreakSpec("i = 99", BreakOnChange: false) });

        var run = vb.Execute();
        await run.Guarded();   // never breaks — completes

        debug.Select(v => v.Value).Should().Equal(1, 2, 3);
    }
}
