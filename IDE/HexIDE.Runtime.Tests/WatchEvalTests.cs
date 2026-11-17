using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Interpreter-debugger P6a — the typed Watch evaluation seam (<see cref="IDebugController.EvaluateWatchAsync"/>).
/// Break, then evaluate a watch against the paused frame: Ok / Display / TypeName for a value, Truthy for a Boolean
/// condition, Ok=false with a message for a bad expression, null when not in Break mode.
/// </summary>
public class WatchEvalTests : BaseVBTestFixture
{

    private static Task<StoppedInfo> NextStop(DebugController dbg)
    {
        var tcs = new TaskCompletionSource<StoppedInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(StoppedInfo info) { dbg.Stopped -= Handler; tcs.TrySetResult(info); }
        dbg.Stopped += Handler;
        return tcs.Task;
    }

    // Break inside Go() with count=42 (Integer) in scope at line 7.
    private async Task<(DebugController dbg, Task run)> BreakWithLocals()
    {
        var (vb, dbg) = NewDebuggable(
            "Go\nSub Go()\nDim count As Integer\ncount = 42\nDim greeting As String\ngreeting = \"Ada\"\n" +
            "Debug.Print count\nEnd Sub\n", "Module1");
        dbg.SetBreakpoints("Module1", new[] { 7 });
        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.Guarded();
        return (dbg, run);
    }

    [Fact]
    public async Task EvaluateWatch_Value_Truthiness_And_Error()
    {
        var (dbg, run) = await BreakWithLocals();

        var v = await dbg.EvaluateWatchAsync("count");
        v!.Ok.Should().BeTrue();
        v.Display.Should().Be("42");
        v.TypeName.Should().Be("Integer");

        (await dbg.EvaluateWatchAsync("count > 10"))!.Truthy.Should().BeTrue();     // Break-When-True fodder
        (await dbg.EvaluateWatchAsync("count > 100"))!.Truthy.Should().BeFalse();

        var bad = await dbg.EvaluateWatchAsync("count +");                          // syntax error
        bad!.Ok.Should().BeFalse();
        bad.Display.Should().NotBeNullOrEmpty();

        dbg.Stop();
        await run.Guarded();
    }

    [Fact]
    public async Task UndeclaredMemberWatchDoesNotCreateVariable()
    {
        var (dbg, run) = await BreakWithLocals();
        try
        {
            (await dbg.EvaluateWatchAsync("Nope.F()"))!.Ok.Should().BeFalse();
            dbg.GetLocals()!.Locals.Should().NotContain(n => n.Name == "Nope");
        }
        finally
        {
            dbg.Stop();
            await run.Guarded();
        }
    }

    [Fact]
    public async Task EvaluateWatch_NotPaused_IsNull()
    {
        // A freshly-constructed controller is Running, not Paused → no frame to evaluate against.
        var (_, dbg) = NewDebuggable("Debug.Print 1\n", "Module1");
        (await dbg.EvaluateWatchAsync("1 + 1")).Should().BeNull();
    }
    [Theory]
    [InlineData("inner", "Module1", false, "<Expression not defined in context>", "Empty")]
    [InlineData("total", "Module1", false, "<Expression not defined in context>", "Empty")]
    [InlineData("nosuch", "Module1", false, "<Expression not defined in context>", "Empty")]
    [InlineData("modvar", "Module1", false, "5", "Integer")]
    [InlineData("inner", "Module1.Square", false, "49", "Integer")]
    [InlineData("total", "Module1.Form_Load", false, "0", "Integer")]
    [InlineData("inner", "Module1", true, "<Expression not defined in context>", "Empty")]
    [InlineData("total", "Module1", true, "<Expression not defined in context>", "Empty")]
    [InlineData("nosuch", "Module1", true, "<Expression not defined in context>", "Empty")]
    [InlineData("modvar", "Module1", true, "5", "Integer")]
    [InlineData("inner", "Module1.Square", true, "<Out of context>", "Empty")]
    [InlineData("total", "Module1.Form_Load", true, "49", "Integer")]
    public async Task StoredWatch_UsesMeasuredContext(string expression, string context, bool stepOut,
        string display, string type)
    {
        var (vb, dbg) = NewDebuggable("""
            Dim modvar As Integer
            Form_Load
            Sub Form_Load()
                Dim total As Integer
                modvar = 5
                total = Square(7)
                Stop
                Debug.Print total
            End Sub
            Function Square(ByVal n As Integer) As Integer
                Dim inner As Integer
                inner = n * n
                Stop
                Square = inner
            End Function
            """, "Module1");
        var stopped = NextStop(dbg);
        var run = vb.Execute();
        await stopped.Guarded();
        try
        {
            if (stepOut)
            {
                stopped = NextStop(dbg);
                dbg.StepOut();
                await stopped.Guarded();
            }
            var result = await ((IDebugController)dbg).EvaluateScopedWatchAsync(expression, context);
            result.Should().NotBeNull();
            result!.Display.Should().Be(display);
            result.TypeName.Should().Be(type);
            result.Ok.Should().Be(type != "Empty");
        }
        finally
        {
            dbg.Stop();
            await run.Guarded();
        }
    }

