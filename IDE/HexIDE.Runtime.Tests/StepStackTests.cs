using System.Linq;
using HexIDE.Runtime.Debugging;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Interpreter-debugger v2·P5 — Step Over / Step Out (call-depth) and the Call Stack. Drive a real run to a
/// breakpoint, then step and assert the break lands in the right frame; and read the activation chain via
/// <see cref="IDebugController.GetCallStack"/>.
/// </summary>
public class StepStackTests : BaseVBTestFixture
{

    private static Task<StoppedInfo> NextStop(DebugController dbg)
    {
        var tcs = new TaskCompletionSource<StoppedInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(StoppedInfo info) { dbg.Stopped -= Handler; tcs.TrySetResult(info); }
        dbg.Stopped += Handler;
        return tcs.Task;
    }

    [Fact]
    public async Task StepOver_RunsCalledSub_BreaksAtNextStatementInCurrentFrame()
    {
        var (vb, dbg) = NewDebuggable(
            "Go\nSub Go()\nDim x As Integer\nx = 1\nHelper\nDebug.Print x\nEnd Sub\nSub Helper()\nDebug.Print 99\nEnd Sub\n", "M");
        dbg.SetBreakpoints("M", new[] { 5 });   // the `Helper` call line
        var stop = NextStop(dbg);
        var run = vb.Execute();
        (await stop.Guarded()).Line.Should().Be(5);

        var next = NextStop(dbg);
        dbg.StepOver();
        var info = await next.Guarded();
        info.Line.Should().Be(6);                       // AFTER the call, in Go — NOT inside Helper
        info.Reason.Should().Be(StopReason.Step);
        debug.Select(v => v.Value).Should().Contain(99);   // Helper ran to completion

        dbg.Continue();
        await run.Guarded();
    }

    [Fact]
    public async Task StepOver_OnNonCall_BehavesLikeStepInto()
    {
        var (vb, dbg) = NewDebuggable("Debug.Print 1\nDebug.Print 2\nDebug.Print 3\n", "M");
        dbg.SetBreakpoints("M", new[] { 2 });
        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.Guarded();

        var next = NextStop(dbg);
        dbg.StepOver();
        (await next.Guarded()).Line.Should().Be(3);   // next statement, same frame

        dbg.Continue();
        await run.Guarded();
    }

    [Fact]
    public async Task StepOut_RunsRestOfProc_BreaksInCaller()
    {
        var (vb, dbg) = NewDebuggable(
            "Go\nSub Go()\nHelper\nDebug.Print 7\nEnd Sub\nSub Helper()\nDebug.Print 1\nDebug.Print 2\nDebug.Print 3\nEnd Sub\n", "M");
        dbg.SetBreakpoints("M", new[] { 8 });   // Debug.Print 2, inside Helper
        var stop = NextStop(dbg);
        var run = vb.Execute();
        (await stop.Guarded()).Line.Should().Be(8);
        debug.Should().ContainSingle();   // only "1" has run so far

        var next = NextStop(dbg);
        dbg.StepOut();
        var info = await next.Guarded();
        info.Line.Should().Be(4);          // back in Go, the statement after the Helper call
        debug.Select(v => v.Value).Should().Equal(1, 2, 3);   // Helper finished (2, 3 ran)

        dbg.Continue();
        await run.Guarded();
    }

    // Regression (adversarial review, HIGH): with the old Max(1, ActivationStack.Count) depth clamp, the module
    // top-level frame (count 0) and a first-level proc (count 1) both reported depth 1, so Step Over from top-level
    // DESCENDED into the callee. Per-frame captured depth (top-level = 0) fixes it.
    [Fact]
    public async Task StepOver_FromModuleTopLevel_DoesNotDescendIntoCallee()
    {
        var (vb, dbg) = NewDebuggable(
            "Go\nDebug.Print 5\nSub Go()\nDebug.Print 1\nEnd Sub\n", "M");
        dbg.SetBreakpoints("M", new[] { 1 });   // the top-level `Go` call
        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.Guarded();

        var next = NextStop(dbg);
        dbg.StepOver();
        var info = await next.Guarded();
        info.Line.Should().Be(2);                          // back at top-level — NOT inside Go (line 4)
        info.Reason.Should().Be(StopReason.Step);
        debug.Select(v => v.Value).Should().Contain(1);    // Go ran to completion
        debug.Select(v => v.Value).Should().NotContain(5); // ...but the top-level continuation hasn't run yet

        dbg.Continue();
        await run.Guarded();
    }

    // Regression (adversarial review, HIGH): with the old clamp, Step Out from a first-level proc (target depth 1)
    // required depth < 1, which the Max(1, …) clamp made unreachable, so Step Out to a top-level caller NEVER fired
    // and the program ran free. Per-frame depth (top-level = 0) lets it break in the caller.
    [Fact]
    public async Task StepOut_FromProcCalledAtTopLevel_BreaksAtModuleCaller()
    {
        var (vb, dbg) = NewDebuggable(
            "Go\nDebug.Print 9\nSub Go()\nDebug.Print 1\nDebug.Print 2\nEnd Sub\n", "M");
        dbg.SetBreakpoints("M", new[] { 4 });   // Debug.Print 1, inside Go
        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.Guarded();

        var next = NextStop(dbg);
        dbg.StepOut();
        var info = await next.Guarded();
        info.Line.Should().Be(2);                          // back at the top-level caller — NOT run to completion
        info.Reason.Should().Be(StopReason.Step);
        debug.Select(v => v.Value).Should().Equal(1, 2);   // Go finished; the top-level continuation (9) hasn't run

        dbg.Continue();
        await run.Guarded();
    }

    [Fact]
    public async Task GetCallStack_ReturnsChain_DeepestFirst_WithProcNamesAndLines()
    {
        var (vb, dbg) = NewDebuggable(
            "A\nSub A()\nB\nEnd Sub\nSub B()\nC\nEnd Sub\nSub C()\nDebug.Print 1\nEnd Sub\n", "M");
        dbg.SetBreakpoints("M", new[] { 9 });   // inside C
        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.Guarded();

        var frames = dbg.GetCallStack();
        frames.Select(f => f.ProcName).Should().Equal("C", "B", "A");   // deepest (current) first
        frames.Select(f => f.Line).Should().Equal(9, 6, 3);            // C's break line, B's call line, A's call line
        frames.Should().OnlyContain(f => f.Module == "M");

        dbg.Stop();
        await run.Guarded();
    }
}
