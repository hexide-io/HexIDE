using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// An error raised deep in a recursion reaches its handler in time linear in the depth (#3247).
/// </summary>
/// <remarks>
/// Every await between a raise and its handler rethrows the error, and a rethrow appends to the .NET stack trace the
/// error already carries. Kept whole, that trace grew by every procedure the error passed, so the error raised N
/// calls deep cost N² to surface: 18 s at 1,200 calls in a Debug build, and minutes for a runaway recursion on
/// #3228's 64 MiB UI thread. Each procedure now throws it afresh. These tests check that by structure, the length
/// of the trace the error arrives with, and not by a timing.
/// </remarks>
public class ErrorUnwindTests : BaseVBTestFixture
{
    [Fact]
    public async Task AnErrorRaisedDeep_ArrivesWithNoMoreTraceThanOneRaisedShallow()
    {
        var shallow = await TraceLinesOfAnErrorRaised(callsDeep: 20);
        var deep = await TraceLinesOfAnErrorRaised(callsDeep: 400);

        deep.Should().BeLessThanOrEqualTo(shallow,
            "the error carries only the trace of the procedure it last left, however deep it was raised");
    }

    [Fact]
    public async Task AnErrorRaisedDeep_ReachesItsHandlerAsOneRaisedShallowDoes()
    {
        // Err and Erl travel with the error object, which each procedure throws on unchanged. Whatever the handler
        // reads 3 calls down, it reads the same 400 calls down.
        var shallow = await HandlerReadsOfAnErrorRaised(callsDeep: 3);
        var deep = await HandlerReadsOfAnErrorRaised(callsDeep: 400);

        deep.Should().Equal(shallow);
        shallow.Should().HaveCount(3);
        shallow[0].Should().Be(new Vb6Value(5L).ToString());
    }

    private async Task<List<string>> HandlerReadsOfAnErrorRaised(int callsDeep)
    {
        debug.Clear();
        await RunOnSizedStack(
            "On Error GoTo H\n" +
            "Call Up(0)\n" +
            "Exit Sub\n" +
            "H:\n" +
            "Debug.Print Err.Number\n" +
            "Debug.Print Err.Description\n" +
            "Debug.Print Erl\n" +
            "Sub Up(n)\n" +
            $"10  If n >= {callsDeep} Then Err.Raise 5\n" +
            "20  Up n + 1\n" +
            "End Sub\n");
        return debug.Select(value => value.ToString()).ToList();
    }

    [Fact]
    public async Task AnErrorRaisedDeep_IsTrappedByTheNearestHandler()
    {
        // On Error at each level still decides: the handler 200 calls up catches it, and the run goes on.
        await RunOnSizedStack(
            "Call Up(0)\n" +
            "Debug.Print \"after\"\n" +
            "Sub Up(n)\n" +
            "  If n = 200 Then On Error Resume Next\n" +
            "  If n >= 400 Then Err.Raise 5\n" +
            "  Up n + 1\n" +
            "  If n = 200 Then Debug.Print Err.Number\n" +
            "End Sub\n");
        AssertDebugLog([new Vb6Value(5L), "after"]);
    }

    private async Task<int> TraceLinesOfAnErrorRaised(int callsDeep)
    {
        VBRunTimeException? raised = null;
        try
        {
            await RunOnSizedStack(
                "Call Up(0)\n" +
                "Sub Up(n)\n" +
                $"  If n >= {callsDeep} Then Err.Raise 5\n" +
                "  Up n + 1\n" +
                "End Sub\n");
        }
        catch (VBRunTimeException error)
        {
            raised = error;
        }
        raised.Should().NotBeNull("an unhandled error ends the run");
        return raised!.StackTrace!.Split('\n').Length;
    }

    /// <summary>Runs <paramref name="code"/> on a thread with the UI thread's stack, so 400 calls fit. A run with no
    /// debugger attached never yields, so it all happens on that thread.</summary>
    private async Task RunOnSizedStack(string code)
    {
        Exception? failure = null;
        await Task.Run(() => UiThread.RunOnNewThread(() =>
        {
            try
            {
                var run = Run(code);
                if (!run.IsCompleted)
                    throw new InvalidOperationException("The run yielded, so it left the thread under test.");
                run.GetAwaiter().GetResult();
            }
            catch (Exception error)
            {
                failure = error;
            }
            return 0;
        }, UiThread.StackBytes), TestContext.Current.CancellationToken);
        // Rethrown with its trace kept: the first test measures that trace.
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure);
    }
}
