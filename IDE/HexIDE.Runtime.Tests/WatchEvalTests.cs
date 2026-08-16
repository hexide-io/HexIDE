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
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(15);

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
        await stop.WaitAsync(Guard);
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
        await run.WaitAsync(Guard);
    }

    [Fact]
    public async Task EvaluateWatch_NotPaused_IsNull()
    {
        // A freshly-constructed controller is Running, not Paused → no frame to evaluate against.
        var (_, dbg) = NewDebuggable("Debug.Print 1\n", "Module1");
        (await dbg.EvaluateWatchAsync("1 + 1")).Should().BeNull();
    }
}
