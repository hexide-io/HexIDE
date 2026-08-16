using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Interpreter-debugger Phase 4 (Immediate) — <see cref="IDebugController.EvaluateAsync"/>, the read-only
/// expression evaluator behind the Immediate window. Break, then evaluate an expression against the paused frame:
/// variables, operators, intrinsics work; user-procedure calls + syntax errors return a message; not-paused is null.
/// </summary>
public class ImmediateEvalTests : BaseVBTestFixture
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(15);

    private static Task<StoppedInfo> NextStop(DebugController dbg)
    {
        var tcs = new TaskCompletionSource<StoppedInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(StoppedInfo info) { dbg.Stopped -= Handler; tcs.TrySetResult(info); }
        dbg.Stopped += Handler;
        return tcs.Task;
    }

    // Break inside Go() with `count`=42 and `greeting`="Ada" in scope (line 7), plus a Calc() function to test the
    // user-call rejection. Returns the paused (vb, dbg) — caller evaluates then Stops.
    private async Task<(BasicInterpreter vb, DebugController dbg, Task run)> BreakWithLocals()
    {
        var (vb, dbg) = NewDebuggable(
            "Go\nSub Go()\nDim count As Integer\ncount = 42\nDim greeting As String\ngreeting = \"Ada\"\n" +
            "Debug.Print count\nEnd Sub\nFunction Calc() As Integer\nCalc = 99\nEnd Function\n", "Module1");
        dbg.SetBreakpoints("Module1", new[] { 7 });
        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.WaitAsync(Guard);
        return (vb, dbg, run);
    }

    [Fact]
    public async Task Evaluate_Variable_Operator_String_Intrinsic_BareExpr()
    {
        var (_, dbg, run) = await BreakWithLocals();

        (await dbg.EvaluateAsync("?count")).Should().Be("42");
        (await dbg.EvaluateAsync("?count + 8")).Should().Be("50");
        (await dbg.EvaluateAsync("?greeting")).Should().Be("Ada");        // string prints verbatim (Debug.Print style)
        (await dbg.EvaluateAsync("?Len(greeting)")).Should().Be("3");     // intrinsic
        (await dbg.EvaluateAsync("count")).Should().Be("42");             // bare expression, no leading ?
        (await dbg.EvaluateAsync("Print greeting & \"!\"")).Should().Be("Ada!");   // Print prefix + concat

        dbg.Stop();
        await run.WaitAsync(Guard);
    }

    [Fact]
    public async Task Evaluate_UserProcedureCall_IsRejected()
    {
        var (_, dbg, run) = await BreakWithLocals();

        var result = await dbg.EvaluateAsync("?Calc()");
        result.Should().Contain("Immediate");   // "...not available in the Immediate window."

        dbg.Stop();
        await run.WaitAsync(Guard);
    }

    [Fact]
    public async Task Evaluate_SyntaxError_ReturnsMessage()
    {
        var (_, dbg, run) = await BreakWithLocals();

        (await dbg.EvaluateAsync("?count +")).Should().Be("Syntax error");

        dbg.Stop();
        await run.WaitAsync(Guard);
    }

    [Fact]
    public async Task Evaluate_ObjectPaths_FieldReadsAndClassName_ButCallsAndNewRejected()
    {
        // The guard lives at RunProcedure/NewObject (the sinks), so ALL user-code paths are covered — not just
        // bare-name calls. Field reads (direct env access) and object formatting still work.
        var (vb, dbg) = NewDebuggable(
            "Go\nSub Go()\nDim s As Ship\nSet s = New Ship\ns.Health = 100\nDebug.Print 1\nEnd Sub\n", "Module1",
            ("Ship", "Public Health As Integer\nPublic Function Hit() As Integer\nHit = Health - 10\nEnd Function\n" +
                     "Public Property Get HalfHealth() As Integer\nHalfHealth = Health \\ 2\nEnd Property\n"));
        dbg.SetBreakpoints("Module1", new[] { 6 });
        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.WaitAsync(Guard);

        (await dbg.EvaluateAsync("?s.Health")).Should().Be("100");                  // field read — not a call
        (await dbg.EvaluateAsync("?s")).Should().Be("Ship");                        // object -> class name
        (await dbg.EvaluateAsync("?s.Hit()")).Should().Contain("Immediate");        // instance METHOD call rejected
        (await dbg.EvaluateAsync("?s.HalfHealth")).Should().Contain("Immediate");   // PROPERTY GET rejected
        (await dbg.EvaluateAsync("?New Ship")).Should().Contain("Immediate");       // NEW rejected

        dbg.Stop();
        await run.WaitAsync(Guard);
    }

    [Fact]
    public async Task Evaluate_WhenNotPaused_ReturnsNull()
    {
        var (vb, dbg) = NewDebuggable("Debug.Print 1\n", "Module1");
        (await dbg.EvaluateAsync("?1")).Should().BeNull();   // never started
        await vb.Execute().WaitAsync(Guard);
        (await dbg.EvaluateAsync("?1")).Should().BeNull();   // finished, not paused
    }
}
