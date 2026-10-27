using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HexIDE.Runtime.Components.InBox;

namespace HexIDE.Runtime.BuiltinControls;

internal sealed partial class VBTreeView
{
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(Brushes.White, null, new Rect(Bounds.Size));
        using var clip = context.PushClip(new Rect(Bounds.Size));
        var row = 0;
        foreach (var root in Nodes.Roots) DrawNode(root, 0);

        void DrawNode(TreeNode node, int depth)
        {
            // Layout sizes are rendering choices, not pinned VB6 pixel metrics.
            const double height = 20;
            var step = indentation * 96 / 2540;
            var x = (depth + 1) * step;
            var y = row++ * height;
            var center = y + height / 2;
            var branch = x - step / 2;
            var pen = new Pen(Brushes.Gray, 1);
            if (depth > 0 || lineStyle == 1)
            {
                if ((style & 4) != 0)
                {
                    context.DrawLine(pen, new Point(branch, y), new Point(branch, center));
                    context.DrawLine(pen, new Point(branch, center), new Point(x, center));
                    if (node.Next is not null)
                        context.DrawLine(pen, new Point(branch, center), new Point(branch, y + SubtreeRows(node) * height));
                }
                if ((style & 2) != 0 && node.ChildNodes.Count > 0)
                {
                    context.DrawRectangle(Brushes.White, pen, new Rect(branch - 4, center - 4, 8, 8));
                    context.DrawLine(pen, new Point(branch - 2, center), new Point(branch + 2, center));
                    if (!node.Expanded)
                        context.DrawLine(pen, new Point(branch, center - 2), new Point(branch, center + 2));
                }
            }
            if (Nodes.Checkboxes)
            {
                context.DrawRectangle(Brushes.White, pen, new Rect(x, center - 6, 12, 12));
                if (node.Checked)
                {
                    context.DrawLine(new Pen(Brushes.Black, 2), new Point(x + 2, center), new Point(x + 5, center + 3));
                    context.DrawLine(new Pen(Brushes.Black, 2), new Point(x + 5, center + 3), new Point(x + 10, center - 3));
                }
                x += 16;
            }
            var selected = node.Selected && (IsFocused || !hideSelection);
            var text = new FormattedText(node.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, node.Bold ? FontWeight.Bold : FontWeight.Normal),
                13, selected && IsFocused ? Brushes.White : Brushes.Black);
            if (selected)
                context.DrawRectangle(IsFocused ? Brushes.Navy : Brushes.LightGray, null,
                    new Rect(x, y, text.Width + 4, height));
            context.DrawText(text, new Point(x + 2, y));
            if (node.Expanded)
                foreach (var child in node.ChildNodes) DrawNode(child, depth + 1);
        }
    }

    private static int SubtreeRows(TreeNode node) => 1 +
        (node.Expanded ? node.ChildNodes.Sum(SubtreeRows) : 0);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsFocusedProperty) InvalidateVisual();
    }
}
