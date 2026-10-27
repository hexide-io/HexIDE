using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HexIDE.Runtime;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

public class ActivationSlotTests
{
    [AvaloniaFact]
    public async Task DoEvents_NestedTimerCallReclaimsBothActivations()
    {
        var context = new ModuleExecutionContext();
        var dbg = new DebugController();
        var vb = new BasicInterpreter(Substitute.For<IBasicStandardLibrary>(), context, new ExecutionEnvironment(),
            "Work\nDebug.Print 0\nSub Work()\nDim local As Long\nDoEvents\nEnd Sub\nSub Timer1_Timer()\nDim other As Long\nEnd Sub\n")
            { DebugController = dbg };
        var pump = new EventPump();
        vb.Events = pump;
        dbg.SetBreakpoints("Module1", [5]);
        var stopped = new TaskCompletionSource<StoppedInfo>();
        dbg.Stopped += info => stopped.TrySetResult(info);
        Task? work = null;
        pump.Raise("Click", () => work = vb.Execute());
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        IDebugFrame stale = vb.ActivationStack.Last();
        int inside = context.State.memory.Count;
        Task? timer = null;
        pump.Raise("Timer1_Timer", () => timer = vb.ExecuteSub("Timer1_Timer"), EventCoalescing.Timer);
        dbg.Continue();
        await work!.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        timer.Should().NotBeNull();
        await timer!.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        context.State.memory.Count.Should().Be(inside - 1);
        stale.GetLocals().Locals.Should().NotContain(n => n.Name == "local");
    }
}
