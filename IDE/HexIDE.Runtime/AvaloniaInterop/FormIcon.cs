using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;

namespace HexIDE.Runtime.AvaloniaInterop;

/// <summary>What a form's file says about its icon: nothing, (None), or an icon of its own.</summary>
internal enum FormIconKind
{
    /// <summary>No Icon line: VB6 gives the window its default form icon. HexIDE gives its own mark.</summary>
    Default,

    /// <summary><c>Icon</c> saved as (None), an empty picture: the window has no icon at all.</summary>
    None,

    /// <summary>An icon the file holds.</summary>
    Own,
}

/// <summary>
/// The icon a running form's window carries, from the form's Icon (#1594). It carried HexIDE's own, whatever the file
/// said, so a dialog VB6 shows bare showed HexIDE's mark, and a form's own icon was never shown.
/// </summary>
/// <remarks>
/// Measured on vb6.exe, built EXEs, native and p-code agreeing (the oracle's "A form's window icon"):
/// <list type="bullet">
/// <item>No Icon line: VB6's default icon as the window's big icon and no small one. Windows draws it in the caption,
/// a Fixed Dialog's included.</item>
/// <item>Icon = (None): no icon, big or small. A Fixed Dialog's caption shows none, and a sizable one Windows'
/// generic icon. VB98's own dialog templates save this to keep a dialog's caption bare.</item>
/// <item>An icon of its own: that icon as both the big and the small icon.</item>
/// </list>
/// Read once, as the window is made, as <see cref="FormChrome"/> is.
/// </remarks>
internal readonly record struct FormIcon(FormIconKind Kind, byte[]? Image)
{
    private const uint WmGetIcon = 0x007F, WmSetIcon = 0x0080;
    private static readonly IntPtr IconSmall = 0;

    /// <summary>The icon the form's file describes.</summary>
    /// <remarks>
    /// The blob is the <c>.frx</c> record as read: a four-byte length, then a StdPicture stream, a preamble and the
    /// image's size, then the image. (None) is a StdPicture of size 0. The size is read from the stream rather than the
    /// image taken as the rest of the blob, because a blob runs to the next offset the form cites, or the file's end, and
    /// so can hold a record that isn't this one.
    /// </remarks>
    public static FormIcon Read(ComponentInstance form)
    {
        if (form.GetPropertyOrDefault(VBProperties.IconProperty) is not { Length: > 0 } blob)
            return new FormIcon(FormIconKind.Default, null);
        var at = VBImage.StdPictureHeaderLength(blob) > 0 ? 0 : VBImage.StdPictureHeaderLength(blob, 4) > 0 ? 4 : -1;
        if (at < 0)
            return new FormIcon(FormIconKind.Own, blob);
        var start = at + 8;
        var size = Math.Min(BitConverter.ToInt32(blob, at + 4), blob.Length - start);
        return size <= 0
            ? new FormIcon(FormIconKind.None, null)
            : new FormIcon(FormIconKind.Own, blob[start..(start + size)]);
    }

    /// <summary>Give <paramref name="window"/> this icon.</summary>
    public void Apply(Window window)
    {
        switch (Kind)
        {
            case FormIconKind.Own:
                try
                {
                    window.Icon = new WindowIcon(new MemoryStream(Image!, writable: false));
                }
                catch (Exception)
                {
                    // An image Avalonia cannot read as an icon. The window keeps the default, and the form still opens.
                }
                break;
            case FormIconKind.None:
                // Avalonia gives a window with no Icon the executable's own. On Windows the icon is set with WM_SETICON and
                // read back with WM_GETICON, which Avalonia answers itself, so both are answered here: no icon is kept,
                // and none is reported. Elsewhere the window has no icon of its own to clear.
                Win32Properties.AddWndProcHookCallback(window, NoIcon);
                break;
            case FormIconKind.Default:
                if (VBLoader.DefaultFormIcon is { } icon)
                    window.Icon = icon;
                // VB6's default is a big icon only. Windows draws the caption's from it, so dropping the small one
                // changes only what WM_GETICON reports.
                Win32Properties.AddWndProcHookCallback(window, BigIconOnly);
                break;
        }
    }

    private static IntPtr NoIcon(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is WmSetIcon or WmGetIcon)
            handled = true;
        return IntPtr.Zero;
    }

    private static IntPtr BigIconOnly(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is WmSetIcon or WmGetIcon && wParam == IconSmall)
            handled = true;
        return IntPtr.Zero;
    }
}
