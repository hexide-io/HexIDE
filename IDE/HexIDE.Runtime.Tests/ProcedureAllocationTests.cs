using HexIDE.IDE;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

[Collection<ErrorUnwindCollection>]
public class ProcedureAllocationTests : BaseVBTestFixture
{
    [Fact]
    public async Task FiveThousandReturningCalls_StayWithinThePerCallAllocationBudget()
    {
        // #4919: allocation, not elapsed time, pins the cost of the ordinary recursive call path.
        // Measured budgets: scripts/probes/4919/README.md.
        Exception? failure = null;
        long bytes = 0;
        // Windows Debug can reach the stack guard before 5,000 calls on the UI's 64 MiB (#4919).
        // This measures allocation for a fixed depth, independently of the production stack limit.
        await Task.Run(() => UiThread.RunOnNewThread(() =>
        {
            try
            {
                var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv,
                    "Up 1\nSub Up(ByVal n As Long)\nIf n >= 5000 Then Exit Sub\nUp n + 1\nEnd Sub\n");
                void Execute()
                {
                    var run = vb.Execute();
                    run.IsCompleted.Should().BeTrue("the allocation must stay on the measured thread");
                    run.GetAwaiter().GetResult();
                }
                Execute();
                Execute();
                Execute();
                var before = GC.GetAllocatedBytesForCurrentThread();
                Execute();
                bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            catch (Exception error) { failure = error; }
            return 0;
        }, 128 * 1024 * 1024), TestContext.Current.CancellationToken);
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure);
#if DEBUG
        const double budget = 18_400;
#else
        const double budget = 5_000;
#endif
        (bytes / 5000.0).Should().BeLessThan(budget, "#4919 caches syntax instead of rebuilding it for every call");
    }
}
