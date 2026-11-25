using HexIDE.IDE;
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
[Collection<ErrorUnwindCollection>]
public class ErrorUnwindTests : BaseVBTestFixture
{
    [Fact]
    public void ARunawaysError28Unwind_AllocatesAsMuchPerLevelAsAShallowOne()
    {
        // #4775: deep recursion costs more per level because each garbage collection costs more on a deep stack,
        // not because a level allocates more the deeper it is. This guards the second half. A runaway raises Error 28
        // wherever this platform and build stop it: the stack probe in a Windows Debug build, MaxCallDepth elsewhere.
        // So the depth is read rather than assumed, and the runaway's walk and unwind are compared, per level, with an
        // Error 28 raised at a fifth of that depth. It passed on main as it was: it guards, and proves no fix.
        AllocatedUntilError28(raiseAt: 1000); // Warm the parser and JIT before measuring.
        AllocatedUntilError28(raiseAt: 1000);
        var (deepest, deepBytes) = AllocatedUntilError28(raiseAt: null);
        deepest.Should().BeGreaterThan(2000, "a 64 MiB thread allows a deep walk on every platform measured");
        var (shallow, shallowBytes) = AllocatedUntilError28(raiseAt: deepest / 5);

        double deepPerLevel = (double)deepBytes / deepest, shallowPerLevel = (double)shallowBytes / shallow;
        deepPerLevel.Should().BeLessThan(shallowPerLevel * 1.2,
            $"a runaway stopped at {deepest} levels allocated {deepPerLevel:F0} bytes a level, and an Error 28 at {shallow} " +
            $"allocated {shallowPerLevel:F0}");
    }

