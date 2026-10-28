using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Runtime.BuiltinControls;

public sealed partial class VBMSFlexGrid
{
    private readonly ScrollBar vertical = new() { Orientation = Orientation.Vertical, SmallChange = 1, AllowAutoHide = false };
    private readonly ScrollBar horizontal = new() { Orientation = Orientation.Horizontal, SmallChange = 1, AllowAutoHide = false };
    private bool updating;
    private bool revealCurrent;
    private Size viewport;
    private const double BarSize = 16;

    private void InitializeDrawing()
    {
        ClipToBounds = true;
        VisualChildren.Add(vertical);
        LogicalChildren.Add(vertical);
        VisualChildren.Add(horizontal);
        LogicalChildren.Add(horizontal);
        vertical.ValueChanged += (_, _) => { if (!updating) SetFirstCell((int)vertical.Value, Model.LeftCol); };
        horizontal.ValueChanged += (_, _) => { if (!updating) SetFirstCell(Model.TopRow, (int)horizontal.Value); };
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        InvalidateVisual();
    }

    private double RowSize(int row) => Model.GetRowHeight(row) / 15.0;
    private double ColSize(int col) => Model.GetColWidth(col) / 15.0;
    private double FixedHeight => Enumerable.Range(0, Model.FixedRows).Sum(RowSize);
    private double FixedWidth => Enumerable.Range(0, Model.FixedCols).Sum(ColSize);

    private static int LastPage(int count, int fixedCount, double room, Func<int, double> size)
    {
        var first = count - 1;
        var used = first >= fixedCount ? size(first) : 0;
        while (first > fixedCount && used + size(first - 1) <= room) used += size(--first);
        return Math.Max(fixedCount, first);
    }

    internal void SetFirstCell(int row, int col)
    {
        if (row == 0 && Model.FixedRows == 1)
            throw new VBRunTimeException(new VBStandardError(30009, "Invalid Row Value"));
        if (row < Model.FixedRows || row >= Model.Rows || col < Model.FixedCols || col >= Model.Cols)
            throw new NotImplementedException("MSFlexGrid.TopRow/LeftCol assignment edge is not implemented (#1099)");
        Model.ScrollTo(Math.Min(row, LastPage(Model.Rows, Model.FixedRows, viewport.Height - FixedHeight, RowSize)),
            Math.Min(col, LastPage(Model.Cols, Model.FixedCols, viewport.Width - FixedWidth, ColSize)));
        UpdateBars();
        InvalidateVisual();
    }

    private static int KeepVisible(int current, int first, int fixedCount, double room, Func<int, double> size)
    {
        if (current < fixedCount) return first;
        first = Math.Min(first, current);
        var used = 0.0;
        for (var i = first; i <= current; i++) used += size(i);
        while (first < current && used > room) used -= size(first++);
        return first;
    }

    internal void RefreshView(bool ensureCurrent = false)
    {
        revealCurrent |= ensureCurrent;
        UpdateViewport();
        InvalidateArrange();
        InvalidateVisual();
    }

    private void UpdateViewport()
    {
        if (viewport.Width > 0 && viewport.Height > 0 && Model.Rows > 0 && Model.Cols > 0)
        {
            var row = revealCurrent ? KeepVisible(Model.Row, Math.Max(Model.FixedRows, Math.Min(Model.TopRow, Model.Rows - 1)),
                Model.FixedRows, viewport.Height - FixedHeight, RowSize) : Math.Max(Model.FixedRows, Math.Min(Model.TopRow, Model.Rows - 1));
            var col = revealCurrent ? KeepVisible(Model.Col, Math.Max(Model.FixedCols, Math.Min(Model.LeftCol, Model.Cols - 1)),
                Model.FixedCols, viewport.Width - FixedWidth, ColSize) : Math.Max(Model.FixedCols, Math.Min(Model.LeftCol, Model.Cols - 1));
            Model.ScrollTo(Math.Min(row, LastPage(Model.Rows, Model.FixedRows, viewport.Height - FixedHeight, RowSize)),
                Math.Min(col, LastPage(Model.Cols, Model.FixedCols, viewport.Width - FixedWidth, ColSize)));
            revealCurrent = false;
        }
        UpdateBars();
    }

