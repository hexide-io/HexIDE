using System;
using System.Reflection;
using Avalonia.Threading;

namespace HexIDE.Runtime.Integration.Tests.Views;

// Drive the wired handler without waiting for dispatcher scheduling or changing the control.
internal static class RepeatTimerDriver
{
    public static DispatcherTimer For(object control) => (DispatcherTimer)control.GetType()
        .GetField("repeat", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;

    public static void Tick(DispatcherTimer timer) => ((EventHandler)typeof(DispatcherTimer)
        .GetField("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(timer)!).Invoke(timer, EventArgs.Empty);
}
