using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace HexIDE.Automation;

/// <summary>Reads the actual HWND, and captures the window with its frame as Windows renders it (#1595, #2182).</summary>
internal static class NativeWindowFrame
{
    /// <summary>
    /// A frame capture: the image, the frame rectangle it shows and the client area's origin, both in screen pixels, and,
    /// when it had to be copied from the screen, a note on what covers it.
    /// </summary>
    internal sealed record FrameImage(Bitmap Image, PixelRect Frame, PixelPoint ClientOrigin, string? Note);

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

    /// <summary>
    /// <paramref name="window"/> with its title bar and frame, as Windows renders it, or why it can't be captured.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Rendered, not copied from the screen.</b> A copy of the screen showed whatever was on top: an IDE started from a
    /// script is not the foreground app, so Windows opens a running form behind whatever is, and the image held another
    /// application's text, with the reply of a clean capture (#2182). <c>PrintWindow</c> with <c>PW_RENDERFULLCONTENT</c>
    /// asks Windows for the window's own pixels, composed content and frame included, whatever covers it.
    /// </para>
    /// <para>
    /// Should Windows render nothing, or only black, the frame rectangle is copied from the screen after all, and the note
    /// says how much of it other windows cover, since that part shows them.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Why a window has no frame to capture, worded for whichever it is. A minimized one is restored through
    /// set_window_state, which reaches a running form since #2357; a hidden one only its program can show.
    /// </summary>
    internal static string HiddenOrMinimized(bool minimized) => minimized
        ? "The window is minimized, so Windows draws no frame for it. Restore it with set_window_state, state Normal and "
          + "its name as 'window', then retry includeFrame true."
        : "The window is hidden, so it has no frame on the screen. Show it, then retry includeFrame true.";