    private void UpdateBars()
    {
        updating = true;
        try
        {
            vertical.Minimum = Model.FixedRows;
            vertical.Maximum = Math.Max(Model.FixedRows, LastPage(Model.Rows, Model.FixedRows, viewport.Height - FixedHeight, RowSize));
            vertical.ViewportSize = Math.Max(1, Model.Rows - vertical.Maximum);
            vertical.Value = Model.TopRow;
            horizontal.Minimum = Model.FixedCols;
            horizontal.Maximum = Math.Max(Model.FixedCols, LastPage(Model.Cols, Model.FixedCols, viewport.Width - FixedWidth, ColSize));
            horizontal.ViewportSize = Math.Max(1, Model.Cols - horizontal.Maximum);
            horizontal.Value = Model.LeftCol;
        }
        finally { updating = false; }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        vertical.Measure(availableSize);
        horizontal.Measure(availableSize);
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = Enumerable.Range(0, Model.Cols).Sum(ColSize);
        var height = Enumerable.Range(0, Model.Rows).Sum(RowSize);
        var bars = Model.Appearance["scrollbars"];
        var v = false;
        var h = false;
        for (var i = 0; i < 3; i++)
        {
            v = (bars & 2) != 0 && height > finalSize.Height - (h ? BarSize : 0);
            h = (bars & 1) != 0 && width > finalSize.Width - (v ? BarSize : 0);
        }
        viewport = new Size(Math.Max(0, finalSize.Width - (v ? BarSize : 0)), Math.Max(0, finalSize.Height - (h ? BarSize : 0)));
        vertical.Visibility = v ? ScrollBarVisibility.Visible : ScrollBarVisibility.Hidden;
        horizontal.Visibility = h ? ScrollBarVisibility.Visible : ScrollBarVisibility.Hidden;
        vertical.Arrange(new Rect(viewport.Width, 0, v ? BarSize : 0, viewport.Height));
        horizontal.Arrange(new Rect(0, viewport.Height, viewport.Width, h ? BarSize : 0));
        UpdateViewport();
        return finalSize;
    }

    private static IEnumerable<(int Index, double Position, double Size)> Axis(int count, int fixedCount, int first,
        double limit, Func<int, double> size)
    {
        var position = 0.0;
        for (var i = 0; i < count && position < limit; i++)
        {
            if (i >= fixedCount && i < first) continue;
            var extent = size(i);
            if (extent > 0) yield return (i, position, extent);
            position += extent;
        }
    }

    private IEnumerable<(int Row, int Col, Rect Bounds)> VisibleCells()
    {
        foreach (var r in Axis(Model.Rows, Model.FixedRows, Model.TopRow, viewport.Height, RowSize))
            foreach (var c in Axis(Model.Cols, Model.FixedCols, Model.LeftCol, viewport.Width, ColSize))
                yield return (r.Index, c.Index, new Rect(c.Position, r.Position, c.Size, r.Size));
    }

