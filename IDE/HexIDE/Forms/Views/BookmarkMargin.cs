using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using HexIDE.Bookmarks;

namespace HexIDE.Forms.Views;

public sealed class BookmarkMargin : AbstractMargin
{
    private const double MarginWidth = 14;
    private const double CircleDiameter = 9;
    private static readonly IBrush BookmarkBrush = new SolidColorBrush(Color.Parse("#00C0C0"));

    private readonly IBookmarkService _bookmarkService;
    private readonly string _documentUri;

    public BookmarkMargin(IBookmarkService bookmarkService, string documentUri)
    {
        _bookmarkService = bookmarkService;
        _documentUri = documentUri;
        _bookmarkService.BookmarksChanged += OnBookmarksChanged;
    }

    protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
    {
        if (oldTextView != null)
            oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView != null)
            newTextView.VisualLinesChanged += OnVisualLinesChanged;
        InvalidateVisual();
    }

    private void OnVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

    private void OnBookmarksChanged(string uri)
    {
        if (uri == _documentUri)
            InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new Size(MarginWidth, 0);

    public override void Render(DrawingContext context)
    {
        var textView = TextView;
        if (textView == null || !textView.VisualLinesValid) return;

        double radius = CircleDiameter / 2;
        double cx = MarginWidth / 2;

        foreach (var line in textView.VisualLines)
        {
            int lineNumber = line.FirstDocumentLine.LineNumber - 1; // convert to 0-based
            if (!_bookmarkService.IsBookmarked(_documentUri, lineNumber)) continue;

            double y = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.LineTop)
                       - textView.VerticalOffset;
            double cy = y + line.Height / 2;

            context.DrawEllipse(BookmarkBrush, null, new Point(cx, cy), radius, radius);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var textView = TextView;
        if (textView == null) return;

        var posInView = e.GetPosition(textView);
        double docY = posInView.Y + textView.VerticalOffset;

        var visualLine = textView.GetVisualLineFromVisualTop(docY);
        if (visualLine == null) return;

        int lineNumber = visualLine.FirstDocumentLine.LineNumber - 1; // 0-based
        _bookmarkService.Toggle(_documentUri, lineNumber);
        e.Handled = true;
    }
}
