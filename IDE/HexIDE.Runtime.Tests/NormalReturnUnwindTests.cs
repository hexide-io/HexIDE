using System.Diagnostics;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

[Collection<ErrorUnwindCollection>]
public class NormalReturnUnwindTests : BaseVBTestFixture
{
    // HexIDE's call-depth cap and the Work/Deep probe are recorded in #5152, not a VB6 recursion limit.
    [Theory(Timeout = 300_000)]
    [InlineData(1000, true, false)]
    [InlineData(4900, false, true)]
    [InlineData(4900, true, false)]
    public async Task AWalkThatReturns_LeavesStackForTheNextWalk(int depth, bool stops, bool everyStatement)
        => await RunWalk(depth, stops, everyStatement);

    // A synchronous Work 1000 can hit the native stack guard before returning (#5152 Windows review).
    [Fact]
    public async Task AWalkWithoutSuspension_LeavesStackForTheNextWalk()
        => await RunWalk(30, stops: false, everyStatement: false);

    private async Task RunWalk(int depth, bool stops, bool everyStatement)
    {
        await OnStack(() =>
        {
            var dbg = new DebugController();
            var returningFrames = -1;
            var walkPosts = -1;
            var library = new StackProbeLibrary(debug, () =>
            {
                walkPosts = ((Pump)SynchronizationContext.Current!).Posts;
                returningFrames = new StackTrace().GetFrames().Count(frame =>
                    frame.GetMethod()?.DeclaringType?.Name.StartsWith("<RunProcedure>", StringComparison.Ordinal) == true);
                // The Property Get end is breakpoint-only; print its result before another statement gate can yield.
                // Then make Deep independent of platform stack-frame sizes and the time-slice clock.
                dbg.TimeSliceMs = -1;
            });
            var vb = new BasicInterpreter(library, context, rootEnv,
                "Dim maxDepth As Long\n" +
                $"Debug.Print Walk({depth})\n" +
                "Deep 0\nDebug.Print maxDepth\n" +
                "Sub Work(ByVal n As Long)\n" +
                "  If n > 0 Then Work n - 1\n" +
                "End Sub\n" +
                "Sub Deep(ByVal d As Long)\n" +
                "  On Error GoTo Done\n" +
                "  maxDepth = d\n" +
                "  Deep d + 1\n" +
                "Done:\nEnd Sub\n" +
                "Property Get Walk(ByVal n As Long) As Long\n  Work n\nEnd Property\n", "M") { DebugController = dbg };
            // Stops already suspend every level; keep the clock out of the stack sample.
            dbg.TimeSliceMs = everyStatement ? -1 : null;
            var pump = (Pump)SynchronizationContext.Current!;
            var hits = 0;
            if (stops)
            {
                dbg.SetBreakpoints("M", [6]);
                dbg.Stopped += _ =>
                {
                    hits++;
                    pump.Post(_ => dbg.Continue(), null);
                };
            }
            var run = vb.Execute();
            pump.Drain();
            run.IsCompleted.Should().BeTrue("the walk must finish on the pumping thread");
            run.GetAwaiter().GetResult();
            hits.Should().Be(stops ? depth + 1 : 0);
            if (!stops && !everyStatement)
                walkPosts.Should().Be(0, "the synchronous walk must not yield on its way down or back up");
            TestContext.Current.TestOutputHelper?.WriteLine($"Work {depth}, stops={stops}, everyStatement={everyStatement}: " +
                $"{returningFrames} RunProcedure frames at return, Deep reached {debug[1].Value}");
            returningFrames.Should().BeInRange(0, BasicInterpreter.UnwindYieldEvery + 1,
                "only one unwind interval plus its yielding boundary frame may remain");
            debug.Should().HaveCount(2);
            debug[1].Should().Be(new Vb6Value(4999L));
        });
    }

    [Fact]
    public async Task AReturnAcrossSuspendedCallers_PostsAtTheUnwindBoundary()
    {
        await OnStack(() =>
        {
            var (vb, dbg) = NewDebuggable(
                "Work 32\nDebug.Print 1\n" +
                "Sub Work(ByVal n As Long)\n  If n > 0 Then Work n - 1\nEnd Sub\n", "M");
            dbg.TimeSliceMs = null;
            dbg.SetBreakpoints("M", [4]);
            dbg.SetBreakpointConditions("M", new Dictionary<int, string> { [4] = "n = 0" });
            var pump = (Pump)SynchronizationContext.Current!;
            dbg.Stopped += _ => pump.Post(_ => dbg.Continue(), null);
            var run = vb.Execute();
            pump.Drain();
            run.IsCompleted.Should().BeTrue("the walk must finish on the pumping thread");
            run.GetAwaiter().GetResult();
            AssertDebugLog([1]);
            // Continue itself and the break-gate continuation account for two posts; the return needs a third.
            pump.Posts.Should().Be(3);
        });
    }

    [Fact]
    public async Task CallsAtAnUnwindBoundary_WithoutSuspension_PostNothing()
    {
        await OnStack(() =>
        {
            // Loop is frame 32; Leaf returns with 32 frames below it, the normal-unwind yield boundary.
            var (vb, dbg) = NewDebuggable(
                "Work 30\n" +
                "Sub Work(ByVal n As Long)\n" +
                "  If n > 0 Then\n    Work n - 1\n  Else\n    LoopCalls\n  End If\nEnd Sub\n" +
                "Sub LoopCalls()\n  Dim i As Long\n  For i = 1 To 1000\n    Leaf\n  Next\nEnd Sub\n" +
                "Function Leaf() As Long\n  Leaf = 1\nEnd Function\n", "M");
            dbg.TimeSliceMs = null;
            var pump = (Pump)SynchronizationContext.Current!;
            var run = vb.Execute();
            run.IsCompletedSuccessfully.Should().BeTrue();
            run.GetAwaiter().GetResult();
            pump.Posts.Should().Be(0);
        });
    }

    private sealed class StackProbeLibrary(List<Vb6Value> log, Action sample) : MockStdLib(log), IBasicStandardLibrary
    {
        private bool sampled;
        void IBasicStandardLibrary.DebugPrint(Vb6Value value)
        {
            if (!sampled)
            {
                sampled = true;
                sample();
            }
            DebugPrint(value);
        }
    }

    private static async Task OnStack(Action body)
    {
        Exception? failure = null;
        await Task.Run(() =>
        {
            var thread = new Thread(() =>
            {
                var previous = SynchronizationContext.Current;
                SynchronizationContext.SetSynchronizationContext(new Pump());
                try { body(); }
                catch (Exception error) { failure = error; }
                finally { SynchronizationContext.SetSynchronizationContext(previous); }
            }, 8 * 1024 * 1024);
            thread.Start();
            thread.Join();
        }, TestContext.Current.CancellationToken);
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure);
    }

    private sealed class Pump : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> pending = new();
        public int Posts { get; private set; }
        public override void Post(SendOrPostCallback callback, object? state)
        {
            Posts++;
            pending.Enqueue((callback, state));
        }
        public void Drain()
        {
            while (pending.TryDequeue(out var work))
                work.Callback(work.State);
        }
    }
}
