using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace HexIDE.Automation;

/// <summary>Reads the actual HWND, and captures its visible frame from the Windows desktop (#1595).</summary>
internal static class NativeWindowFrame
{
    private static nint Handle(Window window) =>
        window.TryGetPlatformHandle() is { HandleDescriptor: "HWND" } handle ? handle.Handle : 0;

    internal static (uint? Style, uint? ExStyle, string? Note) Read(Window window)
    {
        var hwnd = Handle(window);
        if (!OperatingSystem.IsWindows() || hwnd == 0)
            return (null, null, "Native window styles are not reported: this window has no Windows HWND.");
        Marshal.SetLastPInvokeError(0);
        var style = GetWindowLong(hwnd, -16);
        var error = Marshal.GetLastPInvokeError();
        if (error != 0)
            return (null, null, $"GWL_STYLE could not be read (Win32 error {error}).");
        Marshal.SetLastPInvokeError(0);
        var exStyle = GetWindowLong(hwnd, -20);
        error = Marshal.GetLastPInvokeError();
        return error == 0 ? (style, exStyle, null)
            : (null, null, $"GWL_EXSTYLE could not be read (Win32 error {error}).");
    }

    internal static Bitmap? Capture(Window window, out PixelPoint windowOffset, out string? error)
    {
        windowOffset = default;
        error = "Frame capture requires a Windows HWND; this host/window has none. Use includeFrame false for a client-area render.";
        var hwnd = Handle(window);
        if (!OperatingSystem.IsWindows() || hwnd == 0)
            return null;
        if (!IsWindowVisible(hwnd) || IsIconic(hwnd))
        {
            error = "The window is hidden or minimized. Show or restore it, then retry includeFrame true.";
            return null;
        }
        var inputDesktop = OpenInputDesktop(0, false, 1);
        if (inputDesktop == 0)
        {
            error = "The Windows input desktop is inaccessible. Unlock/reconnect the desktop and retry, or use includeFrame false.";
            return null;
        }
        CloseDesktop(inputDesktop);
        error = "Windows could not read the DWM frame rectangle or client origin. Retry on an interactive desktop, or use includeFrame false.";
        var clientOrigin = new NativePoint();
        if (DwmGetWindowAttribute(hwnd, 9, out var rect, Marshal.SizeOf<NativeRect>()) != 0
            || !ClientToScreen(hwnd, ref clientOrigin))
            return null;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
            return null;
        // Refuse partial/off-screen captures rather than filling missing desktop pixels with black.
        var desktop = new PixelRect(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));
        if (!desktop.Contains(new PixelRect(rect.Left, rect.Top, width, height)))
        {
            error = "The window frame extends outside the desktop. Move it fully on screen and retry includeFrame true.";
            return null;
        }
        error = "Windows could not copy the desktop pixels. Retry on an unlocked interactive desktop, or use includeFrame false.";
        var screen = GetDC(0);
        if (screen == 0) return null;
        var memory = CreateCompatibleDC(screen);
        nint pixels = 0;
        var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
        var dib = CreateDIBSection(screen, ref info, 0, out pixels, 0, 0);
        nint old = 0;
        try
        {
            if (memory == 0 || dib == 0 || pixels == 0) return null;
            old = SelectObject(memory, dib);
            if (old == 0 || old == -1) return null;
            if (!BitBlt(memory, 0, 0, width, height, screen, rect.Left, rect.Top, 0x40CC0020))
                return null;
            if (!GdiFlush()) return null;
            var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96 * window.DesktopScaling, 96 * window.DesktopScaling),
                PixelFormat.Bgra8888, AlphaFormat.Opaque);
            try
            {
                using var buffer = bitmap.Lock();
                var row = new byte[checked(width * 4)];
                for (var y = 0; y < height; y++)
                {
                    Marshal.Copy(pixels + y * row.Length, row, 0, row.Length);
                    Marshal.Copy(row, 0, buffer.Address + y * buffer.RowBytes, row.Length);
                }
            }
            catch { bitmap.Dispose(); throw; }
            windowOffset = new PixelPoint(clientOrigin.X - rect.Left, clientOrigin.Y - rect.Top);
            error = null;
            return bitmap;
        }
        finally
        {
            if (old != 0 && old != -1) SelectObject(memory, old);
            if (dib != 0) DeleteObject(dib);
            if (memory != 0) DeleteDC(memory);
            ReleaseDC(0, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
    }
    [DllImport("user32.dll")] private static extern nint OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(nint desktop);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern uint GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint hwnd, ref NativePoint point);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out NativeRect rect, int size);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint pixels, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint dest, int x, int y, int width, int height, nint source, int sx, int sy, uint operation);
}
