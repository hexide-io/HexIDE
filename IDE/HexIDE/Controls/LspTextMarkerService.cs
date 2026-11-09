using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using HexIDE.Lsp.Messages;

namespace HexIDE.Controls;

/// <summary>
/// AvaloniaEdit background renderer that draws zigzag squiggle underlines for LSP diagnostics.
/// </summary>
public sealed class LspTextMarkerService : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private readonly TextSegmentCollection<AnchoredLspMarker> _markers;

    public KnownLayer Layer => KnownLayer.Selection;

    /// <summary>
    /// What is currently drawn. Exposed for tests, because "the renderer is attached" and "the renderer
    /// holds the diagnostics" are different claims, and a view that attaches after a publish satisfies only
    /// the first unless it catches up.
    /// </summary>
    internal IReadOnlyList<LspMarker> Markers => _markers.Select(m => m.Marker).ToArray();

    public LspTextMarkerService(TextEditor editor)
    {
        _editor = editor;
        _markers = new TextSegmentCollection<AnchoredLspMarker>(editor.Document);
    }

    /// <summary>Replace all current markers with <paramref name="markers"/> and redraw.</summary>
    public void SetMarkers(IEnumerable<LspMarker> markers)
    {
        _markers.Clear();
        foreach (var marker in markers)
            _markers.Add(new AnchoredLspMarker(marker));
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

            var brush = marker.Marker.IsError ? Brushes.Red : Brushes.Orange;
            var pen = new Pen(brush, 1.0);

            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(
                         textView, marker))
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
    private readonly TextSegmentCollection<AnchoredLspMarker> _markers;

    public LspDiagnosticsColorizer(TextView textView)
    {
        _textView = textView;
        _markers = new TextSegmentCollection<AnchoredLspMarker>(textView.Document);
    }

    public void SetMarkers(IEnumerable<LspMarker> markers)
    {
        _markers.Clear();
        foreach (var marker in markers)
            _markers.Add(new AnchoredLspMarker(marker));
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
        foreach (var segment in _markers)
        {
            var marker = segment.Marker;
            if (!marker.IsError && marker.Tags is not { Length: > 0 })
                continue;

            int start = Math.Max(marker.StartOffset, line.Offset);
            int end   = Math.Min(marker.EndOffset,   line.EndOffset);

            if (start >= end)
                continue;

            ChangeLinePart(start, end, element =>
            {
                var properties = element.TextRunProperties;
                if (marker.IsError) properties.SetForegroundBrush(Brushes.Red);
                if (marker.Tags?.Contains(1) == true && properties.ForegroundBrush is ISolidColorBrush foreground)
                    properties.SetForegroundBrush(new Avalonia.Media.Immutable.ImmutableSolidColorBrush(foreground.Color, foreground.Opacity * 0.5));
                if (marker.Tags?.Contains(2) == true)
                {
                    var decorations = new TextDecorationCollection();
                    if (properties.TextDecorations is { } existing) decorations.AddRange(existing);
                    decorations.AddRange(TextDecorations.Strikethrough);
                    properties.SetTextDecorations(decorations);
                }
            });
        }
    }
}

public readonly record struct LspMarker(int StartOffset, int EndOffset, bool IsError, string Message, string? Source = null,
    int[]? Tags = null, DiagnosticRelatedInformation[]? RelatedInformation = null);

internal sealed class AnchoredLspMarker : TextSegment
{
    private readonly LspMarker _marker;

    public AnchoredLspMarker(LspMarker marker)
    {
        _marker = marker;
        StartOffset = marker.StartOffset;
        EndOffset = marker.EndOffset;
    }

    public LspMarker Marker => _marker with { StartOffset = StartOffset, EndOffset = EndOffset };
}
