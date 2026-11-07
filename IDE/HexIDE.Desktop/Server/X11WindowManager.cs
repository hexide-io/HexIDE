using System.Runtime.InteropServices;

namespace HexIDE.Desktop.Server;

/// <summary>
/// Whether the X11 display HexIDE runs on has a window manager, read as EWMH defines it: a window manager puts
/// <c>_NET_SUPPORTING_WM_CHECK</c> on the root window. Without one nothing ever applies a minimize or a maximize, so
/// set_window_state's "the window manager has not confirmed" sent a caller to re-read and retry a change no amount of
/// waiting would bring (#2185).
/// </summary>
internal static partial class X11WindowManager
{
    private const string LibX11 = "libX11.so.6";

    // Xlib's predefined XA_WINDOW: the property holds a window id.
    private const nint WindowType = 33;

    /// <summary>
    /// True or false on an X11 display that could be read; null anywhere else, so the caller keeps saying only what it
    /// knows. Only the root window's property is read. Following it to the window it names, as EWMH's full check does,
    /// would raise BadWindow on a stale one, and that error reaches the process's handler.
    /// </summary>
    public static bool? IsRunning()
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
            return null;
        try
        {
            var display = XOpenDisplay(0);
            if (display == 0)
                return null;
            try
            {
                // Only if it exists: an atom no client ever interned has been set by no window manager.
                var check = XInternAtom(display, "_NET_SUPPORTING_WM_CHECK\0"u8.ToArray(), 1);
                if (check == 0)
                    return false;
                if (XGetWindowProperty(display, XDefaultRootWindow(display), check, 0, 1, 0, WindowType,
                        out _, out var format, out var count, out _, out var data) != 0)
                    return null;
                if (data != 0)
                    XFree(data);
                return format == 32 && count > 0;
            }
            finally { XCloseDisplay(display); }
        }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
    }

    [LibraryImport(LibX11)]
    private static partial nint XOpenDisplay(nint displayName);

    [LibraryImport(LibX11)]
    private static partial int XCloseDisplay(nint display);

    [LibraryImport(LibX11)]
    private static partial nint XDefaultRootWindow(nint display);

    // The name is passed as NUL-terminated bytes: Xlib wants a char*, not the UTF-16 a string would marshal to.
    [LibraryImport(LibX11)]
    private static partial nint XInternAtom(nint display, byte[] atomName, int onlyIfExists);

    // Every C long and Atom here is pointer-sized on the 64-bit Linux this runs on.
    [LibraryImport(LibX11)]
    private static partial int XGetWindowProperty(nint display, nint window, nint property, nint longOffset, nint longLength,
        int delete, nint requestedType, out nint actualType, out int actualFormat, out nint itemCount, out nint bytesAfter,
        out nint data);

    [LibraryImport(LibX11)]
    private static partial int XFree(nint data);
}
