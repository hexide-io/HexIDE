using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Interpreter-debugger v2·P7c — statement execution in the Immediate window. A BARE assignment / Set is EXECUTED
/// against the paused frame (mutating its state), mirroring VB6 (`count = 5` assigns; `?count = 5` compares). User
/// Sub/Function calls remain rejected in break mode (D14–D15) — a bare user call, or a user call in an assignment's
/// right-hand side, returns the rejection message and mutates nothing. Bare expressions still evaluate.
/// </summary>
public class ImmediateStatementTests : BaseVBTestFixture
{

    private static Task<StoppedInfo> NextStop(DebugController dbg)
    {
        var tcs = new TaskCompletionSource<StoppedInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(StoppedInfo info) { dbg.Stopped -= Handler; tcs.TrySetResult(info); }
        dbg.Stopped += Handler;
        return tcs.Task;
    }

    // Break inside Go() with count=42, greeting="Ada" in scope (line 7), plus a Calc() user function.
    private async Task<(BasicInterpreter vb, DebugController dbg, Task run)> BreakWithLocals()
    {
        var (vb, dbg) = NewDebuggable(
            "Go\nSub Go()\nDim count As Integer\ncount = 42\nDim greeting As String\ngreeting = \"Ada\"\n" +
            "Debug.Print count\nEnd Sub\nFunction Calc() As Integer\nCalc = 99\nEnd Function\n", "Module1");
        dbg.SetBreakpoints("Module1", new[] { 7 });
        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.Guarded();
        return (vb, dbg, run);
    }

    [Fact]
    public async Task Assignment_MutatesTheFrame()
    {
        var (_, dbg, run) = await BreakWithLocals();

        (await dbg.EvaluateAsync("count = 100")).Should().BeEmpty();     // a statement — no echo
        (await dbg.EvaluateAsync("?count")).Should().Be("100");          // the frame WAS mutated
        (await dbg.EvaluateAsync("greeting = \"Bea\"")).Should().BeEmpty();
        (await dbg.EvaluateAsync("?greeting")).Should().Be("Bea");

        dbg.Stop();
        await run.Guarded();
    }

    [Fact]
    public async Task Assignment_ReadsCurrentValue()
    {
        var (_, dbg, run) = await BreakWithLocals();

        (await dbg.EvaluateAsync("count = count + 8")).Should().BeEmpty();   // reads then writes count
        (await dbg.EvaluateAsync("?count")).Should().Be("50");

        dbg.Stop();
        await run.Guarded();
    }

    [Fact]
    public async Task BareAssignment_VsPrefixedComparison()
    {
        var (_, dbg, run) = await BreakWithLocals();

        // `?count = 5` is an EXPRESSION (comparison): count is 42 -> False, and it must NOT assign.
        (await dbg.EvaluateAsync("?count = 5")).Should().Be("False");
        (await dbg.EvaluateAsync("?count")).Should().Be("42");   // unchanged

        dbg.Stop();
        await run.Guarded();
    }

    [Fact]
    public async Task UserCallInRhs_IsRejected_NoMutation()
    {
        var (_, dbg, run) = await BreakWithLocals();

        (await dbg.EvaluateAsync("count = Calc()")).Should().Contain("Immediate");   // user function rejected
        (await dbg.EvaluateAsync("?count")).Should().Be("42");                        // not mutated

        dbg.Stop();
        await run.Guarded();
    }

    [Fact]
    public async Task BareUserCall_IsNotExecuted()
    {
        var (_, dbg, run) = await BreakWithLocals();

        // A bare user call is NOT run as a statement — it falls through to expression evaluation, where the
        // user-call wall rejects it (a Function reaches the wall → the Immediate message; a Sub has no value as an
        // expression → a syntax error). Either way it returns a non-empty error and executes nothing.
        (await dbg.EvaluateAsync("Calc")).Should().Contain("Immediate");   // bare Function -> the user-call wall
        (await dbg.EvaluateAsync("Go")).Should().NotBeEmpty();             // bare Sub -> rejected (not run)
        debug.Should().BeEmpty();                                          // neither re-ran Go's body (line 7 not reached)

        dbg.Stop();
        await run.Guarded();
    }

    [Fact]
    public async Task SetObjectToNothing_Executes()
    {
        var (vb, dbg) = NewDebuggable(
            "Go\nSub Go()\nDim s As Ship\nSet s = New Ship\ns.Health = 100\nDebug.Print 1\nEnd Sub\n", "Module1",
            ("Ship", "Public Health As Integer\n"));
        dbg.SetBreakpoints("Module1", new[] { 6 });
        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.Guarded();

        (await dbg.EvaluateAsync("?s")).Should().Be("Ship");               // an object before
        (await dbg.EvaluateAsync("Set s = Nothing")).Should().BeEmpty();   // the Set statement executes
        (await dbg.EvaluateAsync("?s")).Should().Be("Nothing");            // now Nothing

        dbg.Stop();
        await run.Guarded();
    }
}
