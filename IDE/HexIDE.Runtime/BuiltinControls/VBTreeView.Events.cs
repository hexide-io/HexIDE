using System.Globalization;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.LogicalTree;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

internal sealed partial class VBTreeView
{
    static VBTreeView()
    {
        // Expansion precedes DblClick, so the control raises it after toggling the node.
        AttachedEvents.AttachMouse<VBTreeView>(AttachedEvents.DblClick.InPlaceOfMouseDown, raiseDoubleClick: false);
        AttachedEvents.AttachClick<VBTreeView>();
        AttachedEvents.AttachFocusEvents<VBTreeView>();
    }

    private void Raise(string name, params Vb6Value[] arguments)
    {
        foreach (var handler in ControlEvents.Handlers(this, this.FindLogicalAncestorOfType<IModuleExecutionRoot>(), name))
        {
            var args = new List<Vb6Value>();
            if (handler.Own && VBProps.GetIndex(this) is { } index) args.Add(new(index));
            args.AddRange(arguments);
            handler.Root.ExecuteSub(handler.Name, args);
        }
    }

    private void SelectFromInput(TreeNode node, bool labelClick = false)
    {
        if (singleSel && labelClick)
        {
            var old = Nodes.SelectedItem;
            var ancestor = false;
            for (var parent = node.ParentNode; parent is not null; parent = parent.ParentNode)
                if (ReferenceEquals(parent, old)) ancestor = true;
            if (old is not null && !ReferenceEquals(old, node) && !ancestor && old.Expanded)
                old.Expanded = false;
            if (!node.Expanded) node.Expanded = true;
        }
        Nodes.SelectedItem = node;
        Raise("NodeClick", new Vb6Value(node));
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        if (Nodes.SelectedItem is null && Nodes.Roots.FirstOrDefault() is { } first)
        {
            var top = vertical.Value;
            Nodes.SelectedItem = first;
            vertical.Value = top;
        }
    }

    private IEnumerable<(TreeNode Node, int Depth)> VisibleNodes()
    {
        foreach (var root in Nodes.Roots)
            foreach (var row in Descend(root, 0)) yield return row;

        static IEnumerable<(TreeNode, int)> Descend(TreeNode node, int depth)
        {
            yield return (node, depth);
            if (node.Expanded)
                foreach (var child in node.ChildNodes)
                    foreach (var row in Descend(child, depth + 1)) yield return row;
        }
    }

    private void HandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, this)) return;
        UpdateScroll();
        var point = e.GetCurrentPoint(this);
        if (point.Properties.PointerUpdateKind is not (PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.RightButtonPressed)) return;
        Focus();
        e.Pointer.Capture(this);
        var secondPress = e.ClickCount % 2 == 0;
        var position = point.Position - new Avalonia.Vector(ClientEdge, ClientEdge);
        if (position.X < 0 || position.Y < 0 || position.X >= ClientWidth
            || position.Y >= ClientHeight) return;
        var screenRow = (int)(position.Y / RowHeight);
        var row = screenRow + (int)vertical.Value;
        position += new Avalonia.Vector(horizontal.Value, 0);
        var rows = VisibleNodes().ToArray();
        if (row >= rows.Length) return;
        var (node, depth) = rows[row];
        var step = indentation * 96 / 2540;
        var x = (depth + 1) * step;
        var button = x - step / 2;
        var left = point.Properties.IsLeftButtonPressed;
        if (left && (depth > 0 || lineStyle == 1) && (style & 2) != 0 && node.ChildNodes.Count > 0
            && Math.Abs(position.X - button) <= 4 && Math.Abs(position.Y - ((screenRow + 0.5) * RowHeight)) <= 4)
        {
            node.Expanded = !node.Expanded;
            return;
        }
        if (left && Nodes.Checkboxes && position.X >= x && position.X <= x + 12)
        {
            node.Checked = !node.Checked;
            Raise("NodeCheck", new Vb6Value(node));
            return;
        }
        if (Nodes.Checkboxes) x += 16;
        var text = new FormattedText(node.Text, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal,
                node.Bold ? FontWeight.Bold : FontWeight.Normal), 13, Brushes.Black);
        if (position.X < x || position.X > x + text.Width + 4) return;
        if (secondPress && left)
        {
            node.Expanded = !node.Expanded;
            this.ExecuteSub(ComponentBaseClass.DblClickEvent);
        }
        else SelectFromInput(node, labelClick: true);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var node = Nodes.SelectedItem;
        if (e.Key == Key.Enter && node is not null) node.Expanded = !node.Expanded;
        RaiseKey("KeyDown", e);
        if (node is null) return;
        switch (e.Key)
        {
            case Key.Home:
                if (Nodes.Roots.FirstOrDefault() is { } first) SelectFromInput(first);
                break;
            case Key.Up:
            case Key.End:
            case Key.PageDown:
            case Key.PageUp:
            case Key.Down:
                var visible = VisibleNodes().Select(r => r.Node).ToList();
                var at = visible.IndexOf(node);
                var page = Math.Max(0, (int)GetVisibleCount() - 1);
                var target = e.Key switch
                {
                    Key.Up => at - 1,
                    Key.End => visible.Count - 1,
                    Key.PageDown => at < vertical.Value + page ? (int)vertical.Value + page : at + page,
                    Key.PageUp => at > vertical.Value ? (int)vertical.Value : at - page,
                    _ => at + 1,
                };
                target = Math.Clamp(target, 0, visible.Count - 1);
                if (at >= 0 && target != at) SelectFromInput(visible[target]);
                break;
            case Key.Right:
                if (node.ChildNodes.Count == 0) break;
                if (!node.Expanded) node.Expanded = true;
                else if (node.Child is { } child) SelectFromInput(child);
                break;
            case Key.Left:
                if (node.Expanded) node.Expanded = false;
                else if (node.Parent is { } parent) SelectFromInput(parent);
                break;
            case Key.Add:
            case Key.Multiply: node.Expanded = true; break;
            case Key.Subtract: node.Expanded = false; break;
            case Key.Space when Nodes.Checkboxes:
                node.Checked = !node.Checked;
                Raise("NodeCheck", new Vb6Value(node));
                break;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        RaiseKey("KeyUp", e);
    }

    private void RaiseKey(string name, KeyEventArgs e)
    {
        // The measured keys, using VB's KeyCodeConstants. Other keys keep their existing behaviour.
        var code = e.Key switch
        {
            Key.Enter => 13, Key.Space => 32, Key.Home => 36, Key.Left => 37, Key.Right => 39,
            Key.Down => 40, Key.Multiply => 106, Key.Subtract => 109,
            Key.A => 65, Key.Q => 81, Key.R => 82,
            _ => -1,
        };
        if (code < 0) return;
        var shift = (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 1 : 0)
            | (e.KeyModifiers.HasFlag(KeyModifiers.Control) ? 2 : 0)
            | (e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? 4 : 0);
        Raise(name, new((short)code), new((short)shift));
    }
}
