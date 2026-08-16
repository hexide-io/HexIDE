using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HexIDE.Tools;

public partial class ImmediateToolView : UserControl
{
    public ImmediateToolView()
    {
        InitializeComponent();
        // Tunnel so we own Enter before AvaloniaEdit inserts its newline: on a non-blank line, Enter EVALUATES
        // the line and prints the result below (VB6's Immediate window), rather than just breaking the line.
        Editor.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
    }

    private async void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
            return;
        if (e.KeyModifiers != KeyModifiers.None)   // Shift+Enter etc. — let the editor insert a plain newline
            return;
        if (DataContext is not ImmediateToolViewModel vm)
            return;

        var doc = Editor.Document;
        var caretLine = doc.GetLineByNumber(Editor.TextArea.Caret.Line);
        string lineText = doc.GetText(caretLine.Offset, caretLine.Length).Trim();
        if (lineText.Length == 0)
            return;   // blank line — a normal newline

        e.Handled = true;   // we take over Enter on a non-blank line

        string output = vm.IsPaused
            ? (await vm.EvaluateAsync(lineText) ?? vm.BreakModeOnlyNote)
            : vm.BreakModeOnlyNote;

        // Print the result on the next line and leave the caret on a fresh line below it (VB6 Immediate behaviour).
        int at = caretLine.EndOffset;
        string block = "\n" + output + "\n";
        doc.Insert(at, block);
        Editor.CaretOffset = at + block.Length;
        Editor.TextArea.Caret.BringCaretToView();
    }
}
