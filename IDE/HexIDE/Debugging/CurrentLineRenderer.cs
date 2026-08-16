using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;

namespace HexIDE.Debugging;

/// <summary>
/// AvaloniaEdit background renderer that paints the full-width amber "current statement" bar over the line the
/// interpreter is paused on — VB6's yellow break line, driven by <c>IDebugController.Stopped</c>. One 1-based line
/// at a time (or none). Unlike a text-segment highlight it spans the whole line (including empty lines) by walking
/// the visible visual lines, so it reads as a break marker rather than a selection.
/// </summary>
public sealed class CurrentLineRenderer : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private int? _line; // 1-based, or null when not paused here
    private static readonly IBrush Brush = new SolidColorBrush(Color.FromArgb(90, 255, 200, 0)); // amber

    public KnownLayer Layer => KnownLayer.Background;

    public CurrentLineRenderer(TextEditor editor)
    {
        _editor = editor;
    }

    /// <summary>Highlight a 1-based line (or pass null to clear).</summary>
    public void SetLine(int? line)
    {
        _line = line;
        _editor.TextArea.TextView.InvalidateLayer(Layer);
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_line is not int ln || !textView.VisualLinesValid) return;

        foreach (var vline in textView.VisualLines)
        {
            if (vline.FirstDocumentLine.LineNumber > ln || vline.LastDocumentLine.LineNumber < ln)
                continue;

            double y = vline.GetTextLineVisualYPosition(vline.TextLines[0], VisualYPosition.LineTop)
                       - textView.VerticalOffset;
            drawingContext.DrawRectangle(Brush, null, new Rect(0, y, textView.Bounds.Width, vline.Height));
            break;
        }
    }
}
