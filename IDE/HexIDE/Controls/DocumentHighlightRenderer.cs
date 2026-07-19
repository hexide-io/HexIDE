using System.Collections.Generic;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace HexIDE.Controls;

/// <summary>
/// AvaloniaEdit background renderer that draws highlight boxes over document-highlight
/// ranges returned by <c>textDocument/documentHighlight</c>.
/// </summary>
public sealed class DocumentHighlightRenderer : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private readonly List<(int Start, int End)> _segments = new();
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.FromArgb(80, 100, 180, 255));

    public KnownLayer Layer => KnownLayer.Background;

    public DocumentHighlightRenderer(TextEditor editor)
    {
        _editor = editor;
    }

    public void SetHighlights(IEnumerable<(int Start, int End)> segments)
    {
        _segments.Clear();
        _segments.AddRange(segments);
        _editor.TextArea.TextView.InvalidateLayer(Layer);
    }

    public void Clear()
    {
        _segments.Clear();
        _editor.TextArea.TextView.InvalidateLayer(Layer);
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_segments.Count == 0) return;
        var document = _editor.Document;
        foreach (var (start, end) in _segments)
        {
            if (start < 0 || end > document.TextLength || start > end) continue;
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(
                         textView, new TextSegment { StartOffset = start, EndOffset = end }))
            {
                drawingContext.DrawRectangle(HighlightBrush, null, rect);
            }
        }
    }
}
