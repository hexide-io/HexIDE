using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

public class TreeViewScrollingTests
{
    [AvaloniaFact]
    public void Overflowing_flat_tree_renders_twice_without_invalidating_layout()
    {
        var tree = Tree();
        tree.Nodes.Roots[0].Text = new string('W', 30);
        var window = new Window { Content = tree, Width = 200, Height = 120 };
        window.Show();
        try
        {
            tree.LoadEdge(0, 0);
            for (var i = 0; i < 2; i++)
            {
                tree.InvalidateVisual();
                using var frame = window.CaptureRenderedFrame();
                frame.Should().NotBeNull();
                tree.IsMeasureValid.Should().BeTrue();
                tree.IsArrangeValid.Should().BeTrue();
                Bar(tree, Orientation.Vertical).Bounds.Should().Be(new Rect(143, 0, 17, 63));
                Bar(tree, Orientation.Horizontal).Bounds.Should().Be(new Rect(0, 63, 143, 17));
            }
        }
        finally { window.Close(); }
    }

    private static VBTreeView Tree(int count = 30)
    {
        var tree = new VBTreeView { Width = 160, Height = 80 };
        for (var i = 1; i <= count; i++) tree.Nodes.Add(key: $"k{i:00}", text: $"Node {i:00}");
        tree.Arrange(new Rect(0, 0, 160, 80));
        return tree;
    }

    private static ScrollBar Bar(VBTreeView tree, Orientation orientation) =>
        tree.GetVisualChildren().OfType<ScrollBar>().Single(b => b.Orientation == orientation);

