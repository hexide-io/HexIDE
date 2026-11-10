using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HexIDE.Controls.TextEditing;

// AvaloniaEdit 12.0.0 re-enters MakeVisible through selection's ScrollOffsetChanged handler when the
// caret rectangle (including its border) cannot fit in the viewport (#3273, AvaloniaEdit#596).
public class TextEditor : AvaloniaEdit.TextEditor
{
    public TextEditor() : base(new TextArea()) { }

    protected override Type StyleKeyOverride => typeof(AvaloniaEdit.TextEditor);
}

internal sealed class TextArea : AvaloniaEdit.Editing.TextArea
{
    public TextArea() : base(new TextView())
    {
        // The protected constructor omits the public constructor's key forwarding to stacked handlers.
        AddHandler(KeyDownEvent, (_, args) => ForwardKey(args, down: true), RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, args) => ForwardKey(args, down: false), RoutingStrategies.Tunnel);
    }

    // AvaloniaEdit handles indentation in OnKeyDown, not EditingCommands.TabForward/TabBackward.
    internal void Indent(bool backwards)
    {
        Focus();
        base.OnKeyDown(new KeyEventArgs
        {
            RoutedEvent = KeyDownEvent,
            Key = Key.Tab,
            KeyModifiers = backwards ? KeyModifiers.Shift : KeyModifiers.None
        });
    }

    private void ForwardKey(KeyEventArgs args, bool down)
    {
        foreach (var handler in StackedInputHandlers)
        {
            if (args.Handled) return;
            if (down) handler.OnPreviewKeyDown(args);
            else handler.OnPreviewKeyUp(args);
        }
    }

    protected override Type StyleKeyOverride => typeof(AvaloniaEdit.Editing.TextArea);
}

internal sealed class TextView : AvaloniaEdit.Rendering.TextView
{
    private bool makingVisible;

    protected override Type StyleKeyOverride => typeof(AvaloniaEdit.Rendering.TextView);

    public override void MakeVisible(Rect rectangle)
    {
        if (makingVisible) return;
        makingVisible = true;
        try { base.MakeVisible(rectangle); }
        finally { makingVisible = false; }
    }
}
