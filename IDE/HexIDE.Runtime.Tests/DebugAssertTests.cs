using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// <c>Debug.Assert</c> (#1467), which stopped the program even when its condition held. In the VB6 IDE a condition that does
/// not hold enters break mode at the statement and one that holds goes on, the condition read as <c>If</c> reads it. A
/// compiled program has no <c>Debug.Assert</c> at all, its argument not even evaluated, which is what HexIDE does headless,
/// with no debugger, as it ends the program at <c>Stop</c> there. Measured with vb6.exe, the IDE through <c>/runexit</c> and
/// the EXE native and p-code (the oracle's "Debug.Assert").
/// </summary>
public class DebugAssertTests : BaseVBTestFixture
{
    private static List<Vb6Value> Lines(string transcript) =>
        [.. transcript.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Select(l => new Vb6Value(l))];

    // A task that completes when the controller next enters Break (one-shot), as DebuggerTests waits for one.
    private static Task<StoppedInfo> NextStop(DebugController dbg)
    {
        var tcs = new TaskCompletionSource<StoppedInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(StoppedInfo info) { dbg.Stopped -= Handler; tcs.TrySetResult(info); }
        dbg.Stopped += Handler;
        return tcs.Task;
    }

    // Headless is the compiled program: the transcript is what vb6.exe's EXE logged.
    [Fact]
    public async Task Headless_an_assertion_is_skipped_and_its_argument_not_evaluated()
    {
        await Run("""
            Private Function G() As Boolean
                Debug.Print "G called"
                G = False
            End Function
            Private Sub Rows()
                On Error Resume Next
                Debug.Print "before"
                Debug.Assert False
                Debug.Print "after False"
                Debug.Assert G()
                Debug.Print "after G()"
                Debug.Assert "abc"
                Debug.Print "after ""abc"", err " & Err.Number
                Err.Clear
                Debug.Assert 1 / 0
                Debug.Print "after 1 / 0, err " & Err.Number
            End Sub

            Rows

            """);

        AssertDebugLog(Lines("""
            before
            after False
            after G()
            after "abc", err 0
            after 1 / 0, err 0
            """));
    }

    [Theory]
    [InlineData("Debug.Assert")]
    [InlineData("Debug.Assert True, False")]
    public async Task No_condition_or_two_do_not_compile(string statement)
    {
        var run = () => Run("Sub S()\n    " + statement + "\nEnd Sub\nS\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Syntax error*");
    }

    [Theory]
    [InlineData("False")]
    [InlineData("0")]
    [InlineData("Empty")]
    [InlineData("Null")]
    [InlineData("\"False\"")]
    public async Task Under_the_debugger_a_condition_that_does_not_hold_breaks_at_the_assertion(string condition)
    {
        var (vb, dbg) = NewDebuggable($"Debug.Print \"before\"\nDebug.Assert {condition}\nDebug.Print \"after\"\n", "M");

        var stop = NextStop(dbg);
        var run = vb.Execute();
        var info = await stop.Guarded();

        info.Reason.Should().Be(StopReason.Assertion);
        info.Line.Should().Be(2);
        debug.Should().HaveCount(1);   // "before" only
        dbg.Continue();
        await run.Guarded();
        AssertDebugLog(["before", "after"]);
    }

    [Theory]
    [InlineData("True")]
    [InlineData("5")]
    [InlineData("-1")]
    [InlineData("\"True\"")]
    public async Task Under_the_debugger_a_condition_that_holds_goes_on(string condition)
    {
        var (vb, dbg) = NewDebuggable($"Debug.Print \"before\"\nDebug.Assert {condition}\nDebug.Print \"after\"\n", "M");

        await vb.Execute().Guarded();

        dbg.State.Should().Be(DebugState.Running);   // never entered Break
        AssertDebugLog(["before", "after"]);
    }

    [Fact]
    public async Task Under_the_debugger_the_condition_is_evaluated_and_its_errors_are_trappable()
    {
        var (vb, dbg) = NewDebuggable("""
            Private Function F() As Boolean
                Debug.Print "F called"
                F = True
            End Function
            Private Sub Rows()
                On Error Resume Next
                Debug.Assert F()
                Debug.Assert "abc"
                Debug.Print "after ""abc"", err " & Err.Number
                Err.Clear
                Debug.Assert 1 / 0
                Debug.Print "after 1 / 0, err " & Err.Number
            End Sub

            Rows

            """, "M");

        await vb.Execute().Guarded();

        dbg.State.Should().Be(DebugState.Running);
        AssertDebugLog(["F called", "after \"abc\", err 13", "after 1 / 0, err 11"]);
    }

    [Fact]
    public async Task Under_the_debugger_On_Error_Resume_Next_does_not_skip_the_break()
    {
        var (vb, dbg) = NewDebuggable("""
            Private Function G() As Boolean
                Debug.Print "G called"
                G = False
            End Function
            Private Sub Rows()
                On Error Resume Next
                Debug.Print "before"
                Debug.Assert G()
                Debug.Print "after, err " & Err.Number
            End Sub

            Rows

            """, "M");

        var stop = NextStop(dbg);
        var run = vb.Execute();
        var info = await stop.Guarded();

        info.Reason.Should().Be(StopReason.Assertion);
        AssertDebugLog(["before", "G called"]);
        dbg.Continue();
        await run.Guarded();
        AssertDebugLog(["before", "G called", "after, err 0"]);
    }
}