    private IBrush Brush(string name) => VBColor.FromOle(Model.Appearance[name]).ToBrush();
    private IBrush CellBrush(int row, int col, string name, string fallback) =>
        Model.CellFormat(row, col, name) is long colour ? VBColor.FromOle(colour).ToBrush() : Brush(fallback);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brush("backcolorbkg"), new Rect(Bounds.Size));
        using var clip = context.PushClip(new Rect(viewport));
        foreach (var cell in VisibleCells()) DrawCell(context, cell.Row, cell.Col, cell.Bounds);
    }

    private void DrawCell(DrawingContext context, int row, int col, Rect bounds)
    {
        var fixedCell = row < Model.FixedRows || col < Model.FixedCols;
        var current = row == Model.Row && col == Model.Col;
        var active = Model.Appearance["highlight"] != 2 || IsKeyboardFocusWithin;
        var selected = !current && active && Model.Appearance["highlight"] != 0
            && row >= Math.Min(Model.Row, Model.RowSel) && row <= Math.Max(Model.Row, Model.RowSel)
            && col >= Math.Min(Model.Col, Model.ColSel) && col <= Math.Max(Model.Col, Model.ColSel);
        var background = selected ? Brush("backcolorsel") : CellBrush(row, col, "cellbackcolor", fixedCell ? "backcolorfixed" : "backcolor");
        var foreground = selected ? Brush("forecolorsel") : CellBrush(row, col, "cellforecolor", fixedCell ? "forecolorfixed" : "forecolor");
        context.FillRectangle(background, bounds);
        var lines = Model.Appearance[fixedCell ? "gridlinesfixed" : "gridlines"];
        if (lines != 0)
        {
            var pen = new Pen(Brush(fixedCell ? "gridcolorfixed" : "gridcolor"), Model.Appearance["gridlinewidth"]);
            context.DrawRectangle(null, pen, bounds.Deflate(pen.Thickness / 2));
            if (lines is 2 or 3)
            {
                var light = VBColor.FromSystemColor(VbSystemColor.Btnhighlight).ToBrush();
                var dark = VBColor.FromSystemColor(VbSystemColor.Btnshadow).ToBrush();
                context.DrawLine(new Pen(lines == 2 ? dark : light), bounds.TopLeft, bounds.TopRight);
                context.DrawLine(new Pen(lines == 2 ? dark : light), bounds.TopLeft, bounds.BottomLeft);
                context.DrawLine(new Pen(lines == 2 ? light : dark), bounds.BottomLeft, bounds.BottomRight);
                context.DrawLine(new Pen(lines == 2 ? light : dark), bounds.TopRight, bounds.BottomRight);
            }
        }
        var text = Model.GetText(row, col);
        var alignment = Model.CellFormat(row, col, "cellalignment") as int? ?? Model.GetAlignment(col);
        if (alignment == 9) alignment = text.Length > 0 && (text[0] is >= '0' and <= '9' or '-') ? 7 : 1;
        var typeface = new Typeface(new FontFamily(Model.CellFormat(row, col, "cellfontname") as string ?? "MS Sans Serif"),
            Model.CellFormat(row, col, "cellfontitalic") is true ? FontStyle.Italic : FontStyle.Normal,
            Model.CellFormat(row, col, "cellfontbold") is true ? FontWeight.Bold : FontWeight.Normal);
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface,
            (Model.CellFormat(row, col, "cellfontsize") as double? ?? 8.25) * 96 / 72, foreground);
        var area = bounds.Deflate(2);
        if (area.Width > 0 && area.Height > 0)
        {
            if (Model.Appearance["wordwrap"] != 0) formatted.MaxTextWidth = area.Width;
            if (Model.CellFormat(row, col, "cellfontstrikethrough") is true) formatted.SetTextDecorations(TextDecorations.Strikethrough);
            var x = (alignment / 3) switch { 1 => (area.Width - formatted.Width) / 2, 2 => area.Width - formatted.Width, _ => 0 };
            var y = (alignment % 3) switch { 1 => (area.Height - formatted.Height) / 2, 2 => area.Height - formatted.Height, _ => 0 };
            y = Math.Max(0, y);
            if (Model.CellFormat(row, col, "cellfontunderline") is true
                && Model.CellFormat(row, col, "cellfontstrikethrough") is not true)
            {
                var metrics = new TextMetrics(typeface.GlyphTypeface, (Model.CellFormat(row, col, "cellfontsize") as double? ?? 8.25) * 96 / 72);
                // A font's underline can fall below the cell clip even when its glyphs fit.
                var bottom = y + formatted.Baseline + metrics.UnderlinePosition + metrics.UnderlineThickness / 2;
                formatted.SetTextDecorations(new TextDecorationCollection
                {
                    new TextDecoration { Location = TextDecorationLocation.Underline,
                        StrokeOffsetUnit = TextDecorationUnit.Pixel, StrokeOffset = Math.Min(0, area.Height - bottom) }
                });
            }
            using (context.PushClip(area)) context.DrawText(formatted, area.TopLeft + new Vector(Math.Max(0, x), y));
        }
        if (current && active && Model.Appearance["focusrect"] != 0)
            context.DrawRectangle(null, new Pen(foreground, Model.Appearance["focusrect"] == 2 ? 2 : 1,
                Model.Appearance["focusrect"] == 1 ? DashStyle.Dot : null), bounds.Deflate(1));
    }
}
