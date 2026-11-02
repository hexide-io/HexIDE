using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

public sealed partial class VBListView
{
    private bool Flag(string name) => (bool)properties[name].Value!;
    private long Number(string name) => (long)properties[name].Value!;
    private static IBrush SystemBrush(VbSystemColor color) => VBColor.FromSystemColor(color).ToBrush();
    private static object? Read(ICSharpPropertyBag item, string name)
    {
        item.TryGetProperty(name, out var value);
        return value.Value;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;
        var area = new Rect(Bounds.Size);
        using var clip = context.PushClip(area);
        context.FillRectangle(VBColor.FromOle(Number("BackColor")).ToBrush(), area);
        var frame = new Pen(SystemBrush(VbSystemColor.Btnshadow));
        if (Number("BorderStyle") != 0)
            context.DrawRectangle(null, frame, area.Deflate(0.5));
        var content = area.Deflate(VBProps.GetClientInset(this));
        if (content.Width <= 0 || content.Height <= 0)
            return;
        using var contentClip = context.PushClip(content);

        const double rowHeight = 18;
        var report = Number("View") == 3;
        var top = content.Y;
        if (report && !Flag("HideColumnHeaders") && Columns.Entries.Count > 0)
        {
            var x = content.X;
            foreach (var column in Columns.DisplayOrder)
            {
                var cell = new Rect(x, top, column.Width / 15d, rowHeight);
                context.FillRectangle(SystemBrush(VbSystemColor.Btnface), cell);
                context.DrawRectangle(null, frame, cell);
                DrawText(context, column.Text, cell.Deflate(new Thickness(3, 1)), column.Alignment);
                x += cell.Width;
            }
            top += rowHeight;
        }
        if (report && Flag("GridLines"))
        {
            var grid = new Pen(SystemBrush(VbSystemColor.Btnface));
            for (var y = top + rowHeight; y < content.Bottom; y += rowHeight)
                context.DrawLine(grid, new Point(content.X, y), new Point(content.Right, y));
            var x = content.X;
            foreach (var column in Columns.DisplayOrder)
            {
                x += column.Width / 15d;
                context.DrawLine(grid, new Point(x, top), new Point(x, content.Bottom));
            }
        }

        foreach (var (item, cell, row) in ItemCells())
        {
            if (!report)
            {
                DrawItem(context, item, cell, row, Number("View") == 0);
                continue;
            }
            if (!IsEnabled)
            {
                var width = Columns.DisplayOrder.Sum(column => column.Width / 15d);
                context.FillRectangle(SystemBrush(VbSystemColor.Btnface), new Rect(row.X, row.Y, width, row.Height));
            }
            if (Flag("FullRowSelect") && ShowSelection(item))
                context.FillRectangle(SelectionBrush(), row);
            var x = content.X;
            foreach (var column in Columns.DisplayOrder)
            {
                var columnCell = new Rect(x, row.Y, column.Width / 15d, rowHeight);
                var subIndex = Columns.Entries.IndexOf(column);
                if (subIndex == 0)
                    DrawItem(context, item, columnCell, row, false);
                else
                {
                    var sub = item.SubItems.Entries.ElementAtOrDefault(subIndex - 1);
                    DrawText(context, sub?.Text ?? "", columnCell.Deflate(new Thickness(3, 1)), column.Alignment,
                        foreground: Flag("FullRowSelect") && ShowSelection(item) && (IsFocused || !IsEnabled)
                            ? SystemBrush(VbSystemColor.Highlighttext) : null,
                        bold: sub is not null && (bool)Read(sub, "Bold")!);
                }
                x += columnCell.Width;
            }
        }
    }

    private static double TextWidth(string text) => new FormattedText(text, CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight, new Typeface("Arial"), 11, Brushes.Black).Width;

    private bool ShowSelection(ListViewItem item) => item.IsSelected && (IsFocused || !Flag("HideSelection") || !IsEnabled);
    private IBrush SelectionBrush() => SystemBrush(IsFocused || !IsEnabled ? VbSystemColor.Highlight : VbSystemColor.Btnface);

