using System.Globalization;
using System.Runtime.InteropServices;

namespace HexIDE.Runtime.Tests;

/// <summary>The stack the calling thread was given, read from the operating system (#3243).</summary>
internal static class ThreadStack
{
    /// <summary>The calling thread's whole reserved stack, on Windows, Linux or macOS; null elsewhere, or when the
    /// system will not say.</summary>
    public static ulong? ReservedBytes()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                GetCurrentThreadStackLimits(out var low, out var high);
                return high - low;
            }
            if (OperatingSystem.IsMacOS())
                return pthread_get_stacksize_np(pthread_self());
            if (!OperatingSystem.IsLinux())
                return null;
            var attr = new byte[256];
            if (pthread_getattr_np(pthread_self(), attr) != 0)
                return null;
            var read = pthread_attr_getstack(attr, out _, out var size);
            pthread_attr_destroy(attr);
            return read == 0 ? size : null;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>A size as a reader would write it: "16 MiB", "1.5 MiB", or "unknown".</summary>
    public static string Describe(ulong? bytes) =>
        bytes is { } b ? (b / (1024.0 * 1024)).ToString("0.##", CultureInfo.InvariantCulture) + " MiB" : "unknown";

    [DllImport("kernel32")] private static extern void GetCurrentThreadStackLimits(out nuint low, out nuint high);
    [DllImport("libc")] private static extern nint pthread_self();
    [DllImport("libc")] private static extern nuint pthread_get_stacksize_np(nint thread);
    [DllImport("libc")] private static extern int pthread_getattr_np(nint thread, [In, Out] byte[] attr);
    [DllImport("libc")] private static extern int pthread_attr_getstack([In, Out] byte[] attr, out nint address, out nuint size);
    [DllImport("libc")] private static extern int pthread_attr_destroy([In, Out] byte[] attr);
}
