using System;
using System.Collections.Generic;
using System.Linq;
using HexIDE.Runtime.Debugging;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Interpreter-debugger v2·P6b — the VB6 break-type watches evaluated at the pause-gate: "Break When Value Is True"
/// and "Break When Value Has Changed" are edge-triggered (docs/vb6-fidelity-oracle/break-when-value-is-true.md,
/// #869). Drive a real run with break-watches pushed
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

    // VB6 SP6 measurements: docs/vb6-fidelity-oracle/break-when-value-is-true.md (#869).
    [Theory]
    [InlineData("\"True\"", 1)]
    [InlineData("\"1\"", 1)]
    [InlineData("\" 5 \"", 1)]
    [InlineData("\"&H10\"", 1)]
    [InlineData("\"abc\"", 1)]
    [InlineData("\"\"", 1)]
    [InlineData("\"0\"", 0)] // Deliberately retain today's result: the page has no defensible rule for VB6's 3.
    [InlineData("\"False\"", 0)]
    [InlineData("0", 0)]
    [InlineData("Null", 0)]
    [InlineData("Empty", 0)]
    [InlineData("7", 1)]
    [InlineData("True", 1)]
    [InlineData("#1/2/2000#", 1)]
    public async Task BreakWhenTrue_MeasuredValues_BreakOnceAtTheFollowingStatement(string value, int breaks)
    {
        var (vb, dbg) = NewDebuggable(
            $"Go\nSub Go()\nDim v As Variant\nDim n As Integer\nv = False\nn = 1\nv = {value}\nn = 2\nn = 3\nv = False\nn = 4\nn = 5\nEnd Sub\n", "M");
        dbg.SetWatchBreaks(new[] { new WatchBreakSpec("v", BreakOnChange: false) });
        var stops = new List<StoppedInfo>();
        dbg.Stopped += info => { stops.Add(info); dbg.Continue(); };

        await vb.Execute().Guarded();

        stops.Should().HaveCount(breaks);
        stops.Select(info => info.Line).Should().Equal(Enumerable.Repeat(8, breaks));
        stops.Should().OnlyContain(info => info.Reason == StopReason.Watch);
    }

    [Fact]
    public async Task ConditionalBreakpoint_NonConvertibleString_KeepsNotStopping()
    {
        // #869 changes watch truth only; conditional breakpoints retain today's rule.
        var (vb, dbg) = NewDebuggable("Go\nSub Go()\nDebug.Print 1\nEnd Sub\n", "M");
        dbg.SetBreakpoints("M", [3]);
        dbg.SetBreakpointConditions("M", new Dictionary<int, string> { [3] = "\"abc\"" });
        var stops = new List<StoppedInfo>();
        dbg.Stopped += info => { stops.Add(info); dbg.Continue(); };

        await vb.Execute().Guarded();

        stops.Should().BeEmpty();
        debug.Select(v => v.Value).Should().Equal(1);
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

        // Clear the watch and run free to completion.
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
    public async Task BreakWhenChanged_SeesAChangeBelowTheDigitsTheValueIsShownWith()
    {
        // Line 5 moves x by two units in the last place, which its 15-digit text does not show (#772). The watch is
        // about the value, so it must still break.
        var (vb, dbg) = NewDebuggable(
            "Go\nSub Go()\nDim x As Double\nx = 1 / 3\nx = x + 1E-16\nDebug.Print \"end\"\nEnd Sub\n", "M");
        dbg.SetWatchBreaks(new[] { new WatchBreakSpec("x", BreakOnChange: true) });

        var first = NextStop(dbg);
        var run = vb.Execute();
        (await first.Guarded()).Line.Should().Be(5);   // 0 -> 1/3
        var second = NextStop(dbg);
        dbg.Continue();
        var info = await second.Guarded();

        info.Reason.Should().Be(StopReason.Watch);
        info.Line.Should().Be(6);   // 1/3 -> 1/3 + 1E-16, both shown as 0.333333333333333

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
