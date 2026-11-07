using System.Runtime.ExceptionServices;
using System.Threading;

namespace HexIDE.Runtime;

/// <summary>
/// Starts the UI on a thread of its own, with a stack sized for the VB code that runs on it (#3228).
/// </summary>
/// <remarks>
/// <para>VB code executes on the UI thread, so that thread's stack is how deep a program can recurse or nest
/// before Error 28. The process's first thread is the wrong size for that, and is set by the launcher rather
/// than by HexIDE: 1.5 MiB on Windows, the apphost's PE header, and <c>ulimit -s</c> on Linux, usually
/// 8 MiB. Under <c>ulimit -s unlimited</c> it is so large that the Error 28 guard never trips, and a runaway
/// recursion takes memory instead.</para>
/// <para>macOS is left on its first thread, because AppKit runs its UI there only. It gets 8 MiB.</para>
/// </remarks>
public static class UiThread
{
    /// <summary>
    /// The UI thread's stack. Jen chose 64 MiB on #3228, where the measurements are. A recursive VB
    /// Function reaches about 1,490 levels on it in a Debug build, against 31 on Windows' first thread. It
    /// also holds vb6.exe's 7,227 nested If blocks. It is address space: memory is used only as deep code
    /// touches it.
    /// </summary>
    public const int StackBytes = 64 * 1024 * 1024;

    /// <summary>
    /// Runs <paramref name="start"/> on a new thread with a <see cref="StackBytes"/> stack, waits for it,
    /// and returns what it returns. An exception it throws is rethrown here, with its own stack trace. On
    /// Windows the thread is STA, as <c>[STAThread]</c> made the first thread: OLE drag-and-drop, the
    /// clipboard and OCX hosting need it. On macOS <paramref name="start"/> runs on the calling thread.
    /// </summary>
    public static int Run(Func<int> start) =>
        OperatingSystem.IsMacOS() ? start() : RunOnNewThread(start, StackBytes);

    internal static int RunOnNewThread(Func<int> start, int stackBytes)
    {
        var result = 0;
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try { result = start(); }
            catch (Exception e) { error = ExceptionDispatchInfo.Capture(e); }
        }, stackBytes) { Name = "HexIDE UI" };
        if (OperatingSystem.IsWindows())
            thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
        return result;
    }
}
