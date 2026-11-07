using System;
using System.Runtime.InteropServices;

namespace HexIDE.Integration.Tests.Views;

// Independent Windows reference for the installed Segoe UI 9 pt font at 96 dpi.
internal static class ToolbarGdiProbe
{
    internal static int Measure(string caption)
    {
        var dc = CreateCompatibleDC(IntPtr.Zero);
        dc.Should().NotBe(IntPtr.Zero);
        var font = CreateFontW(-12, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 0, 0, "Segoe UI");
        font.Should().NotBe(IntPtr.Zero);
        var previous = SelectObject(dc, font);
        try
        {
            GetTextExtentPoint32W(dc, caption, caption.Length, out var size).Should().BeTrue();
            return size.Width;
        }
        finally
        {
            SelectObject(dc, previous);
            DeleteObject(font);
            DeleteDC(dc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Size { public int Width; public int Height; }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation,
        int weight, uint italic, uint underline, uint strikeOut, uint charSet, uint outputPrecision,
        uint clipPrecision, uint quality, uint pitchAndFamily, string face);
    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTextExtentPoint32W(IntPtr dc, string text, int count, out Size size);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr dc);
}
