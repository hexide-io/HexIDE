using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace HexIDE.IDE;

internal static partial class WindowFrameMeasurement
{
    internal static (Size Outer, PixelRect OuterBounds, PixelRect DrawnBounds)? Measure(Window window)
    {
        if (!OperatingSystem.IsWindows() || window.TryGetPlatformHandle() is not { Handle: var handle }
            || window.FrameSize is not { } frame)
            return null;
        try
        {
            if (GetWindowRect(handle, out var outer)
                && DwmGetWindowAttribute(handle, 9, out var drawn, Marshal.SizeOf<NativeRect>()) == 0)
                return (frame, outer.ToPixelRect(), drawn.ToPixelRect());
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly PixelRect ToPixelRect() => new(Left, Top, Right - Left, Bottom - Top);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint handle, out NativeRect rect);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(nint handle, int attribute, out NativeRect rect, int size);
}
