using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

public class TreeViewDrawingTests
{
    private static List<Drawing> Draw(VBTreeView tree)
    {
        tree.Arrange(new Rect(0, 0, 300, 200));
        var drawing = new DrawingGroup();
        using (var context = drawing.Open()) tree.Render(context);
        return Leaves(drawing).ToList();
    }

    private static IEnumerable<Drawing> Leaves(Drawing drawing)
    {
        if (drawing is DrawingGroup group && group.Transform is null)
        {
            foreach (var child in group.Children)
                foreach (var leaf in Leaves(child)) yield return leaf;
        }
        else yield return drawing;
    }

    [AvaloniaTheory]
    [InlineData(1, 0, 2)]
    [InlineData(1, 1, 3)]
    [InlineData(0, 0, 0)]
    [InlineData(0, 1, 1)]
    public void Edge_insets_the_drawing(int appearance, int border, int edge)
    {
        var (_, canvas, _, _) = ContainerRuntimeHarness.Spawn($"""
            VERSION 5.00
            Begin VB.Form Form1
               Begin MSComctlLib.TreeView TreeView1
                  Width = 2400
                  Height = 1200
                  Appearance = {appearance}
                  BorderStyle = {border}
                  Style = 0
                  HideSelection = 0
               End
            End
            Attribute VB_Name = "Form1"
            """);
        var tree = (VBTreeView)ContainerRuntimeHarness.Child(canvas, "TreeView1");
        tree.Nodes.SelectedItem = tree.Nodes.Add(text: "selected");
        var drawings = Draw(tree).OfType<GeometryDrawing>().ToArray();
        var highlight = drawings.Last().GetBounds();
        highlight.Top.Should().Be(edge);
        highlight.Left.Should().BeApproximately(1000 * 96.0 / 2540 + edge, 0.001);
        drawings.Length.Should().Be(2 + edge * 4);
    }

    [AvaloniaFact]
    public void Rows_start_at_the_client_top_and_are_sixteen_pixels_high()
    {
        var tree = new VBTreeView();
        tree.TrySetProperty("Style", new Vb6Value(0));
        tree.TrySetProperty("HideSelection", new Vb6Value(false));
        var first = tree.Nodes.Add(text: "first");
        var second = tree.Nodes.Add(text: "second");
        tree.Nodes.SelectedItem = first;
        var firstHighlight = Draw(tree).OfType<GeometryDrawing>().Last().GetBounds();
        firstHighlight.Top.Should().Be(2);
        firstHighlight.Height.Should().Be(16);
        tree.Nodes.SelectedItem = second;
        var secondHighlight = Draw(tree).OfType<GeometryDrawing>().Last().GetBounds();
        secondHighlight.Top.Should().Be(18);
        secondHighlight.Height.Should().Be(16);
    }

    [AvaloniaFact]
    public void Visible_labels_follow_expansion_and_model_changes()
    {
        var tree = new VBTreeView();
        tree.TrySetProperty("Style", new Vb6Value(0));
        var root = tree.Nodes.Add(text: "root");
        var collapsed = Draw(tree).Count;
        tree.Nodes.Add(root, 4, text: "child");
        Draw(tree).Count.Should().Be(collapsed);
        root.Expanded = true;
        Draw(tree).Count.Should().BeGreaterThan(collapsed);
        var label = Draw(tree)[^2].GetBounds();
        root.Text = "a much longer root label";
        Draw(tree)[^2].GetBounds().Should().NotBe(label);
        tree.Nodes.Clear();
        Draw(tree).Count.Should().BeLessThan(collapsed);
    }

    [AvaloniaTheory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void Styles_add_buttons_and_lines_only_at_the_requested_levels(int style)
    {
        var tree = new VBTreeView();
        var root = tree.Nodes.Add(text: "root");
        tree.Nodes.Add(root, 4, text: "child");
        tree.TrySetProperty("Style", new Vb6Value(style));
        var withoutRootLines = Draw(tree).Count;
        tree.TrySetProperty("LineStyle", new Vb6Value(1));
        Draw(tree).Count.Should().BeGreaterThan(withoutRootLines);
        tree.TrySetProperty("Style", new Vb6Value(0));
        Draw(tree).Count.Should().BeLessThan(withoutRootLines + 1);
    }

    [AvaloniaFact]
    public void Checkboxes_and_unfocused_selection_are_drawn()
    {
        var tree = new VBTreeView();
        var root = tree.Nodes.Add(text: "root");
        var plain = Draw(tree).Count;
        tree.Nodes.Checkboxes = true;
        var box = Draw(tree).Count;
        box.Should().BeGreaterThan(plain);
        root.Checked = true;
        Draw(tree).Count.Should().BeGreaterThan(box);
        tree.Nodes.SelectedItem = root;
        var hidden = Draw(tree).Count;
        tree.TrySetProperty("HideSelection", new Vb6Value(false));
        Draw(tree).Count.Should().BeGreaterThan(hidden);
    }

    [AvaloniaFact]
    public void Indentation_moves_the_label_and_bold_changes_its_typeface()
    {
        var tree = new VBTreeView();
        var root = tree.Nodes.Add(text: "root");
        tree.TrySetProperty("Style", new Vb6Value(0));
        var before = Draw(tree)[^1].GetBounds();
        tree.TrySetProperty("Indentation", new Vb6Value(1200));
        Draw(tree)[^1].GetBounds().Should().NotBe(before);
        var normal = Draw(tree)[^1].GetBounds();
        root.TrySetProperty("Bold", new Vb6Value(true));
        Draw(tree)[^1].GetBounds().Should().NotBe(normal);
    }
}
