using System;
using System.Collections.Generic;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace HexIDE.Controls;

/// <summary>
/// AvaloniaEdit background renderer that draws zigzag squiggle underlines for LSP diagnostics.
/// </summary>
public sealed class LspTextMarkerService : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private readonly List<LspMarker> _markers = new();

    public KnownLayer Layer => KnownLayer.Selection;

    /// <summary>
    /// What is currently drawn. Exposed for tests, because "the renderer is attached" and "the renderer
    /// holds the diagnostics" are different claims, and a view that attaches after a publish satisfies only
    /// the first unless it catches up.
    /// </summary>
    internal IReadOnlyList<LspMarker> Markers => _markers;

    public LspTextMarkerService(TextEditor editor)
    {
        _editor = editor;
    }

    /// <summary>Replace all current markers with <paramref name="markers"/> and redraw.</summary>
    public void SetMarkers(IEnumerable<LspMarker> markers)
    {
        _markers.Clear();
        _markers.AddRange(markers);
        _editor.TextArea.TextView.InvalidateLayer(Layer);
    }

    /// <summary>Clear all markers and redraw.</summary>
    public void Clear()
    {
        _markers.Clear();
        _editor.TextArea.TextView.InvalidateLayer(Layer);
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_markers.Count == 0)
            return;

        var document = _editor.Document;

        foreach (var marker in _markers)
        {
            if (marker.StartOffset < 0 || marker.EndOffset > document.TextLength)
                continue;

            var brush = marker.IsError ? Brushes.Red : Brushes.Orange;
            var pen = new Pen(brush, 1.0);

            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(
                         textView, new TextSegment { StartOffset = marker.StartOffset, EndOffset = marker.EndOffset }))
            {
                drawingContext.DrawGeometry(null, pen, BuildSquiggle(rect.Left, rect.Right, rect.Bottom - 1.5));
            }
        }
    }

    /// <summary>
    /// Builds a zigzag squiggle geometry along the bottom of a text segment.
    /// The wave sits at <paramref name="baseline"/> with peaks 2px above it.
    /// </summary>
    private static StreamGeometry BuildSquiggle(double left, double right, double baseline)
    {
        const double step = 3.0;   // horizontal distance per half-wave
        const double amplitude = 2.0; // vertical peak height

        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();

        ctx.BeginFigure(new Avalonia.Point(left, baseline), false);

        double x = left + step;
        bool up = true;
        while (x < right)
        {
            ctx.LineTo(new Avalonia.Point(x, baseline - (up ? amplitude : 0)));
            up = !up;
            x += step;
        }
        // Final point at right edge so the squiggle doesn't stop short
        ctx.LineTo(new Avalonia.Point(right, baseline - (up ? amplitude : 0)));

        return geometry;
    }
}

/// <summary>
/// AvaloniaEdit line transformer that colours error tokens red, mirroring the classic VB6 IDE.
/// </summary>
public sealed class LspDiagnosticsColorizer : DocumentColorizingTransformer
{
    private readonly TextView _textView;
    private readonly List<LspMarker> _markers = new();

    public LspDiagnosticsColorizer(TextView textView)
    {
        _textView = textView;
    }

    public void SetMarkers(IEnumerable<LspMarker> markers)
    {
        _markers.Clear();
        _markers.AddRange(markers);
        // Redraw() clears the visual line cache so ColorizeLine is re-invoked.
        // InvalidateLayer() alone only repaints background renderers and is not enough.
        _textView.Redraw();
    }

    public void Clear()
    {
        _markers.Clear();
        _textView.Redraw();
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        foreach (var marker in _markers)
        {
            if (!marker.IsError)
                continue;

            int start = Math.Max(marker.StartOffset, line.Offset);
            int end   = Math.Min(marker.EndOffset,   line.EndOffset);

            if (start >= end)
                continue;

            ChangeLinePart(start, end, element =>
                element.TextRunProperties.SetForegroundBrush(Brushes.Red));
        }
    }
}

public readonly record struct LspMarker(int StartOffset, int EndOffset, bool IsError, string Message);
