using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using HexIDE.Runtime.ProjectElements;

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
    private readonly DocumentIdentity _document;
    private readonly Func<int, bool> _toggle;
    private readonly Func<bool>? _externalActive;
    private readonly Func<IReadOnlyList<ExternalBreakpoint>>? _externalRows;
    private readonly Func<int, string?>? _describe;
    private int? hoverLine;

    /// <param name="document">
    /// The document this gutter is drawn for. An identity rather than a name: a gutter is built when its view
    /// attaches and lives until the tab closes, so a name captured here and recomputed by the F9 command
    /// stopped agreeing the moment the document was renamed (#269). The identity is the same value on both
    /// sides for as long as the document is loaded.
    /// </param>
    /// <param name="toggle">
    /// What a click does, given the clicked line: the code window's own toggle, which refuses a read-only line
    /// (#273 task 3.12). Not the store's, which takes any line it is given. A click on a folded header lands on
    /// the fold's first line, which is the header's, so it is refused with the rest.
    /// </param>
    public BreakpointMargin(IBreakpointService breakpoints, DocumentIdentity document, Func<int, bool> toggle,
        Func<bool>? externalActive = null, Func<IReadOnlyList<ExternalBreakpoint>>? externalRows = null,
        Func<int, string?>? describe = null)
    {
        _breakpoints = breakpoints;
        _document = document;
        _toggle = toggle;
        _externalActive = externalActive;
        _externalRows = externalRows;
        _describe = describe;
        AutomationProperties.SetAutomationId(this, "BreakpointMargin");
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ControlAutomationPeer(this);

    public void RefreshExternal()
    {
        InvalidateVisual();
        if (hoverLine is { } line) ToolTip.SetTip(this, _describe?.Invoke(line));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var view = TextView;
        hoverLine = view?.GetVisualLineFromVisualTop(e.GetPosition(view).Y + view.VerticalOffset)?.FirstDocumentLine.LineNumber;
        ToolTip.SetTip(this, hoverLine is { } line ? _describe?.Invoke(line) : null);
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

    private void OnBreakpointsChanged(DocumentIdentity document)
    {
        if (document == _document)
            InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new Size(MarginWidth, 0);

    public override void Render(DrawingContext context)
    {
        // Empty lines must take pointer input too, not just the painted marker shapes.
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        var textView = TextView;
        if (textView == null || !textView.VisualLinesValid) return;

        double radius = CircleDiameter / 2;
        double cx = MarginWidth / 2;
        var external = _externalActive?.Invoke() == true;
        var rows = external ? _externalRows?.Invoke() ?? [] : [];

        foreach (var line in textView.VisualLines)
        {
            int lineNumber = line.FirstDocumentLine.LineNumber; // 1-based, matches the store
            var row = rows.FirstOrDefault(item => item.RequestedLine == lineNumber);
            if (external ? row is null : !_breakpoints.IsBreakpoint(_document, lineNumber)) continue;

            double y = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.LineTop)
                       - textView.VerticalOffset;
            double cy = y + line.Height / 2;

            var pen = new Pen(BreakpointBrush, 1.5);
            if (row is { Pending: true })
                context.DrawEllipse(null, pen, new Point(cx, cy), radius, radius);
            else if (row is { Verified: false })
            {
                context.DrawLine(pen, new Point(cx - radius, cy - radius), new Point(cx + radius, cy + radius));
                context.DrawLine(pen, new Point(cx - radius, cy + radius), new Point(cx + radius, cy - radius));
            }
            else if (row is { Relocated: true })
            {
                context.DrawLine(pen, new Point(cx, cy - radius), new Point(cx + radius, cy));
                context.DrawLine(pen, new Point(cx + radius, cy), new Point(cx, cy + radius));
                context.DrawLine(pen, new Point(cx, cy + radius), new Point(cx - radius, cy));
                context.DrawLine(pen, new Point(cx - radius, cy), new Point(cx, cy - radius));
            }
            else context.DrawEllipse(BreakpointBrush, null, new Point(cx, cy), radius, radius);
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
        _toggle(lineNumber);
        e.Handled = true;
    }
}
