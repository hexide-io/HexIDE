using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Avalonia.Threading;

namespace HexIDE.Runtime.Integration.Tests.Views;

// Own the repeat handler before input, leaving the product timer's state and intervals intact (#3460).
internal static class RepeatTimerDriver
{
    private static readonly ConditionalWeakTable<DispatcherTimer, EventHandler> handlers = new();

    // Let overdue dispatcher timers run, preserving the caller's execution context across the pump.
    public static void Stall(TimeSpan delay)
    {
        var context = ExecutionContext.Capture()!;
        try
        {
            Thread.Sleep(delay);
            var frame = new DispatcherFrame();
            using var exit = DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(30));
            Dispatcher.UIThread.PushFrame(frame);
        }
        finally { ExecutionContext.Restore(context); }
    }

    public static DispatcherTimer For(object control)
    {
        var timer = (DispatcherTimer)control.GetType()
            .GetField("repeat", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
        handlers.GetValue(timer, t =>
        {
            var handler = (EventHandler)typeof(DispatcherTimer)
                .GetField("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(t)!;
            t.Tick -= handler;
            return handler;
        });
        return timer;
    }

    public static void Tick(DispatcherTimer timer) => handlers.GetValue(timer,
        _ => throw new InvalidOperationException("Take over the repeat timer before driving it."))
        .Invoke(timer, EventArgs.Empty);
}
