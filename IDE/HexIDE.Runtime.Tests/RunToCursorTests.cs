using System;
using System.Linq;
using HexIDE.Runtime.Debugging;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Interpreter-debugger v2·P7a — Run To Cursor: a one-shot temporary breakpoint at a target line. Arm it while
/// paused, Continue, and the walk runs the intervening statements and breaks at the target (then the target clears).
/// </summary>
public class RunToCursorTests : BaseVBTestFixture
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
    public async Task RunToCursor_RunsThroughIntervening_BreaksAtTarget()
    {
        var (vb, dbg) = NewDebuggable(
            "Go\nSub Go()\nDebug.Print 1\nDebug.Print 2\nDebug.Print 3\nDebug.Print 4\nEnd Sub\n", "M");
        dbg.SetBreakpoints("M", new[] { 3 });
        var stop = NextStop(dbg);
        var run = vb.Execute();
        (await stop.WaitAsync(Guard)).Line.Should().Be(3);
        debug.Should().BeEmpty();   // nothing printed yet

        // Run to line 6 (Debug.Print 4): lines 3,4,5 execute (print 1,2,3), break before line 6.
        var next = NextStop(dbg);
        dbg.RunToCursor("M", 6);
        dbg.Continue();
        var info = await next.WaitAsync(Guard);
        info.Line.Should().Be(6);
        info.Reason.Should().Be(StopReason.Breakpoint);   // a temp breakpoint hit
        debug.Select(v => v.Value).Should().Equal(1, 2, 3);   // 4 not printed yet

        // Target cleared (one-shot): a plain Continue now runs to completion.
        dbg.Continue();
        await run.WaitAsync(Guard);
        debug.Select(v => v.Value).Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public async Task RunToCursor_TargetNeverReached_RunsToCompletion()
    {
        var (vb, dbg) = NewDebuggable(
            "Go\nSub Go()\nDebug.Print 1\nDebug.Print 2\nEnd Sub\n", "M");
        dbg.SetBreakpoints("M", new[] { 3 });
        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.WaitAsync(Guard);

        dbg.RunToCursor("M", 99);   // no such line — never reached
        dbg.Continue();
        await run.WaitAsync(Guard);   // completes, no second break
        debug.Select(v => v.Value).Should().Equal(1, 2);
    }
}
