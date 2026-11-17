using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace HexIDE.Automation;

/// <summary>Activation may switch our own windows, but must not pull foreground from another application (#4223).</summary>
internal static partial class WindowActivation
{
    internal static bool OwnsForeground(Window target)
    {
        // Headless windows have no HWND and never request native activation.
        if (!OperatingSystem.IsWindows() || target.TryGetPlatformHandle()?.HandleDescriptor != "HWND")
            return true;
        var foreground = GetForegroundWindow();
        return foreground != 0 && GetWindowThreadProcessId(foreground, out var pid) != 0
            && pid == (uint)Environment.ProcessId;
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);
}