    private static void Key(VBTreeView tree, Key key) =>
        tree.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });

    [AvaloniaFact]
    public void Bars_use_whole_client_rows_and_seventeen_pixels_and_follow_content()
    {
        var tree = Tree(4);
        tree.TryGetProperty("Scroll", out var defaultScroll).Should().BeTrue();
        defaultScroll.Value.Should().Be(true);
        tree.GetVisibleCount().Should().Be(4);
        Bar(tree, Orientation.Vertical).IsVisible.Should().BeFalse();
        var fifth = tree.Nodes.Add(text: "fifth");
        tree.Arrange(new Rect(0, 0, 160, 80));
        Bar(tree, Orientation.Vertical).IsVisible.Should().BeTrue();
        Bar(tree, Orientation.Vertical).Bounds.Width.Should().Be(17);
        fifth.Text = new string('W', 30);
        tree.GetVisibleCount().Should().Be(3);
        Bar(tree, Orientation.Horizontal).IsVisible.Should().BeTrue();
        tree.Arrange(new Rect(0, 0, 160, 80));
        Bar(tree, Orientation.Horizontal).Bounds.Height.Should().Be(17);
        Bar(tree, Orientation.Horizontal).Bounds.Width.Should().Be(139);
        Bar(tree, Orientation.Vertical).Bounds.Height.Should().Be(59);
        tree.Nodes.SelectedItem = fifth;
        Bar(tree, Orientation.Horizontal).Value.Should().Be(0);
        fifth.Text = "short";
        tree.GetVisibleCount().Should().Be(4);
        Bar(tree, Orientation.Horizontal).IsVisible.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Selection_and_EnsureVisible_scroll_minimally()
    {
        var tree = Tree();
        var rows = tree.Nodes.Roots;
        rows[19].EnsureVisible().Should().BeTrue();
        Bar(tree, Orientation.Vertical).Value.Should().Be(16);
        rows[16].EnsureVisible().Should().BeFalse();
        tree.Nodes.SelectedItem = rows[24];
        Bar(tree, Orientation.Vertical).Value.Should().Be(21);
        rows[1].Selected = true;
        Bar(tree, Orientation.Vertical).Value.Should().Be(1);
    }

    [AvaloniaFact]
    public void Navigation_keeps_selection_in_whole_rows_and_pages_to_the_edge_first()
    {
        var tree = Tree();
        tree.Nodes.SelectedItem = tree.Nodes.Roots[0];
        Key(tree, Avalonia.Input.Key.PageDown);
        tree.Nodes.SelectedItem.Should().BeSameAs(tree.Nodes.Roots[3]);
        Bar(tree, Orientation.Vertical).Value.Should().Be(0);
        Key(tree, Avalonia.Input.Key.Down);
        Bar(tree, Orientation.Vertical).Value.Should().Be(1);
        Key(tree, Avalonia.Input.Key.PageDown);
        tree.Nodes.SelectedItem.Should().BeSameAs(tree.Nodes.Roots[7]);
        Bar(tree, Orientation.Vertical).Value.Should().Be(4);
        Key(tree, Avalonia.Input.Key.End);
        Bar(tree, Orientation.Vertical).Value.Should().Be(26);
        Key(tree, Avalonia.Input.Key.PageUp);
        tree.Nodes.SelectedItem.Should().BeSameAs(tree.Nodes.Roots[26]);
        Key(tree, Avalonia.Input.Key.Up);
        Bar(tree, Orientation.Vertical).Value.Should().Be(25);
        Key(tree, Avalonia.Input.Key.Home);
        Key(tree, Avalonia.Input.Key.Up);
        tree.Nodes.SelectedItem.Should().BeSameAs(tree.Nodes.Roots[0]);
        Bar(tree, Orientation.Vertical).Value.Should().Be(0);
    }

    [AvaloniaFact]
    public void Scroll_false_disables_bars_selection_scrolling_and_EnsureVisible()
    {
        var tree = Tree();
        tree.TrySetProperty("Scroll", new Vb6Value(false));
        tree.Nodes.SelectedItem = tree.Nodes.Roots[0];
        Key(tree, Avalonia.Input.Key.End);
        tree.Nodes.SelectedItem.Should().BeSameAs(tree.Nodes.Roots[29]);
        tree.Nodes.Roots[19].EnsureVisible().Should().BeFalse();
        tree.GetVisualChildren().OfType<ScrollBar>().Should().OnlyContain(b => !b.IsVisible && b.Value == 0);
        var (_, canvas, _, _) = ContainerRuntimeHarness.Spawn("""
            VERSION 5.00
            Begin VB.Form Form1
               Begin MSComctlLib.TreeView TreeView1
                  Width = 2400
                  Height = 1200
                  Scroll = 0
               End
            End
            Attribute VB_Name = "Form1"
            """);
        var loaded = (VBTreeView)ContainerRuntimeHarness.Child(canvas, "TreeView1");
        loaded.TryGetProperty("Scroll", out var savedScroll).Should().BeTrue();
        savedScroll.Value.Should().Be(false);
    }

    [AvaloniaFact]
    public void Wheel_moves_three_rows_without_selection_and_uses_horizontal_bar_alone()
    {
        var tree = Tree();
        tree.Nodes.SelectedItem = tree.Nodes.Roots[0];
        void Wheel() => tree.RaiseEvent(new PointerWheelEventArgs(tree,
            new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true), tree, default, 0,
            new PointerPointProperties(), KeyModifiers.None, new Vector(0, -1)));
        Wheel();
        Bar(tree, Orientation.Vertical).Value.Should().Be(3);
        tree.Nodes.SelectedItem.Should().BeSameAs(tree.Nodes.Roots[0]);
        for (var i = 0; i < 20; i++) Wheel();
        Bar(tree, Orientation.Vertical).Value.Should().Be(26);
        tree.Nodes.Clear();
        tree.Nodes.Add(text: new string('W', 30));
        Wheel();
        Bar(tree, Orientation.Vertical).IsVisible.Should().BeFalse();
        Bar(tree, Orientation.Horizontal).Value.Should().Be(15);
        tree.TrySetProperty("Scroll", new Vb6Value(false));
        Wheel();
        Bar(tree, Orientation.Horizontal).Value.Should().Be(0);
    }

    [AvaloniaFact]
    public void Selected_label_draws_in_the_last_whole_row_after_scrolling()
    {
        var tree = Tree();
        tree.TrySetProperty("Style", new Vb6Value(0));
        tree.TrySetProperty("HideSelection", new Vb6Value(false));
        tree.Nodes.SelectedItem = tree.Nodes.Roots[19];
        var drawing = new DrawingGroup();
        using (var context = drawing.Open()) tree.Render(context);
        GeometryDrawing? highlight = null;
        void Visit(Drawing item)
        {
            if (item is DrawingGroup group)
                foreach (var child in group.Children) Visit(child);
            else if (item is GeometryDrawing geometry) highlight = geometry;
        }
        Visit(drawing);
        highlight!.GetBounds().Top.Should().Be(50);
        highlight.GetBounds().Height.Should().Be(16);
    }

    [AvaloniaFact]
    public void Pointer_selects_the_row_at_the_scrolled_origin()
    {
        var tree = Tree();
        var window = new Window { Content = tree, Width = 200, Height = 120 };
        window.Show();
        try
        {
            tree.Nodes.Roots[19].EnsureVisible().Should().BeTrue();
            Bar(tree, Orientation.Vertical).Value.Should().Be(16);
            var point = tree.TranslatePoint(new Point(45, 8), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Bar(tree, Orientation.Vertical).Value.Should().Be(16);
            tree.Nodes.SelectedItem!.Key.Should().Be("k17");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Expansion_changes_bars_without_scrolling_and_childless_right_does_nothing()
    {
        var tree = Tree(1);
        var root = tree.Nodes.Roots[0];
        tree.Nodes.SelectedItem = root;
        Key(tree, Avalonia.Input.Key.Right);
        root.Expanded.Should().BeFalse();
        for (var i = 0; i < 8; i++) tree.Nodes.Add(root, 4, text: "child");
        Key(tree, Avalonia.Input.Key.Add);
        Bar(tree, Orientation.Vertical).IsVisible.Should().BeTrue();
        Bar(tree, Orientation.Vertical).Value.Should().Be(0);
        Key(tree, Avalonia.Input.Key.Subtract);
        root.Expanded.Should().BeFalse();
        Bar(tree, Orientation.Vertical).Maximum.Should().Be(0);
        tree.GetVisibleCount().Should().Be(4);
        Bar(tree, Orientation.Vertical).IsVisible.Should().BeFalse();
        root.ChildNodes[7].EnsureVisible().Should().BeTrue();
        Bar(tree, Orientation.Vertical).Value.Should().Be(5);
    }
}
