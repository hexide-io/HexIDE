using System;
using Avalonia.Controls;

namespace HexIDE.Runtime.Dialogs;

internal static class ProgramDialogChrome
{
    // System MessageBox styles measured in #4099; own only the caption buttons and modal frame.
    internal static (uint Style, uint ExStyle) WindowsStyles(uint style, uint exStyle) =>
        (style & ~0x00030000u, exStyle | 0x00000001u);

    private const uint WmGetIcon = 0x007F, WmSetIcon = 0x0080;

    /// <summary>
    /// Keeps a program dialog's title bar free of an icon, as the system message box's is: <c>WM_GETICON</c> on it
    /// returns none (measured, #4099).
    /// </summary>
    /// <remarks>
    /// <c>WS_EX_DLGMODALFRAME</c> alone isn't enough. Avalonia gives every window a default icon with <c>WM_SETICON</c>
    /// and answers <c>WM_GETICON</c> with it, so the caption still showed HexIDE's mark (live, 2026-11-16). Both
    /// messages are answered here with no icon, and nothing else is touched.
    /// </remarks>
    internal static IntPtr NoIcon(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is WmGetIcon or WmSetIcon)
            handled = true;
        return IntPtr.Zero;
    }

    internal static void Apply(Window window)
    {
        // An embedded dialog does not own its host window's caption.
        if (window.Content is not MessageBox { IsProgramDialog: true } and not InputBox { IsProgramDialog: true })
            return;
        if (OperatingSystem.IsWindows())
        {
            Win32Properties.AddWindowStylesCallback(window, WindowsStyles);
            Win32Properties.AddWndProcHookCallback(window, NoIcon);
        }
        window.CanMinimize = false;
        window.CanMaximize = false;
    }
}
