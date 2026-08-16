using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace HexIDE.Debugging;

/// <summary>
/// The breakpoint gutter — a red dot per breakpoint line, click-to-toggle. Lines are 1-based throughout (matching
/// <see cref="IBreakpointService"/>, ANTLR <c>stmt.Start.Line</c> and the runtime), so — unlike
/// <c>BookmarkMargin</c>, which stores 0-based — this never subtracts 1.
/// </summary>
public sealed class BreakpointMargin : AbstractMargin
{
    private const double MarginWidth = 14;
    private const double CircleDiameter = 9;
    private static readonly IBrush BreakpointBrush = new SolidColorBrush(Color.Parse("#C00000"));

    private readonly IBreakpointService _breakpoints;
    private readonly string _documentUri;

    public BreakpointMargin(IBreakpointService breakpoints, string documentUri)
    {
        _breakpoints = breakpoints;
        _documentUri = documentUri;
    }

    protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
    {
        if (oldTextView != null)
            oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView != null)
            newTextView.VisualLinesChanged += OnVisualLinesChanged;

        // Subscribe to the session-singleton store only while attached — otherwise every margin from every
        // opened/closed tab stays pinned by the store's event for the whole session (a growing leak). The `-=`
        // before `+=` keeps a single subscription across repeated OnTextViewChanged calls.
        _breakpoints.BreakpointsChanged -= OnBreakpointsChanged;
        if (newTextView != null)
            _breakpoints.BreakpointsChanged += OnBreakpointsChanged;

        InvalidateVisual();
    }

    private void OnVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

    private void OnBreakpointsChanged(string uri)
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
            int lineNumber = line.FirstDocumentLine.LineNumber; // 1-based, matches the store
            if (!_breakpoints.IsBreakpoint(_documentUri, lineNumber)) continue;

            double y = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.LineTop)
                       - textView.VerticalOffset;
            double cy = y + line.Height / 2;

            context.DrawEllipse(BreakpointBrush, null, new Point(cx, cy), radius, radius);
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

        int lineNumber = visualLine.FirstDocumentLine.LineNumber; // 1-based
        _breakpoints.Toggle(_documentUri, lineNumber);
        e.Handled = true;
    }
}
