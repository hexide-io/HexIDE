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
        await stop.Guarded();
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
        await run.Guarded();
    }

    [Fact]
    public async Task Evaluate_an_object_prints_its_default_member_or_says_why_not()
    {
        // `? w` prints w's default member in VB6. Reading a class's calls into the program, which break mode refuses as it
        // refuses `?obj.Prop` (D15); a class with none is 438, and Err is its Number. It used to print the class's name.
        var (vb, dbg) = NewDebuggable(
            "Go\nSub Go()\nDim w As Widget\nSet w = New Widget\nDim k As Plain\nSet k = New Plain\nDebug.Print 1\nEnd Sub\n",
            "Module1",
            ("Widget", "Public Function Value() As Long\nAttribute Value.VB_UserMemId = 0\n    Value = 42\nEnd Function\n"),
            ("Plain", "Public Tag As Long\n"));
        dbg.SetBreakpoints("Module1", new[] { 7 });
        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.Guarded();

        (await dbg.EvaluateAsync("?w")).Should().Contain("Immediate");
        (await dbg.EvaluateAsync("?k")).Should().Contain("438");
        (await dbg.EvaluateAsync("?Err")).Should().Be("0");
        (await dbg.EvaluateAsync("?TypeName(w)")).Should().Be("Widget");

        dbg.Stop();
        await run.Guarded();
    }

    [Fact]
    public async Task Evaluate_UserProcedureCall_IsRejected()
    {
        var (_, dbg, run) = await BreakWithLocals();

        var result = await dbg.EvaluateAsync("?Calc()");
        result.Should().Contain("Immediate");   // "...not available in the Immediate window."

        dbg.Stop();
        await run.Guarded();
    }

    [Fact]
    public async Task Evaluate_SyntaxError_ReturnsMessage()
    {
        var (_, dbg, run) = await BreakWithLocals();

        (await dbg.EvaluateAsync("?count +")).Should().Be("Syntax error");

        dbg.Stop();
        await run.Guarded();
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
        await stop.Guarded();

        (await dbg.EvaluateAsync("?s.Health")).Should().Be("100");                  // field read — not a call
        (await dbg.EvaluateAsync("?s")).Should().Contain("438");                    // no default member: 438, as in VB6
        (await dbg.EvaluateAsync("?s.Hit()")).Should().Contain("Immediate");        // instance METHOD call rejected
        (await dbg.EvaluateAsync("?s.HalfHealth")).Should().Contain("Immediate");   // PROPERTY GET rejected
        (await dbg.EvaluateAsync("?New Ship")).Should().Contain("Immediate");       // NEW rejected

        dbg.Stop();
        await run.Guarded();
    }

    [Fact]
    public async Task Evaluate_WhenNotPaused_ReturnsNull()
    {
        var (vb, dbg) = NewDebuggable("Debug.Print 1\n", "Module1");
        (await dbg.EvaluateAsync("?1")).Should().BeNull();   // never started
        await vb.Execute().Guarded();
        (await dbg.EvaluateAsync("?1")).Should().BeNull();   // finished, not paused
    }
}
