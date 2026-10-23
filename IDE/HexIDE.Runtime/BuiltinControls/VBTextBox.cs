using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

public class VBTextBox : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);

    static VBTextBox()
    {
        TextProperty.Changed.AddClassHandler<VBTextBox>((textBox, e) =>
        {
            textBox.ExecuteSub(TextBoxComponentClass.ChangeEvent);
        });
        // Before Click, so MouseUp comes first, as VB6 raises them (#974).
        AttachedEvents.AttachMouse<VBTextBox>(AttachedEvents.DblClick.InPlaceOfMouseDown);
        AttachedEvents.AttachClick<VBTextBox>();
        // A TextBox raises GotFocus and LostFocus, as SetFocus's are measured to come (#1061).
        AttachedEvents.AttachFocusEvents<VBTextBox>();
    }

    public VBTextBox() => ApplyLayout(false, 0);

    /// <summary>Whether the box holds several lines: read-only at run time, as in VB6 (#1340).</summary>
    public bool MultiLine { get; private set; }

    /// <summary>The scroll bars it asks for: 0 none, 1 horizontal, 2 vertical, 3 both. Read-only at run time (#1340).</summary>
    public int ScrollBars { get; private set; }

    /// <summary>
    /// Lays the box out as its MultiLine and ScrollBars say. Measured on vb6.exe, native and p-code agreeing (the oracle's
    /// "A TextBox's MultiLine and ScrollBars"):
    /// <list type="bullet">
    /// <item>A single-line box has no scroll bars, whatever ScrollBars says, and scrolls sideways as the caret goes.</item>
    /// <item>A MultiLine box wraps unless it has a horizontal scroll bar, and scrolls down as lines are added.</item>
    /// <item>Enter breaks a MultiLine box's line with CR LF, the only line break VB6 knows, so on every host.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// VB6 draws a scroll bar it asks for even when there is nothing to scroll, so a bar here is Visible rather than Auto.
    /// Where VB6 has none, a direction the box still scrolls in is Hidden, and one it never does is Disabled, which also
    /// stops a single-line box showing part of a vertical bar (#914).
    /// </remarks>
    internal void ApplyLayout(bool multiLine, int scrollBars)
    {
        MultiLine = multiLine;
        ScrollBars = scrollBars;
        var horizontal = multiLine && scrollBars is 1 or 3;
        var vertical = multiLine && scrollBars is 2 or 3;
        AcceptsReturn = multiLine;
        NewLine = "\r\n";
        TextWrapping = multiLine && !horizontal ? TextWrapping.Wrap : TextWrapping.NoWrap;
        ScrollViewer.SetHorizontalScrollBarVisibility(this,
            horizontal ? ScrollBarVisibility.Visible : multiLine ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Hidden);
        ScrollViewer.SetVerticalScrollBarVisibility(this,
            vertical ? ScrollBarVisibility.Visible : multiLine ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Disabled);
    }
}
