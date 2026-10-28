using System.Globalization;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using HexIDE.Runtime.Components.InBox;

namespace HexIDE.Runtime.BuiltinControls;

internal sealed partial class VBTreeView
{
    private const double BarSize = 17;
    private bool scroll = true;
    private readonly ScrollBar vertical = new() { Orientation = Orientation.Vertical, Width = BarSize, SmallChange = 1 };
    private readonly ScrollBar horizontal = new() { Orientation = Orientation.Horizontal, Height = BarSize, SmallChange = 15 };
    private double ClientWidth { get; set; }
    private double ClientHeight { get; set; }
    private int WholeRows => (int)(ClientHeight / RowHeight);

    private void InitializeScrollBars()
    {
        VisualChildren.Add(vertical);
        LogicalChildren.Add(vertical);
        VisualChildren.Add(horizontal);
        LogicalChildren.Add(horizontal);
        vertical.ValueChanged += (_, _) => InvalidateVisual();
        horizontal.ValueChanged += (_, _) => InvalidateVisual();
    }

    private void UpdateScroll()
    {
        var width = Math.Max(0, (double.IsFinite(Width) ? Width : Bounds.Width) - 2 * ClientEdge);
        var height = Math.Max(0, (double.IsFinite(Height) ? Height : Bounds.Height) - 2 * ClientEdge);
        var rows = VisibleNodes().ToArray();
        var extent = 0.0;
        foreach (var (node, depth) in rows)
        {
            var text = new FormattedText(node.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, node.Bold ? FontWeight.Bold : FontWeight.Normal),
                13, Brushes.Black);
            extent = Math.Max(extent, (depth + 1) * indentation * 96 / 2540 + (Nodes.Checkboxes ? 16 : 0) + text.Width + 4);
        }
        var v = false;
        var h = false;
        for (var pass = 0; pass < 3; pass++)
        {
            v = scroll && rows.Length > (int)(Math.Max(0, height - (h ? BarSize : 0)) / RowHeight);
            h = scroll && extent > Math.Max(0, width - (v ? BarSize : 0));
        }
        ClientWidth = Math.Max(0, width - (v ? BarSize : 0));
        ClientHeight = Math.Max(0, height - (h ? BarSize : 0));
        vertical.IsVisible = v;
        horizontal.IsVisible = h;
        vertical.Maximum = v ? Math.Max(0, rows.Length - WholeRows) : 0;
        horizontal.Maximum = h ? Math.Max(0, extent - ClientWidth) : 0;
        vertical.ViewportSize = WholeRows;
        vertical.LargeChange = Math.Max(1, WholeRows - 1);
        horizontal.ViewportSize = ClientWidth;
        vertical.Value = Math.Clamp(vertical.Value, 0, vertical.Maximum);
        horizontal.Value = Math.Clamp(horizontal.Value, 0, horizontal.Maximum);
    }

    private bool EnsureNodeVisible(TreeNode node)
    {
        UpdateScroll();
        if (!scroll || WholeRows == 0) return false;
        var index = VisibleNodes().Select(r => r.Node).ToList().IndexOf(node);
        if (index < 0) return false;
        var old = vertical.Value;
        if (index < old) vertical.Value = index;
        else if (index >= old + WholeRows) vertical.Value = index - WholeRows + 1;
        return vertical.Value != old;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        UpdateScroll();
        vertical.Arrange(new Rect(ClientEdge + ClientWidth, ClientEdge, BarSize, ClientHeight));
        horizontal.Arrange(new Rect(ClientEdge, ClientEdge + ClientHeight, ClientWidth, BarSize));
        return finalSize;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        UpdateScroll();
        if (!scroll) return;
        if (vertical.IsVisible) vertical.Value = Math.Clamp(vertical.Value - e.Delta.Y * 3, 0, vertical.Maximum);
        else if (horizontal.IsVisible) horizontal.Value = Math.Clamp(horizontal.Value - e.Delta.Y * 15, 0, horizontal.Maximum);
        e.Handled = true;
    }
}
