using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace HexIDE.IDE;

internal interface IWindowRaiser
{
    void Raise(Window window);
}

/// <summary>Restacks a program's windows on Continue without delivering form activation events (#4667).</summary>
internal sealed partial class WindowRaiser : IWindowRaiser
{
    public void Raise(Window window)
    {
        var handle = window.TryGetPlatformHandle();
        if (handle is null || handle.Handle == 0) return;
        if (OperatingSystem.IsWindows() && handle.HandleDescriptor == "HWND")
            SetWindowPos(handle.Handle, 0, 0, 0, 0, 0, 0x0002 | 0x0001 | 0x0010); // NOMOVE | NOSIZE | NOACTIVATE
        else if (OperatingSystem.IsMacOS() && handle.HandleDescriptor == "NSWindow")
            SendMessage(handle.Handle, RegisterSelector("orderFront:"), 0);
        else if (OperatingSystem.IsLinux() && handle.HandleDescriptor == "XID")
        {
            var display = XOpenDisplay(0);
            if (display == 0) return;
            try
            {
                // The window manager may refuse a client's restack request.
                XRaiseWindow(display, handle.Handle);
                XFlush(display);
            }
            finally { XCloseDisplay(display); }
        }
        // Wayland has no client-requested raise. Unknown backends retain their existing behaviour.
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint RegisterSelector(string name);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial void SendMessage(nint window, nint selector, nint sender);

    [LibraryImport("libX11.so.6")]
    private static partial nint XOpenDisplay(nint name);

    [LibraryImport("libX11.so.6")]
    private static partial int XRaiseWindow(nint display, nint window);

    [LibraryImport("libX11.so.6")]
    private static partial int XFlush(nint display);

    [LibraryImport("libX11.so.6")]
    private static partial int XCloseDisplay(nint display);
}