    private static (long Depth, long Bytes) AllocatedUntilError28(long? raiseAt)
    {
        (long Depth, long Bytes) result = default;
        UiThread.RunOnNewThread(() =>
        {
            var log = new List<Vb6Value>();
            var code = "Dim deepest As Long\nOn Error Resume Next\nUp 1\nDebug.Print Err.Number\nDebug.Print deepest\n" +
                "Sub Up(ByVal n As Long)\ndeepest = n\n" +
                (raiseAt is { } at ? $"If n = {at} Then Err.Raise 28\n" : "") +
                "Up n + 1\nEnd Sub\n";
            var vb = new BasicInterpreter(new MockStdLib(log), new ModuleExecutionContext(), new ExecutionEnvironment(), code);
            var before = GC.GetAllocatedBytesForCurrentThread();
            var run = vb.Execute();
            run.IsCompleted.Should().BeTrue("the measured walk must stay on the 64 MiB thread");
            run.GetAwaiter().GetResult();
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            // A runaway's Error 28 is trappable (#3250), whichever guard raised it.
            log.Should().HaveCount(2);
            log[0].Should().Be(new Vb6Value(28L));
            var depth = Convert.ToInt64(log[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (raiseAt is { } raised)
                depth.Should().Be(raised);
            result = (depth, bytes);
            return 0;
        }, UiThread.StackBytes);
        return result;
    }

    [Fact]
    public async Task AnErrorRaisedDeep_ArrivesWithNoMoreTraceThanOneRaisedShallow()
    {
        var shallow = await TraceLinesOfAnErrorRaised(callsDeep: 20);
        var deep = await TraceLinesOfAnErrorRaised(callsDeep: 400);

        deep.Should().BeLessThanOrEqualTo(shallow,
            "the error carries only the trace of the procedure it last left, however deep it was raised");
    }

    [Fact]
    public async Task AnEndReachedDeep_UnwindsWithNoMoreTraceThanOneReachedShallow()
    {
        // End and a debugger's Stop unwind every frame with one signal, which the run's entry swallows. A Stop 113,000
        // calls deep grew memory by 5 GB in 29 seconds before it was thrown afresh too (#3250).
        var shallow = await TraceLinesOfAnEndReached(callsDeep: 20);
        var deep = await TraceLinesOfAnEndReached(callsDeep: 400);

        deep.Should().BeLessThanOrEqualTo(shallow,
            "the signal carries only the trace of the procedure it last left, however deep End was reached");
    }

    [Fact(Timeout = 300_000)]
    public async Task ARunawayOnTheUiThreadsStack_UnderATimeSlicingDebugger_ReachesItsHandler()
    {
        // The time slice leaves the frames it yielded across waiting on their callees, and on a 64 MiB stack .NET runs
        // their continuations inline inside one another as the error unwinds, each throw dearer than the last: 138 s
        // to reach the handler from #3250's cap, where a 1.5 MiB thread took 0.8 s. A yield every few procedures on the
        // way down keeps that nesting shallow: about 2.5 s. Five minutes bounds a hang, not speed; raising
        // the old one-minute bound and isolating this class mitigate loaded-run timeouts (#3821).
        var failure = default(Exception);
        await Task.Run(() => UiThread.RunOnNewThread(() =>
        {
            try
            {
                var (vb, dbg) = NewDebuggable(
                    "Main2\n" +
                    "Sub Main2()\n" +
                    "  On Error GoTo H\n" +
                    "  Up 1\n" +
                    "  Exit Sub\n" +
                    "H:\n" +
                    "  Debug.Print Err.Number\n" +
                    "End Sub\n" +
                    "Sub Up(ByVal n As Long)\n" +
                    "  Up n + 1\n" +
                    "End Sub\n", "M");
                dbg.TimeSliceMs = 15;
                // As a made EXE and the IDE have one, so each frame asks whether the error halts there, and the answer the
                // first frame found is reused rather than scanned for again.
                dbg.HaltHost = _ => Task.FromResult(false);
                var previous = SynchronizationContext.Current;
                var pump = new Pump();
                SynchronizationContext.SetSynchronizationContext(pump);
                try
                {
                    var run = vb.Execute();
                    pump.Drain();
                    run.IsCompletedSuccessfully.Should().BeTrue(
                        "the runaway must finish unwinding on the UI thread and reach its error handler");
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(previous);
                }
            }
            catch (Exception error)
            {
                failure = error;
            }
            return 0;
        }, UiThread.StackBytes), TestContext.Current.CancellationToken);
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure);
        AssertDebugLog([new Vb6Value(28L)]);
    }

    private async Task<int> TraceLinesOfAnEndReached(int callsDeep)
    {
        HexIDE.Runtime.Debugging.StopExecutionSignal? last = null;
        var thread = 0;
        EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> watch = (_, e) =>
        {
            if (e.Exception is HexIDE.Runtime.Debugging.StopExecutionSignal signal && Environment.CurrentManagedThreadId == thread)
                last = signal;
        };
        AppDomain.CurrentDomain.FirstChanceException += watch;
        try
        {
            await RunOnSizedStack(
                "Call Up(0)\n" +
                "Sub Up(n)\n" +
                $"  If n >= {callsDeep} Then End\n" +
                "  Up n + 1\n" +
                "End Sub\n",
                onThread: () => thread = Environment.CurrentManagedThreadId);
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= watch;
        }
        last.Should().NotBeNull("End unwinds with the signal");
        return last!.StackTrace!.Split('\n').Length;
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

    [Fact]
    public void UnboundedRecursion_UnderADebuggerThatYieldsEveryStatement_RaisesError28AtTheCallDepthCap()
    {
        // A yield releases the native frames, so the stack probe never trips under F5 or in a made EXE. The count
        // must (#3250). This runs on the test's own small stack, so a 28 can come from nothing else.
        var (vb, dbg) = NewDebuggable(
            "On Error Resume Next\n" +
            "Dim deepest\n" +
            "Up 1, deepest\n" +
            "Debug.Print Err.Number\n" +
            "Debug.Print deepest\n" +
            "Sub Up(n, deepest)\n" +
            "  deepest = n\n" +
            "  Up n + 1, deepest\n" +
            "End Sub\n", "M");
        dbg.TimeSliceMs = -1;
        var previous = SynchronizationContext.Current;
        var pump = new Pump();
        SynchronizationContext.SetSynchronizationContext(pump);
        try
        {
            var run = vb.Execute();
            pump.Drain();
            run.IsCompletedSuccessfully.Should().BeTrue();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
        // The statements at the top are no procedure activation, so Up's own frames fill the count: it reaches the cap
        // and the next call is refused.
        AssertDebugLog([new Vb6Value(28L), BasicInterpreter.MaxCallDepth]);
    }

    // The same for GoSub (#4726). Within one procedure each pending GoSub is a nested pass, and each pass throws what
    // leaves it afresh, as each procedure does. Without that, a 28 leaving 2,000 passes took 7.6 to 11.5 s to reach the
    // caller's handler in Debug, and 3.1 s with it, against 1.6 to 1.8 s for 1,999 passes returning normally (#4775
    // named that deep-stack cost, and #4919 cuts it). Unlike the procedure tests above, these read the longest trace the error carries on
    // its way, not the one it leaves the procedure with: RunProcedure throws afresh too, which hides the passes' trace.
    [Fact]
    public async Task AnErrorRaisedDeepInGoSub_CarriesNoMoreTraceOnItsWayThanOneRaisedShallow()
    {
        var shallow = await LongestTraceOnTheWay<VBRunTimeException>(GoSubsDeep(20, "Err.Raise 5"));
        var deep = await LongestTraceOnTheWay<VBRunTimeException>(GoSubsDeep(400, "Err.Raise 5"));

        deep.Should().BeLessThanOrEqualTo(shallow,
            "the error carries only the trace of the GoSub pass it last left, however deep it was raised");
    }

    [Fact]
    public async Task AnEndReachedDeepInGoSub_CarriesNoMoreTraceOnItsWayThanOneReachedShallow()
    {
        var shallow = await LongestTraceOnTheWay<HexIDE.Runtime.Debugging.StopExecutionSignal>(GoSubsDeep(20, "End"));
        var deep = await LongestTraceOnTheWay<HexIDE.Runtime.Debugging.StopExecutionSignal>(GoSubsDeep(400, "End"));

        deep.Should().BeLessThanOrEqualTo(shallow,
            "the signal carries only the trace of the GoSub pass it last left, however deep End was reached");
    }

    /// <summary>The most stack-trace lines a <typeparamref name="T"/> carried at any throw while <paramref name="code"/>
    /// ran, on the thread running it.</summary>
    private async Task<int> LongestTraceOnTheWay<T>(string code) where T : Exception
    {
        var longest = 0;
        var thread = 0;
        EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> watch = (_, e) =>
        {
            if (e.Exception is T && Environment.CurrentManagedThreadId == thread)
                longest = Math.Max(longest, e.Exception.StackTrace?.Split('\n').Length ?? 0);
        };
        AppDomain.CurrentDomain.FirstChanceException += watch;
        try
        {
            await RunOnSizedStack(code, onThread: () => thread = Environment.CurrentManagedThreadId);
        }
        catch (VBRunTimeException)
        {
            // The unhandled error ends the run; only its trace on the way matters here.
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= watch;
        }
        longest.Should().BePositive("the run throws on its way out");
        return longest;
    }

    [Fact]
    public void UnboundedGoSub_UnderADebuggerThatYieldsEveryStatement_RaisesError28AtTheGoSubCap()
    {
        // As for procedures above: a yield releases the native frames, so only the count can stop a runaway GoSub
        // under F5 or in a made EXE. The procedure's own handler traps that 28 (measured, G41).
        var (vb, dbg) = NewDebuggable(
            "Dim deepest\n" +
            "Up deepest\n" +
            "Debug.Print deepest\n" +
            "Sub Up(deepest)\n" +
            "  On Error GoTo E\n" +
            "  GoSub A\n" +
            "  Exit Sub\n" +
            "A:\n" +
            "  deepest = deepest + 1\n" +
            "  GoSub A\n" +
            "  Return\n" +
            "E:\n" +
            "  Debug.Print Err.Number\n" +
            "End Sub\n", "M");
        dbg.TimeSliceMs = -1;
        var previous = SynchronizationContext.Current;
        var pump = new Pump();
        SynchronizationContext.SetSynchronizationContext(pump);
        try
        {
            var run = vb.Execute();
            pump.Drain();
            run.IsCompletedSuccessfully.Should().BeTrue();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
        AssertDebugLog([new Vb6Value(28L), StatementExecutor.MaxGoSubDepth]);
    }

    [Fact(Timeout = 300_000)]
    public async Task ARunawayGoSubOnTheUiThreadsStack_UnderATimeSlicingDebugger_ReachesItsHandler()
    {
        // As ARunawayOnTheUiThreadsStack_UnderATimeSlicingDebugger_ReachesItsHandler, for nested GoSub passes: a 28 at
        // MaxGoSubDepth unwinds every pass to Main2's handler. The yield GoSub makes every few passes on the way down
        // matters here: measured in Debug, 1.9 s to the handler with it and 8.3 s without. Five minutes bounds a hang.
        var failure = default(Exception);
        await Task.Run(() => UiThread.RunOnNewThread(() =>
        {
            try
            {
                var (vb, dbg) = NewDebuggable(
                    "Main2\n" +
                    "Sub Main2()\n" +
                    "  On Error GoTo H\n" +
                    "  Up\n" +
                    "  Exit Sub\n" +
                    "H:\n" +
                    "  Debug.Print Err.Number\n" +
                    "End Sub\n" +
                    "Sub Up()\n" +
                    "  GoSub A\n" +
                    "  Exit Sub\n" +
                    "A:\n" +
                    "  GoSub A\n" +
                    "  Return\n" +
                    "End Sub\n", "M");
                dbg.TimeSliceMs = 15;
                dbg.HaltHost = _ => Task.FromResult(false);
                var previous = SynchronizationContext.Current;
                var pump = new Pump();
                SynchronizationContext.SetSynchronizationContext(pump);
                try
                {
                    var run = vb.Execute();
                    pump.Drain();
                    run.IsCompletedSuccessfully.Should().BeTrue(
                        "the runaway GoSub must finish unwinding on the UI thread and reach its error handler");
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(previous);
                }
            }
            catch (Exception error)
            {
                failure = error;
            }
            return 0;
        }, UiThread.StackBytes), TestContext.Current.CancellationToken);
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure);
        AssertDebugLog([new Vb6Value(28L)]);
    }

    private static string GoSubsDeep(int passes, string then) =>
        "Call Up\n" +
        "Sub Up()\n" +
        "  Dim n As Long\n" +
        "  GoSub A\n" +
        "  Exit Sub\n" +
        "A:\n" +
        "  n = n + 1\n" +
        $"  If n >= {passes} Then {then}\n" +
        "  GoSub A\n" +
        "  Return\n" +
        "End Sub\n";

    /// <summary>Runs posted continuations on the calling thread, until there are none.</summary>
    private sealed class Pump : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> pending = new();
        public override void Post(SendOrPostCallback callback, object? state) => pending.Enqueue((callback, state));
        public void Drain()
        {
            while (pending.TryDequeue(out var work))
                work.Callback(work.State);
        }
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
    private async Task RunOnSizedStack(string code, Action? onThread = null)
    {
        Exception? failure = null;
        await Task.Run(() => UiThread.RunOnNewThread(() =>
        {
            onThread?.Invoke();
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

// The runaway competes with thousands of interpreter tests in a full run (#3821).
[CollectionDefinition(DisableParallelization = true)]
public sealed class ErrorUnwindCollection;
