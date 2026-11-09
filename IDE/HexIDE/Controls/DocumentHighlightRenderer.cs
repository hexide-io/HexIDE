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
    private readonly TextSegmentCollection<TextSegment> _segments;
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.FromArgb(80, 100, 180, 255));

    internal IEnumerable<TextSegment> Segments => _segments;

    public KnownLayer Layer => KnownLayer.Background;

    public DocumentHighlightRenderer(TextEditor editor)
    {
        _editor = editor;
        _segments = new TextSegmentCollection<TextSegment>(editor.Document);
    }

    public void SetHighlights(IEnumerable<(int Start, int End)> segments)
    {
        _segments.Clear();
        foreach (var (start, end) in segments)
            _segments.Add(new TextSegment { StartOffset = start, EndOffset = end });
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
        foreach (var segment in _segments)
        {
            if (segment.StartOffset < 0 || segment.EndOffset > document.TextLength) continue;
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(
                         textView, segment))
            {
                drawingContext.DrawRectangle(HighlightBrush, null, rect);
            }
        }
    }
}