    internal static FrameImage? Capture(Window window, out string? error)
    {
        error = "Frame capture requires a Windows HWND; this host/window has none. Use includeFrame false for a client-area render.";
        var hwnd = Handle(window);
        if (!OperatingSystem.IsWindows() || hwnd == 0)
            return null;
        if (!IsWindowVisible(hwnd) || IsIconic(hwnd))
        {
            error = HiddenOrMinimized(IsIconic(hwnd));
            return null;
        }
        error = "Windows could not read the window's rectangles or client origin. Retry on an interactive desktop, or use includeFrame false.";
        var clientOrigin = new NativePoint();
        if (DwmGetWindowAttribute(hwnd, 9, out var bounds, Marshal.SizeOf<NativeRect>()) != 0
            || !GetWindowRect(hwnd, out var outer)
            || !ClientToScreen(hwnd, ref clientOrigin))
            return null;
        var frame = ToPixelRect(bounds);
        var whole = ToPixelRect(outer);
        if (frame.Width <= 0 || frame.Height <= 0)
            return null;
        var origin = new PixelPoint(clientOrigin.X, clientOrigin.Y);
        var scaling = window.DesktopScaling;

        // The window's rectangle holds the invisible resize borders as well, which the frame bounds leave out: crop to those.
        if (whole.Contains(frame)
            && Drawn(whole.Size, new PixelRect(frame.X - whole.X, frame.Y - whole.Y, frame.Width, frame.Height), scaling,
                   refuseBlack: true, (memory, _) => PrintWindow(hwnd, memory, 2)) is { } printed)
        {
            error = null;
            return new FrameImage(printed, frame, origin, null);
        }

        var inputDesktop = OpenInputDesktop(0, false, 1);
        if (inputDesktop == 0)
        {
            error = "Windows rendered nothing for this window, and the input desktop is inaccessible, so it could not be copied "
                + "from the screen either. Unlock or reconnect the desktop and retry, or use includeFrame false.";
            return null;
        }
        CloseDesktop(inputDesktop);
        // Refuse partial/off-screen copies rather than filling missing desktop pixels with black.
        var desktop = new PixelRect(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));
        if (!desktop.Contains(frame))
        {
            error = "Windows rendered nothing for this window, and its frame extends outside the desktop, so it could not be "
                + "copied from the screen either. Move it fully on screen and retry includeFrame true.";
            return null;
        }
        error = "Windows rendered nothing for this window, and could not copy the desktop pixels. Retry on an unlocked "
            + "interactive desktop, or use includeFrame false.";
        var copied = Drawn(frame.Size, new PixelRect(frame.Size), scaling, refuseBlack: false,
            (memory, screen) => BitBlt(memory, 0, 0, frame.Width, frame.Height, screen, frame.X, frame.Y, 0x40CC0020));
        if (copied is null)
            return null;
        error = null;
        return new FrameImage(copied, frame, origin, CoverNote(frame, Above(hwnd)));
    }

    /// <summary>
    /// What <paramref name="draw"/> puts in a <paramref name="size"/> bitmap, cut to <paramref name="crop"/>. Null when it
    /// draws nothing, or, with <paramref name="refuseBlack"/>, only black: a frame always has some colour.
    /// </summary>
    private static WriteableBitmap? Drawn(PixelSize size, PixelRect crop, double scaling, bool refuseBlack, Func<nint, nint, bool> draw)
    {
        var screen = GetDC(0);
        if (screen == 0) return null;
        var memory = CreateCompatibleDC(screen);
        var info = new BitmapInfo { Size = 40, Width = size.Width, Height = -size.Height, Planes = 1, BitCount = 32 };
        var dib = CreateDIBSection(screen, ref info, 0, out var pixels, 0, 0);
        nint old = 0;
        try
        {
            if (memory == 0 || dib == 0 || pixels == 0) return null;
            old = SelectObject(memory, dib);
            if (old == 0 || old == -1) return null;
            if (!draw(memory, screen) || !GdiFlush()) return null;
            var bitmap = new WriteableBitmap(crop.Size, new Vector(96 * scaling, 96 * scaling), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            var coloured = false;
            try
            {
                using var buffer = bitmap.Lock();
                var row = new byte[checked(crop.Width * 4)];
                for (var y = 0; y < crop.Height; y++)
                {
                    Marshal.Copy(pixels + (nint)(((long)(crop.Y + y) * size.Width + crop.X) * 4), row, 0, row.Length);
                    for (var x = 0; refuseBlack && !coloured && x < row.Length; x += 4)
                        coloured = (row[x] | row[x + 1] | row[x + 2]) != 0;
                    Marshal.Copy(row, 0, buffer.Address + y * buffer.RowBytes, row.Length);
                }
            }
            catch { bitmap.Dispose(); throw; }
            if (!refuseBlack || coloured)
                return bitmap;
            bitmap.Dispose();
            return null;
        }
        finally
        {
            if (old != 0 && old != -1) SelectObject(memory, old);
            if (dib != 0) DeleteObject(dib);
            if (memory != 0) DeleteDC(memory);
            ReleaseDC(0, screen);
        }
    }

    /// <summary>What a copy of <paramref name="frame"/> from the screen shows of the windows in <paramref name="above"/>.</summary>
    internal static string CoverNote(PixelRect frame, IReadOnlyList<(PixelRect Rect, string Title)> above)
    {
        const string copied = "Windows rendered nothing for this window, so its frame rectangle was copied from the screen";
        var covering = above.Where(a => a.Rect.Intersects(frame)).ToList();
        var fraction = CoveredFraction(frame, covering.Select(a => a.Rect));
        if (fraction <= 0)
            return copied + "; no other window covers it.";
        var percent = Math.Max(1, (int)Math.Round(fraction * 100));
        var top = covering[0].Title is { Length: > 0 } title ? $"'{title}'" : "an untitled window";
        return $"{copied}, and other windows cover {percent}% of it, {top} on top: that part of the image shows them, "
            + "not this window.";
    }

    /// <summary>How much of <paramref name="target"/> the union of <paramref name="above"/> covers, from 0 to 1.</summary>
    internal static double CoveredFraction(PixelRect target, IEnumerable<PixelRect> above)
    {
        var area = (long)target.Width * target.Height;
        var clipped = above.Select(r => r.Intersect(target)).Where(r => r.Width > 0 && r.Height > 0).ToList();
        if (area <= 0 || clipped.Count == 0)
            return 0;
        // The union's area, by strips between each distinct x edge, so windows overlapping one another count once.
        var xs = clipped.SelectMany(r => new[] { r.X, r.Right }).Distinct().Order().ToList();
        long covered = 0;
        for (var i = 0; i + 1 < xs.Count; i++)
        {
            var spans = clipped.Where(r => r.X <= xs[i] && r.Right >= xs[i + 1]).Select(r => (r.Y, r.Bottom)).OrderBy(s => s.Y);
            long height = 0;
            int? start = null, end = null;
            foreach (var (top, bottom) in spans)
            {
                if (end is null || top > end)
                {
                    height += (end ?? 0) - (start ?? 0);
                    (start, end) = (top, bottom);
                }
                else if (bottom > end)
                    end = bottom;
            }
            height += (end ?? 0) - (start ?? 0);
            covered += height * (xs[i + 1] - xs[i]);
        }
        return (double)covered / area;
    }

    /// <summary>
    /// The visible windows above <paramref name="hwnd"/>, top first, with their rectangles: not its own popups, which are
    /// part of what it shows, nor a cloaked window or a click-through overlay, which show nothing over it.
    /// </summary>
    private static List<(PixelRect Rect, string Title)> Above(nint hwnd)
    {
        var above = new List<(PixelRect, string)>();
        var size = Marshal.SizeOf<NativeRect>();
        for (var (h, n) = (GetWindow(hwnd, 3), 0); h != 0 && n < 4096; (h, n) = (GetWindow(h, 3), n + 1))
        {
            if (!IsWindowVisible(h) || IsIconic(h) || OwnedBy(h, hwnd) || (GetWindowLong(h, -20) & 0x20) != 0
                || DwmGetWindowAttributeInt(h, 14, out var cloaked, 4) == 0 && cloaked != 0)
                continue;
            if (DwmGetWindowAttribute(h, 9, out var bounds, size) != 0 && !GetWindowRect(h, out bounds))
                continue;
            var title = new char[256];
            above.Add((ToPixelRect(bounds), new string(title, 0, Math.Max(0, GetWindowText(h, title, title.Length)))));
        }
        return above;
    }

    private static bool OwnedBy(nint window, nint owner)
    {
        for (var (o, n) = (GetWindow(window, 4), 0); o != 0 && n < 16; (o, n) = (GetWindow(o, 4), n + 1))
            if (o == owner)
                return true;
        return false;
    }

    private static PixelRect ToPixelRect(NativeRect r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

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
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hwnd, char[] text, int count);
    [DllImport("user32.dll")] private static extern bool PrintWindow(nint hwnd, nint dc, uint flags);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out NativeRect rect, int size);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int DwmGetWindowAttributeInt(nint hwnd, int attribute, out int value, int size);
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
