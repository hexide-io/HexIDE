using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using HexIDE.Lsp.Messages;

namespace HexIDE.Controls;

/// <summary>Paints server labels after the line, without adding document text or caret columns.</summary>
internal sealed class InlayHintRenderer(TextEditor editor) : IBackgroundRenderer
{
    private InlayHint[] hints = [];
    public IBrush? Brush { get; set; }
    public KnownLayer Layer => KnownLayer.Text;

    public void SetHints(InlayHint[] value)
    {
        hints = value;
        editor.TextArea.TextView.InvalidateLayer(Layer);
    }

    internal IEnumerable<(Point Origin, string Text)> Placements(TextView view)
    {
        if (!view.VisualLinesValid) yield break;
        foreach (var line in view.VisualLines)
        {
            if (line.FirstDocumentLine.LineNumber != line.LastDocumentLine.LineNumber) continue;
            var labels = hints.Where(h => h.Position.Line + 1 == line.LastDocumentLine.LineNumber
                && h.Position.Character >= 0 && h.Position.Character <= line.LastDocumentLine.Length)
                .Select(h => h.Text).Where(text => text.Length > 0).ToArray();
            if (labels.Length == 0) continue;
            var end = line.GetVisualPosition(line.VisualLength, VisualYPosition.TextTop);
            yield return (new Point(end.X + editor.FontSize - view.HorizontalOffset, end.Y - view.VerticalOffset),
                string.Join("  ", labels));
        }
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Brush is null) return;
        using var clip = drawingContext.PushClip(new Rect(0, 0, textView.Bounds.Width, textView.Bounds.Height));
        foreach (var (origin, text) in Placements(textView))
        {
            var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(editor.FontFamily, editor.FontStyle, editor.FontWeight), editor.FontSize, Brush);
            drawingContext.DrawText(formatted, origin);
        }
    }
}