    internal Rect GetItemLabelBounds(int index)
    {
        foreach (var (item, cell, _) in ItemCells())
            if (ReferenceEquals(item, Items.Entries[index]))
                return ItemLabelBounds(item, cell);
        return default;
    }

    private Rect ItemLabelBounds(ListViewItem item, Rect cell)
    {
        var label = ItemLabel(item, cell);
        if (Checkboxes)
            label = new Rect(label.X + 16, label.Y, Math.Max(0, label.Width - 16), label.Height);
        var width = Math.Min(label.Width, TextWidth(item.Caption) + 4);
        return new Rect(Number("View") == 0 ? label.Center.X - width / 2 : label.X, label.Y, width, label.Height);
    }

    private void DrawItem(DrawingContext context, ListViewItem item, Rect cell, Rect row, bool icon)
    {
        if (cell.Width <= 0 || cell.Height <= 0)
            return;
        var label = ItemLabel(item, cell);
        var unwrappedIcon = icon && !Flag("LabelWrap");
        var checkboxLabel = label;
        if (Checkboxes)
            label = new Rect(label.X + 16, label.Y, Math.Max(0, label.Width - 16), label.Height);
        var highlight = !icon && Number("View") == 3 && Flag("FullRowSelect") ? row
            : ItemLabelBounds(item, cell);
        var selected = ShowSelection(item);
        if (selected && !(Number("View") == 3 && Flag("FullRowSelect")))
            context.FillRectangle(SelectionBrush(), highlight);
        if (Checkboxes && item.HasCheckbox)
        {
            using var checkboxClip = context.PushClip(cell);
            var box = new Rect(checkboxLabel.X, checkboxLabel.Y + 1, 12, 12);
            context.DrawRectangle(Brushes.White, new Pen(SystemBrush(VbSystemColor.Btnshadow)), box);
            if ((bool)Read(item, "Checked")!)
            {
                var pen = new Pen(Brushes.Black, 2);
                context.DrawLine(pen, new Point(box.X + 2, box.Y + 6), new Point(box.X + 5, box.Y + 9));
                context.DrawLine(pen, new Point(box.X + 5, box.Y + 9), new Point(box.Right - 2, box.Y + 2));
            }
        }
        var foreground = selected && (IsFocused || !IsEnabled) ? SystemBrush(VbSystemColor.Highlighttext)
            : (long)Read(item, "ForeColor")! == 255 ? Brushes.Red : SystemBrush(VbSystemColor.Windowtext);
        using (context.PushClip(unwrappedIcon ? new Rect(Bounds.Size) : cell))
            DrawText(context, item.Caption, label, icon ? 2 : 0, foreground, (bool)Read(item, "Bold")!, icon && Flag("LabelWrap"));
        if (IsFocused && ReferenceEquals(SelectedItem, item))
            context.DrawRectangle(null, new Pen(SystemBrush(VbSystemColor.Windowtext), 1, new DashStyle([1, 1], 0)), highlight.Deflate(0.5));
    }

    private static void DrawText(DrawingContext context, string text, Rect cell, int alignment = 0,
        IBrush? foreground = null, bool bold = false, bool wrap = false)
    {
        if (cell.Width <= 0 || cell.Height <= 0)
            return;
        using var clip = context.PushClip(cell);
        using var layout = new TextLayout(text, new Typeface("Arial", weight: bold ? FontWeight.Bold : FontWeight.Normal),
            11, foreground ?? SystemBrush(VbSystemColor.Windowtext),
            textAlignment: alignment switch { 1 => TextAlignment.Right, 2 => TextAlignment.Center, _ => TextAlignment.Left },
            textWrapping: wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, textTrimming: TextTrimming.CharacterEllipsis,
            maxWidth: cell.Width, maxHeight: cell.Height);
        layout.Draw(context, cell.TopLeft);
    }
}
