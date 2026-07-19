using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Rendering;

namespace HexIDE.Controls;

/// <summary>
/// PROTOTYPE — a VS-style minimap for the code editor.
///
/// Drawn from the <see cref="TextDocument"/> model (NOT the virtualized
/// <see cref="TextView"/>, which only holds visible lines) and syntax-coloured via the
/// editor's <see cref="IHighlightingDefinition"/>. A translucent viewport rectangle marks
/// the visible region; click/drag scrolls the editor.
///
/// Prototype limitations (intentional, to be revisited if we keep it):
///  • Scale-to-fill with a capped line height: the document maps into the control height,
///    growing lines up to a cap for short files and shrinking to fit for long ones. Very large
///    files (beyond ~Height/MinLineHeight lines) currently render only their top region —
///    sampling / bitmap windowing is deferred.
///  • The viewport rectangle assumes uniform line height — folded/word-wrapped regions skew it.
///  • Read-only; colours are literals here (a drawing control, not themed IDE chrome).
/// </summary>
public sealed class MinimapControl : Control
{
    private const double MinLineHeight = 1.5; // px per line when fitting long files
    private const double MaxLineHeight = 6.0; // px per line cap for short files
    private const double LeftPad       = 2.0;

    // Fallbacks used until the editor's theme brushes can be resolved.
    private static readonly Color FallbackBackground = Color.FromRgb(0xFA, 0xFA, 0xFA);
    private static readonly Color FallbackText       = Color.FromRgb(0x9A, 0xA0, 0xA6);
    private static readonly IBrush ViewportFill = new SolidColorBrush(Color.FromArgb(48, 0x33, 0x66, 0xCC));
    private static readonly IPen   ViewportPen  = new Pen(new SolidColorBrush(Color.FromArgb(140, 0x2A, 0x52, 0xA0)), 1);
    private static readonly IPen   BorderPen    = new Pen(new SolidColorBrush(Color.FromArgb(60, 0x80, 0x80, 0x80)), 1);

    // Resolved from the editor's live theme (see ResolveThemeColors).
    private Color _backgroundColor = FallbackBackground;
    private Color _defaultTextColor = FallbackText;

    private readonly Dictionary<Color, IBrush> _brushCache = new();

    private TextEditor? _editor;
    private TextView? _textView;
    private TextDocument? _document;
    private DocumentHighlighter? _highlighter;
    private Typeface _typeface = Typeface.Default;

    private RenderTargetBitmap? _cache;
    private bool _cacheDirty = true;
    private DispatcherTimer? _debounce;
    private bool _dragging;

    public MinimapControl()
    {
        ClipToBounds = true;
    }

    public void Attach(TextEditor editor, IHighlightingDefinition? definition)
    {
        Detach();

        _editor = editor;
        _textView = editor.TextArea.TextView;
        _document = editor.Document;
        _typeface = new Typeface(editor.FontFamily);

        if (_document is not null && definition is not null)
            _highlighter = new DocumentHighlighter(_document, definition);

        if (_document is not null)
            _document.TextChanged += OnDocumentTextChanged;

        _textView.VisualLinesChanged += OnVisualLinesChanged;
        ActualThemeVariantChanged += OnThemeChanged;
        ResolveThemeColors();

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _debounce.Tick += OnDebounceTick;

        _cacheDirty = true;
        InvalidateVisual();
    }

    public void Detach()
    {
        ActualThemeVariantChanged -= OnThemeChanged;
        if (_document is not null)
            _document.TextChanged -= OnDocumentTextChanged;
        if (_textView is not null)
            _textView.VisualLinesChanged -= OnVisualLinesChanged;
        if (_debounce is not null)
        {
            _debounce.Stop();
            _debounce.Tick -= OnDebounceTick;
            _debounce = null;
        }
        _highlighter?.Dispose();
        _highlighter = null;
        _cache?.Dispose();
        _cache = null;
        _editor = null;
        _textView = null;
        _document = null;
    }

