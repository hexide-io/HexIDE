using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using HexIDE.Lsp.Messages;

namespace HexIDE.Controls;

/// <summary>Inline server labels with no document characters or caret stops.</summary>
internal sealed class InlayHintRenderer(TextEditor editor) : VisualLineElementGenerator
{
    private InlayHint[] hints = [];
    private InlayHint[] inlineValues = [];
    private IEnumerable<InlayHint> Labels => hints.Concat(inlineValues);
    public IBrush? Brush { get; set; }

    public void SetHints(InlayHint[] value)
    {
        hints = value;
        editor.TextArea.TextView.Redraw();
    }

    public void SetInlineValues(InlayHint[] value)
    {
        inlineValues = value;
        editor.TextArea.TextView.Redraw();
    }

    private IEnumerable<InlayHint> At(int offset) => Labels.Where(h => Offset(h) == offset && h.Text.Length > 0);

    private int Offset(InlayHint hint)
    {
        if (hint.Position.Line < 0 || hint.Position.Line >= editor.Document.LineCount) return -1;
        var line = editor.Document.GetLineByNumber(hint.Position.Line + 1);
        return hint.Position.Character >= 0 && hint.Position.Character <= line.Length
            ? line.Offset + hint.Position.Character : -1;
    }

    public override int GetFirstInterestedOffset(int startOffset) => Labels.Where(h => h.Text.Length > 0)
        .Select(Offset).Where(offset => offset >= startOffset).DefaultIfEmpty(-1).Min();

    public override VisualLineElement? ConstructElement(int offset)
    {
        // A folded closing line can end at this offset, but its label belongs to the hidden line.
        if (editor.Document.GetLineByOffset(offset).LineNumber != CurrentContext.VisualLine.FirstDocumentLine.LineNumber)
            return null;
        var labels = At(offset).ToArray();
        if (labels.Length == 0) return null;
        var panel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, IsHitTestVisible = false };
        foreach (var hint in labels)
        {
            panel.Children.Add(new TextBlock
            {
                Text = hint.Text, FontFamily = editor.FontFamily, FontSize = editor.FontSize,
                FontStyle = editor.FontStyle, FontWeight = editor.FontWeight, Foreground = Brush,
                Margin = new Thickness(hint.PaddingLeft ? editor.FontSize / 4 : 0, 0,
                    hint.PaddingRight ? editor.FontSize / 4 : 0, 0)
            });
        }
        TextBlock.SetBaselineOffset(panel, editor.TextArea.TextView.DefaultBaseline);
        return new HintElement(panel, CurrentContext.VisualLine);
    }

    private sealed class HintElement(Control control, VisualLine line) : InlineObjectElement(0, control)
    {
        // Both neighbouring text runs offer a stop at the same document offset. Continue beyond the
        // opposite edge so an arrow key never spends a second step on that offset.
        public override bool HandlesLineBorders => true;

        public override int GetNextCaretPosition(int visualColumn, LogicalDirection direction, CaretPositioningMode mode)
        {
            if (direction == LogicalDirection.Forward && visualColumn < VisualColumn)
                return VisualColumn + VisualLength;
            if (direction == LogicalDirection.Backward && visualColumn > VisualColumn + VisualLength)
                return VisualColumn;
            return line.GetNextCaretPosition(direction == LogicalDirection.Forward ? VisualColumn + VisualLength : VisualColumn,
                direction, mode, false);
        }
    }
}