    // docs/vb6-fidelity-oracle/a-watch-and-its-context.md, "A watch scoped to one accessor, and a watch on (All Modules)"
    // (#4452): such a watch sees the project's Public module-level names only, wherever the program is paused. A standard
    // module's Private variable, a class's Private variable and a local are not defined there, even in their own scope.
    [Theory]
    [InlineData("g", false, "7", "Long")]
    [InlineData("1 + 1", false, "2", "Integer")]
    [InlineData("p", false, "<Expression not defined in context>", "Empty")]
    [InlineData("m", false, "<Expression not defined in context>", "Empty")]
    [InlineData("v", false, "<Expression not defined in context>", "Empty")]
    [InlineData("zq", false, "<Expression not defined in context>", "Empty")]
    [InlineData("g", true, "7", "Long")]
    [InlineData("1 + 1", true, "2", "Integer")]
    [InlineData("p", true, "<Expression not defined in context>", "Empty")]
    [InlineData("m", true, "<Expression not defined in context>", "Empty")]
    [InlineData("zq", true, "<Expression not defined in context>", "Empty")]
    public async Task StoredWatch_OnAllModules_SeesOnlyPublicModuleNames(string expression, bool inFormLoad,
        string display, string type)
    {
        var (vb, dbg) = NewDebuggableProject("""
            Form_Load
            Sub Form_Load()
                Dim zq As Long
                SetP
                g = 7
                Dim c As New Class1
                c.X = 5
                zq = 8
                Stop
            End Sub
            """,
            [("Module2", """
                Public g As Long
                Private p As Long
                Public Sub SetP()
                    p = 3
                End Sub
                """)],
            [("Class1", """
                Private m As Long
                Public Property Let X(ByVal v As Long)
                    Stop
                    m = v
                End Property
                """)]);
        var stopped = NextStop(dbg);
        var run = vb.Execute();
        await stopped.Guarded();
        try
        {
            if (inFormLoad)
            {
                stopped = NextStop(dbg);
                dbg.Continue();
                await stopped.Guarded();
                (await dbg.EvaluateWatchAsync("zq"))!.Display.Should().Be("8", "the local is in scope where the program paused");
            }
            else (await dbg.EvaluateWatchAsync("m"))!.Display.Should().Be("0", "the class's Private is in scope in its own Let");
            var result = await ((IDebugController)dbg).EvaluateScopedWatchAsync(expression, "(All Modules)");
            result.Should().NotBeNull();
            result!.Display.Should().Be(display);
            result.TypeName.Should().Be(type);
            result.Ok.Should().Be(type != "Empty");
        }
        finally
        {
            dbg.Stop();
            await run.Guarded();
        }
    }

    // docs/vb6-fidelity-oracle/a-watch-and-its-context.md, "A watch scoped to one accessor" (measured in a class,
    // #4401): each Property accessor is a context of its own, and the module's watch evaluates in either.
    [Theory]
    [InlineData("m", "Module1.X(Get)", false, "<Out of context>", "Empty")]
    [InlineData("m", "Module1.X(Let)", false, "0", "Long")]
    [InlineData("v", "Module1.X(Let)", false, "5", "Long")]
    [InlineData("m", "Module1", false, "0", "Long")]
    [InlineData("m", "Module1.X(Get)", true, "5", "Long")]
    [InlineData("m", "Module1.X(Let)", true, "<Out of context>", "Empty")]
    [InlineData("v", "Module1.X(Let)", true, "<Out of context>", "Empty")]
    [InlineData("m", "Module1", true, "5", "Long")]
    public async Task StoredWatch_TellsPropertyAccessorsApart(string expression, string context, bool inGet,
        string display, string type)
    {
        var (vb, dbg) = NewDebuggable("""
            Dim m As Long
            Form_Load
            Sub Form_Load()
                X = 5
                Debug.Print X
            End Sub
            Property Get X() As Long
                Stop
                X = m
            End Property
            Property Let X(ByVal v As Long)
                Stop
                m = v
            End Property
            """, "Module1");
        var stopped = NextStop(dbg);
        var run = vb.Execute();
        await stopped.Guarded();
        try
        {
            dbg.GetLocals()!.WatchContext.Should().Be("Module1.X(Let)");
            if (inGet)
            {
                stopped = NextStop(dbg);
                dbg.Continue();
                await stopped.Guarded();
                dbg.GetLocals()!.WatchContext.Should().Be("Module1.X(Get)");
            }
            var result = await ((IDebugController)dbg).EvaluateScopedWatchAsync(expression, context);
            result.Should().NotBeNull();
            result!.Display.Should().Be(display);
            result.TypeName.Should().Be(type);
            result.Ok.Should().Be(type != "Empty");
        }
        finally
        {
            dbg.Stop();
            await run.Guarded();
        }
    }

}
