using HexIDE.Runtime.Debugging;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Interpreter-debugger Phase 1 (Core) — the cooperative pause-gate driven directly through IDebugController
/// (headless; the same gate the live IDE/MCP run uses). Await the <see cref="IDebugController.Stopped"/> event to
/// know the walk has reached a break (robust to the time-slice yield, which can make the walk go async before the
/// break under load), then assert the paused state and drive Continue/Stop.
/// </summary>
public class DebuggerTests : BaseVBTestFixture
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(15);   // generous — avoid false timeouts under full-suite parallel load (matches the other debugger test fixtures)

    // A task that completes when the controller next enters Break (one-shot).
    private static Task<StoppedInfo> NextStop(DebugController dbg)
    {
        var tcs = new TaskCompletionSource<StoppedInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(StoppedInfo info) { dbg.Stopped -= Handler; tcs.TrySetResult(info); }
        dbg.Stopped += Handler;
        return tcs.Task;
    }

    [Fact]
    public async Task Breakpoint_StopsAtLine_ThenContinueResumes()
    {
        var (vb, dbg) = NewDebuggable("Debug.Print \"a\"\nDebug.Print \"b\"\nDebug.Print \"c\"\n", "M");
        dbg.SetBreakpoints("M", new[] { 2 });

        var stop = NextStop(dbg);
        var run = vb.Execute();
        var info = await stop.WaitAsync(Guard);

        info.Line.Should().Be(2);
        info.Reason.Should().Be(StopReason.Breakpoint);
        dbg.State.Should().Be(DebugState.Paused);
        debug.Should().HaveCount(1);   // only "a" ran before the break
        run.IsCompleted.Should().BeFalse();

        dbg.Continue();
        await run.WaitAsync(Guard);
        dbg.State.Should().Be(DebugState.Running);
        AssertDebugLog(["a", "b", "c"]);
    }

    [Fact]
    public async Task NoBreakpoint_RunsToCompletion_NeverPauses()
    {
        var (vb, dbg) = NewDebuggable("Debug.Print \"x\"\nDebug.Print \"y\"\n", "M");
        await vb.Execute().WaitAsync(Guard);
        dbg.State.Should().Be(DebugState.Running);   // never entered Break
        AssertDebugLog(["x", "y"]);
    }

    [Fact]
    public async Task StopStatement_UnderController_EntersBreak()
    {
        var (vb, dbg) = NewDebuggable("Debug.Print \"before\"\nStop\nDebug.Print \"after\"\n", "M");

        var stop = NextStop(dbg);
        var run = vb.Execute();
        var info = await stop.WaitAsync(Guard);

        info.Reason.Should().Be(StopReason.StopStatement);
        info.Line.Should().Be(2);
        debug.Should().HaveCount(1);   // "before" only

        dbg.Continue();
        await run.WaitAsync(Guard);
        AssertDebugLog(["before", "after"]);
    }

    [Fact]
    public async Task StopStatement_Headless_TerminatesProgram_NoNotImplemented()
    {
        // Run() uses a null controller — the compiled/standalone path. `Stop` ends the program (does NOT throw
        // NotImplementedException as before), so the statement after Stop never runs.
        await Run("Debug.Print \"before\"\nStop\nDebug.Print \"after\"\n").WaitAsync(Guard);
        AssertDebugLog(["before"]);
    }

    [Fact]
    public async Task Reset_UnwindsWithoutFiringClassTerminate()
    {
        // Break inside Go() while its local `c` (a live object with a Class_Terminate) is held; then Stop (reset).
        // The unwind runs Go's refcount teardown finally, but VB6 End fires NO Terminate (oracle-verified) — the
        // aborting-guard suppresses it — and nothing escapes the run.
        var (vb, dbg) = NewDebuggable(
            "Go\nDebug.Print \"top-after\"\n" +
            "Sub Go()\nDim c As Ob\nSet c = New Ob\nDebug.Print \"in-go\"\nEnd Sub\n", "M",
            ("Ob", "Private Sub Class_Terminate()\nDebug.Print \"TERMINATE\"\nEnd Sub\n"));
        dbg.SetBreakpoints("M", new[] { 6 });   // the "in-go" line, inside Go, with c live

        var stop = NextStop(dbg);
        var run = vb.Execute();
        var info = await stop.WaitAsync(Guard);
        info.Line.Should().Be(6);

        dbg.Stop();
        await run.WaitAsync(Guard);   // StopExecutionSignal unwinds cleanly and is swallowed
        // Nothing printed: "in-go" was never reached, the reset skipped "top-after", and NO "TERMINATE" fired.
        AssertDebugLog([]);
    }

    [Fact]
    public async Task OnErrorResumeNext_DoesNotSwallowBreak()
    {
        // A breakpoint on a statement inside an On Error Resume Next scope still Stops (the On-Error trap catches
        // only VBRunTimeException, never the break suspension).
        var (vb, dbg) = NewDebuggable("On Error Resume Next\nDebug.Print \"a\"\nDebug.Print \"b\"\n", "M");
        dbg.SetBreakpoints("M", new[] { 3 });

        var stop = NextStop(dbg);
        var run = vb.Execute();
        var info = await stop.WaitAsync(Guard);
        info.Line.Should().Be(3);
        debug.Should().HaveCount(1);   // "a" ran, break before "b"

        dbg.Continue();
        await run.WaitAsync(Guard);
        AssertDebugLog(["a", "b"]);
    }

    [Fact]
    public async Task Break_FreezesNewActivations_UntilContinue()
    {
        // While paused, a newly-fired activation (simulated Timer/event) must not run its body until Continue.
        var (vb, dbg) = NewDebuggable(
            "Debug.Print \"main1\"\nDebug.Print \"main2\"\n" +
            "Sub Other()\nDebug.Print \"other\"\nEnd Sub\n", "M");
        dbg.SetBreakpoints("M", new[] { 2 });

        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.WaitAsync(Guard);
        debug.Should().HaveCount(1);   // "main1"

        var other = vb.ExecuteSub("Other");   // fires while paused → frozen at its first gate (before printing)
        other.IsCompleted.Should().BeFalse();  // can't complete while frozen on the resume gate

        dbg.Continue();
        await run.WaitAsync(Guard);
        await other.WaitAsync(Guard);
        debug.Select(v => v.Value?.ToString()).Should().Contain("other").And.Contain("main2");
    }

    [Fact]
    public async Task StopThenReset_WhilePaused_UnwindsStaleWalk_DoesNotResurrect()
    {
        // Reproduces the RestartProject-while-paused race at the controller level: End (Stop) then a new run's
        // Reset() happen in one turn, before the parked walk's posted continuation runs. Reset() clears _isAborting,
        // so the session generation — not the abort flag — must kill the stale walk. It must NOT resume and run the
        // post-break statements onto the (now new) session.
        var (vb, dbg) = NewDebuggable("Debug.Print \"a\"\nDebug.Print \"b\"\nDebug.Print \"c\"\n", "M");
        dbg.SetBreakpoints("M", new[] { 2 });

        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.WaitAsync(Guard);
        debug.Should().HaveCount(1);   // "a" only

        dbg.Stop();     // End
        dbg.Reset();    // new run's fresh session, clearing _isAborting in the same turn

        await run.WaitAsync(Guard);    // the stale walk unwinds via the session check
        debug.Should().HaveCount(1);   // "b"/"c" never ran — no resurrection
        debug.Select(v => v.Value?.ToString()).Should().NotContain("b").And.NotContain("c");
    }

    [Fact]
    public async Task Stop_WhilePaused_RaisesContinued_SoTheCurrentLineBarClears()
    {
        // Ending while paused must signal "left break mode" so the editor clears the amber current-statement bar
        // (its only other clear path is the Continued event on resume).
        var (vb, dbg) = NewDebuggable("Debug.Print \"a\"\nDebug.Print \"b\"\n", "M");
        dbg.SetBreakpoints("M", new[] { 2 });

        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.WaitAsync(Guard);

        var continued = false;
        dbg.Continued += () => continued = true;
        dbg.Stop();
        await run.WaitAsync(Guard);

        continued.Should().BeTrue();
    }

    [Fact]
    public async Task StepInto_FromStart_BreaksBeforeEachStatement_InOrder()
    {
        // F8-from-idle: arm step before the run so it breaks at the FIRST statement (before it executes), then
        // steps line by line.
        var (vb, dbg) = NewDebuggable("Debug.Print \"a\"\nDebug.Print \"b\"\nDebug.Print \"c\"\n", "M");
        dbg.StepInto();   // arm step (as ProjectRunnerService does on F8-from-idle)

        var s1 = NextStop(dbg);
        var run = vb.Execute();
        var i1 = await s1.WaitAsync(Guard);
        i1.Line.Should().Be(1);
        i1.Reason.Should().Be(StopReason.Step);
        debug.Should().BeEmpty();   // broke BEFORE line 1 ran

        var s2 = NextStop(dbg);
        dbg.StepInto();
        (await s2.WaitAsync(Guard)).Line.Should().Be(2);
        debug.Select(v => v.Value?.ToString()).Should().Equal("a");   // line 1 ran, break before line 2

        var s3 = NextStop(dbg);
        dbg.StepInto();
        (await s3.WaitAsync(Guard)).Line.Should().Be(3);

        dbg.Continue();   // F5 from a step break runs free
        await run.WaitAsync(Guard);
        AssertDebugLog(["a", "b", "c"]);
    }

    [Fact]
    public async Task StepInto_DescendsIntoCalledSub()
    {
        var (vb, dbg) = NewDebuggable(
            "Debug.Print \"main-before\"\nFoo\nDebug.Print \"main-after\"\n" +
            "Sub Foo()\nDebug.Print \"in-foo\"\nEnd Sub\n", "M");
        dbg.SetBreakpoints("M", new[] { 2 });   // the "Foo" call line

        var stop = NextStop(dbg);
        var run = vb.Execute();
        (await stop.WaitAsync(Guard)).Line.Should().Be(2);
        debug.Select(v => v.Value?.ToString()).Should().Equal("main-before");

        // Step Into the call → break at Foo's first body statement (line 5), not "main-after".
        var s2 = NextStop(dbg);
        dbg.StepInto();
        var inFoo = await s2.WaitAsync(Guard);
        inFoo.Line.Should().Be(5);
        inFoo.Reason.Should().Be(StopReason.Step);

        dbg.Continue();
        await run.WaitAsync(Guard);
        AssertDebugLog(["main-before", "in-foo", "main-after"]);
    }

    [Fact]
    public async Task Continue_FromStepBreak_RunsFree_NoFurtherStepBreaks()
    {
        var (vb, dbg) = NewDebuggable("Debug.Print \"a\"\nDebug.Print \"b\"\nDebug.Print \"c\"\n", "M");
        dbg.StepInto();

        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.WaitAsync(Guard);   // paused at line 1 (step)

        dbg.Continue();                // disarms step
        await run.WaitAsync(Guard);
        AssertDebugLog(["a", "b", "c"]);   // ran to completion, no further step break
    }

    [Fact]
    public async Task Stepping_DoesNotOrphanAFrozenNewcomer_ReleasedOnContinue()
    {
        // A Timer/control event that froze while paused must still run when the user eventually Continues, even
        // after several Step Into presses (the resume gate persists across step re-breaks, not orphaned).
        var (vb, dbg) = NewDebuggable(
            "Debug.Print \"m1\"\nDebug.Print \"m2\"\nDebug.Print \"m3\"\n" +
            "Sub Other()\nDebug.Print \"other\"\nEnd Sub\n", "M");
        dbg.SetBreakpoints("M", new[] { 1 });

        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.WaitAsync(Guard);   // paused at line 1

        var other = vb.ExecuteSub("Other");   // newcomer fires while paused → frozen
        other.IsCompleted.Should().BeFalse();

        var s2 = NextStop(dbg); dbg.StepInto(); (await s2.WaitAsync(Guard)).Line.Should().Be(2);
        var s3 = NextStop(dbg); dbg.StepInto(); (await s3.WaitAsync(Guard)).Line.Should().Be(3);
        other.IsCompleted.Should().BeFalse();  // still frozen across the steps, not stolen or orphaned

        dbg.Continue();
        await run.WaitAsync(Guard);
        await other.WaitAsync(Guard);          // released + ran on Continue
        debug.Select(v => v.Value?.ToString()).Should().Contain("other");
    }

    [Fact]
    public void IsSessionActive_IsTrueBetweenResetAndStop()
    {
        // The IDE reads this to know a run is live (running OR paused) without depending on the project runner —
        // it drives the "edit while running → reset your project?" prompt.
        var (_, dbg) = NewDebuggable("Debug.Print \"a\"\n", "M");
        dbg.IsSessionActive.Should().BeFalse();   // fresh controller = idle

        dbg.Reset();
        dbg.IsSessionActive.Should().BeTrue();     // a run started

        dbg.Stop();
        dbg.IsSessionActive.Should().BeFalse();    // the run ended
    }
}