    private void OnDocumentTextChanged(object? sender, EventArgs e) => _debounce?.Start();
    private void OnVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        ResolveThemeColors();
        _cacheDirty = true;
        InvalidateVisual();
    }

    /// <summary>
    /// Derive the minimap background and default-text colours from the editor's live theme
    /// brushes so the minimap reads correctly on dark themes. Falls back to the light literals
    /// when the brushes aren't solid colours.
    /// </summary>
    private void ResolveThemeColors()
    {
        _backgroundColor = SolidColorOf(_editor?.Background) ?? FallbackBackground;

        // Unhighlighted code reads as a soft tint of the foreground toward the background,
        // rather than full contrast (which would fight the syntax-coloured tokens).
        var fg = SolidColorOf(_editor?.Foreground);
        _defaultTextColor = fg is { } f ? Blend(f, _backgroundColor, 0.4) : FallbackText;
    }

    private static Color? SolidColorOf(IBrush? brush) =>
        brush is ISolidColorBrush scb ? scb.Color : null;

    private static Color Blend(Color a, Color b, double t) => Color.FromArgb(
        255,
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounce?.Stop();
        _cacheDirty = true;
        InvalidateVisual();
    }

    // ── minimap layout: rows, sampling, line ↔ row ↔ Y (single source of truth) ──
    // When the document fits, one line maps to one row. When it's taller than the control can
    // show at MinLineHeight, lines are bucketed `Step` per row so the whole file's shape spans
    // the full height (rather than only the top region rendering). The cache renderer, the
    // viewport rectangle, and click hit-testing all derive from ComputeLayout + YForLine/
    // LineForY, so they always agree. Painted rows are bounded by control height / MinLineHeight,
    // independent of file size.

    private readonly record struct Layout(int Step, double RowHeight, int LineCount, int RowCount);

    private Layout ComputeLayout()
    {
        var lineCount = Math.Max(1, _document?.LineCount ?? 1);
        var h = Math.Max(1.0, Bounds.Height);
        var rowsAvailable = Math.Max(1, (int)(h / MinLineHeight));
        var step = Math.Max(1, (int)Math.Ceiling((double)lineCount / rowsAvailable));
        var rowCount = (int)Math.Ceiling((double)lineCount / step);
        var rowHeight = Math.Clamp(h / rowCount, MinLineHeight, MaxLineHeight);
        return new Layout(step, rowHeight, lineCount, rowCount);
    }

    private static double YForLine(int line, Layout layout) =>
        ((line - 1) / layout.Step) * layout.RowHeight;

    private static int LineForY(double y, Layout layout) =>
        Math.Clamp((int)(y / layout.RowHeight) * layout.Step + 1, 1, layout.LineCount);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width < 1 || Bounds.Height < 1) return;

        context.FillRectangle(BrushFor(_backgroundColor), new Rect(Bounds.Size));

        EnsureCache();
        if (_cache is not null)
            context.DrawImage(_cache, new Rect(_cache.Size), new Rect(Bounds.Size));

        DrawViewport(context);
        context.DrawLine(BorderPen, new Point(0.5, 0), new Point(0.5, Bounds.Height));
    }

    private void EnsureCache()
    {
        var w = (int)Bounds.Width;
        var h = (int)Bounds.Height;
        if (w < 1 || h < 1) return;

        var needNew = _cache is null || _cache.PixelSize.Width != w || _cache.PixelSize.Height != h;
        if (!needNew && !_cacheDirty) return;

        if (needNew)
        {
            _cache?.Dispose();
            _cache = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
        }

        var doc = _document;
        if (doc is null || _cache is null) { _cacheDirty = false; return; }

        var layout = ComputeLayout();

        using (var dc = _cache.CreateDrawingContext())
        {
            dc.FillRectangle(BrushFor(_backgroundColor), new Rect(0, 0, w, h));
            // One representative document line per row (the first line of each bucket).
            for (var row = 0; row < layout.RowCount; row++)
            {
                var ln = row * layout.Step + 1;
                if (ln > layout.LineCount) break;
                var y = row * layout.RowHeight;
                if (y > h) break;
                DrawLine(dc, doc, ln, y, layout.RowHeight);
            }
        }
        _cacheDirty = false;
    }

    private void DrawLine(DrawingContext dc, TextDocument doc, int lineNumber, double y, double rowHeight)
    {
        var line = doc.GetLineByNumber(lineNumber);
        if (line.Length == 0) return;

        var text = doc.GetText(line.Offset, line.Length);
        if (string.IsNullOrWhiteSpace(text)) return; // blank line

        // Render the real characters scaled down to the row height (leading whitespace is
        // preserved, so indentation shows). At this size glyphs read as text shapes rather
        // than the solid bars a per-token rectangle would give.
        var fontSize = Math.Max(2.0, rowHeight * 0.9);
        var ft = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _typeface,
            fontSize,
            BrushFor(_defaultTextColor));

        // Colour each syntax section as a foreground span over the formatted line.
        if (_highlighter is not null)
        {
            HighlightedLine? hl = null;
            try { hl = _highlighter.HighlightLine(lineNumber); }
            catch { /* degenerate highlight state — fall back to default colour */ }

            if (hl is not null)
            {
                foreach (var section in hl.Sections)
                {
                    var color = ColorOf(section.Color);
                    if (color is null) continue;
                    var start = section.Offset - line.Offset;
                    if (start < 0 || start >= text.Length) continue;
                    var len = Math.Min(section.Length, text.Length - start);
                    if (len <= 0) continue;
                    ft.SetForegroundBrush(BrushFor(color.Value), start, len);
                }
            }
        }

        dc.DrawText(ft, new Point(LeftPad, y));
    }

    private void DrawViewport(DrawingContext context)
    {
        if (_document is null || _textView is null) return;
        var layout = ComputeLayout();

        // First/last visible *document* lines come straight from the editor's visual layout,
        // so the rectangle's top and bottom track the real on-screen range under folding and
        // word-wrap (one wrapped document line still reports one line number). Mapping them
        // through YForLine keeps the rectangle aligned with the painted rows, sampled or not.
        var (first, last) = VisibleDocumentLineRange(layout.LineCount);

        var y0 = YForLine(first, layout);
        var y1 = YForLine(last + 1, layout); // bottom of the last visible line's row
        var rect = new Rect(0, y0, Bounds.Width, Math.Max(3, y1 - y0));
        context.FillRectangle(ViewportFill, rect);
        context.DrawRectangle(null, ViewportPen, rect);
    }

    /// <summary>First and last visible document line numbers from the editor's visual layout
    /// (folding/word-wrap aware); falls back to the whole document when no visual lines exist.</summary>
    private (int First, int Last) VisibleDocumentLineRange(int lineCount)
    {
        if (_textView is { VisualLinesValid: true } tv && tv.VisualLines.Count > 0)
            return (tv.VisualLines[0].FirstDocumentLine.LineNumber,
                    tv.VisualLines[^1].LastDocumentLine.LineNumber);
        return (1, lineCount);
    }

    // ── pointer: click / drag to scroll ─────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _dragging = true;
        e.Pointer.Capture(this);
        ScrollToPointer(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging) ScrollToPointer(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
    }

    private void ScrollToPointer(PointerEventArgs e)
    {
        ScrollEditorToMinimapY(e.GetPosition(this).Y);
        InvalidateVisual();
    }

    /// <summary>
    /// Scroll the editor so the document line under the given minimap-Y is centred. Folding/
    /// wrap-aware via GetVisualTopByDocumentLine. Internal so the headless test can drive it
    /// without synthesising pointer events.
    /// </summary>
    internal void ScrollEditorToMinimapY(double y)
    {
        if (_editor is null || _document is null || _textView is null) return;
        var layout = ComputeLayout();
        var target = LineForY(y, layout);

        // GetVisualTopByDocumentLine is folding/wrap-aware, so the target lands in the middle
        // of the viewport regardless of collapsed regions above it.
        var viewportH = _textView.Bounds.Height;
        var visualTop = _textView.GetVisualTopByDocumentLine(target);
        _editor.ScrollToVerticalOffset(Math.Max(0, visualTop - viewportH / 2));
    }

    // ── test surface (InternalsVisibleTo HexIDE.Integration.Tests) ───────────────
    internal double MinimapYForLine(int line) => YForLine(line, ComputeLayout());
    internal int MinimapLineForY(double y) => LineForY(y, ComputeLayout());
    internal int MinimapRowCount => ComputeLayout().RowCount;

    // ── helpers ──────────────────────────────────────────────────────────────────

    private IBrush BrushFor(Color c)
    {
        if (!_brushCache.TryGetValue(c, out var b))
        {
            b = new SolidColorBrush(c);
            _brushCache[c] = b;
        }
        return b;
    }

    private static Color? ColorOf(HighlightingColor? hc)
    {
        var brush = hc?.Foreground?.GetBrush(null!);
        return brush is ISolidColorBrush scb ? scb.Color : null;
    }
}
