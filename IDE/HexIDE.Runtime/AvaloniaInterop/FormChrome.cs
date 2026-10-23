using Avalonia.Controls;
using HexIDE.Runtime.Components;

namespace HexIDE.Runtime.AvaloniaInterop;

/// <summary>
/// The window a running form is drawn in: its border, caption bar and buttons, from its BorderStyle, ControlBox, Caption,
/// MinButton, MaxButton and menu bar (#1575). It was a sizable window with a title bar and every button, whatever the
/// form's file said.
/// </summary>
/// <remarks>
/// <para>
/// Measured on vb6.exe, built EXEs, native and p-code agreeing: the window styles each form's window was given, read with
/// <c>GetWindowLong</c> (the oracle's "A form's window styles"). On Windows the window is given those styles exactly,
/// through Avalonia's style callback, which owns only the bits measured here. Elsewhere the same rules drive Avalonia's
/// own window properties, which have no tool window and no title bar without a close box.
/// </para>
/// <para>
/// Read once, as the window is made, as <see cref="FormFrame"/> is: a Caption emptied or a ControlBox changed at run time
/// keeps the window it had.
/// </para>
/// </remarks>
internal readonly record struct FormChrome(int BorderStyle, bool ControlBox, bool CaptionBar, bool MinButton, bool MaxButton)
{
    private const uint WsBorder = 0x00800000, WsDlgFrame = 0x00400000, WsSysMenu = 0x00080000, WsThickFrame = 0x00040000;
    private const uint WsMinimizeBox = 0x00020000, WsMaximizeBox = 0x00010000, DsModalFrame = 0x00000080;
    private const uint WsExDlgModalFrame = 0x00000001, WsExToolWindow = 0x00000080;

    /// <summary>The style bits this decides: the rest, visibility and clipping and the window's state, are Avalonia's.</summary>
    internal const uint OwnedStyle = WsBorder | WsDlgFrame | WsSysMenu | WsThickFrame | WsMinimizeBox | WsMaximizeBox | DsModalFrame;

    /// <summary>The extended style bits this decides. The taskbar's, WS_EX_APPWINDOW, follows ShowInTaskbar already.</summary>
    internal const uint OwnedExStyle = WsExDlgModalFrame | WsExToolWindow;

    /// <summary>The window a form's file describes.</summary>
    /// <remarks>
    /// A borderless form with a menu has Fixed Single's window, caption bar included (measured), and a form whose
    /// ControlBox is False and whose Caption is empty has no caption bar (measured, #1072), whatever MinButton and MaxButton
    /// say. All three of MinButton, MaxButton and ControlBox are True where the file leaves them out (measured).
    /// </remarks>
    public static FormChrome Read(ComponentInstance form, bool menu)
    {
        var borderStyle = VBLoader.RawNumber(form, "BorderStyle") ?? 2;
        var controlBox = VBLoader.RawNumber(form, "ControlBox") != 0;
        // Measured with a caption and a control box only: that the menu keeps the caption bar without either, as #1072's
        // frame has always taken it to, is inferred.
        var menuOnly = borderStyle == 0 && menu;
        if (menuOnly)
            borderStyle = 1;
        var captionBar = menuOnly || borderStyle != 0
            && (controlBox || !string.IsNullOrEmpty(form.GetPropertyOrDefault(VBProperties.CaptionProperty)));
        return new FormChrome(borderStyle, controlBox, captionBar,
            form.GetPropertyOrDefault(VBProperties.MinButtonProperty), form.GetPropertyOrDefault(VBProperties.MaxButtonProperty));
    }

    private bool Sizable => BorderStyle is 2 or 5;

    private bool ToolWindow => BorderStyle is 4 or 5;

    /// <summary>
    /// The styles vb6.exe gives the window, as measured: a border, and the caption bar's frame, for any style but None; a
    /// resizing frame for the sizable ones; the system menu, and so the close box, with ControlBox; the minimize and
    /// maximize boxes as MinButton and MaxButton say, but never on a Fixed Dialog, which has the dialog frame instead; and
    /// a tool window's caption for the two tool windows.
    /// </summary>
    public (uint Style, uint ExStyle) Win32Styles
    {
        get
        {
            if (BorderStyle == 0)
                return (0, 0);
            var style = WsBorder | (CaptionBar ? WsDlgFrame : 0) | (Sizable ? WsThickFrame : 0) | (ControlBox ? WsSysMenu : 0);
            if (BorderStyle == 3)
                return (style | DsModalFrame, WsExDlgModalFrame);
            style |= (MinButton ? WsMinimizeBox : 0) | (MaxButton ? WsMaximizeBox : 0);
            return (style, ToolWindow ? WsExToolWindow : 0);
        }
    }

    /// <summary>Whether the user can resize the window by its border.</summary>
    public bool CanResize => Sizable;

    /// <summary>The frame Avalonia draws: none for a borderless form, a border alone with no caption bar, or both.</summary>
    public WindowDecorations Decorations =>
        BorderStyle == 0 ? WindowDecorations.None : CaptionBar ? WindowDecorations.Full : WindowDecorations.BorderOnly;

    // A button is drawn only with the system menu. Windows draws none on a tool window's caption, whatever the styles say:
    // that is Windows' rule, not measured here.
    private bool Buttons => ControlBox && CaptionBar && BorderStyle is 1 or 2;

    /// <summary>Whether the caption bar has a minimize button.</summary>
    public bool CanMinimize => Buttons && MinButton;

    /// <summary>Whether the caption bar has a maximize button. Avalonia allows one only on a window it can resize.</summary>
    public bool CanMaximize => Buttons && MaxButton;

    /// <summary>Give <paramref name="window"/> this chrome.</summary>
    public void Apply(Window window)
    {
        var (style, exStyle) = Win32Styles;
        // Registered first: Avalonia recomputes the window's styles on each property change below and again as the window is
        // shown, and on Windows offers them to this callback before setting them. Every form whose styles differ from
        // Avalonia's own changes one of these properties, so its styles are in place before the window is sized to its
        // client area. The one that changes none is a Sizable form with every button, whose styles are Avalonia's already.
        Win32Properties.AddWindowStylesCallback(window, (s, e) => ((s & ~OwnedStyle) | style, (e & ~OwnedExStyle) | exStyle));
        window.CanResize = CanResize;
        window.CanMaximize = CanMaximize;
        window.CanMinimize = CanMinimize;
        window.WindowDecorations = Decorations;
    }
}
