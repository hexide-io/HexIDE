#if DEBUG
using System;
using System.Threading;
using Avalonia.Threading;

namespace HexIDE.Infrastructure;

/// <summary>
/// Crashes a Debug build on purpose, so that what a crash leaves behind can be checked on the real process.
/// Compiled out of Release.
/// </summary>
/// <remarks>
/// <para>
/// <c>HEXIDE_DEBUG_CRASH=thread</c> throws on a background thread three seconds after startup, and
/// <c>HEXIDE_DEBUG_CRASH=ui</c> posts a throw to the UI thread. Either ends the process with the exception at the
/// end of the IDE log (#643). Nothing else reads the variable, and any other value does nothing.
/// </para>
/// <para>
/// <c>HEXIDE_DEBUG_CRASH=timer</c> throws from a <see cref="DispatcherTimer"/> callback instead, and is kept
/// because it does NOT crash: measured on Avalonia 12.0.4, the exception reaches neither
/// <c>Dispatcher.UIThread.UnhandledException</c> nor <c>AppDomain.UnhandledException</c>, and the IDE carries on
/// with nothing logged. The warning written before the throw is what shows the timer did fire.
/// </para>
/// </remarks>
internal static class DebugCrashHook
{
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(3);

    public static void ArmFromEnvironment()
    {
        switch (Environment.GetEnvironmentVariable("HEXIDE_DEBUG_CRASH"))
        {
            case "thread":
                new Thread(() =>
                {
                    Thread.Sleep(Delay);
                    throw new InvalidOperationException("HEXIDE_DEBUG_CRASH=thread: a deliberate crash on a background thread");
                }) { IsBackground = true, Name = "HEXIDE_DEBUG_CRASH" }.Start();
                break;
            case "ui":
                new Thread(() =>
                {
                    Thread.Sleep(Delay);
                    Dispatcher.UIThread.Post(() =>
                        throw new InvalidOperationException("HEXIDE_DEBUG_CRASH=ui: a deliberate crash on the UI thread"));
                }) { IsBackground = true, Name = "HEXIDE_DEBUG_CRASH" }.Start();
                break;
            case "timer":
                DispatcherTimer.RunOnce(
                    () =>
                    {
                        Serilog.Log.Warning("HEXIDE_DEBUG_CRASH=timer: the timer fired and is about to throw");
                        throw new InvalidOperationException("HEXIDE_DEBUG_CRASH=timer: a deliberate crash in a UI-thread timer");
                    },
                    Delay);
                break;
        }
    }
}
#endif
