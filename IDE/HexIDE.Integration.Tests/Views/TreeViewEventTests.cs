using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

public class TreeViewEventTests
{
    private static (VBTreeView tree, ContainerRuntimeHarness.RecordingRoot root, Window window) Create()
    {
        var tree = new VBTreeView { Width = 300, Height = 200 };
        VBProps.SetName(tree, "TreeView1");
        var root = new ContainerRuntimeHarness.RecordingRoot { Child = tree };
        var window = new Window { Content = root, Width = 300, Height = 200 };
        window.Show();
        window.FocusManager!.Focus(null);
        root.Calls.Clear();
        return (tree, root, window);
    }

    private static string[] Names(ContainerRuntimeHarness.RecordingRoot root) =>
        root.Calls.Select(c => c.Name.Replace("TreeView1_", "")).ToArray();

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Second_row_hit_testing_uses_sixteen_pixel_rows(bool button)
    {
        var (tree, root, window) = Create();
        try
        {
            var first = tree.Nodes.Add(text: "first");
            var second = tree.Nodes.Add(text: "second");
            tree.Nodes.Add(second, 4, text: "child");
            tree.Nodes.SelectedItem = first;
            tree.TrySetProperty("LineStyle", new Vb6Value(1));
            var point = tree.TranslatePoint(new Point(button ? 19 : 42, button ? 26 : 19), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            if (button)
            {
                second.Expanded.Should().BeTrue();
                tree.Nodes.SelectedItem.Should().BeSameAs(first);
            }
            else tree.Nodes.SelectedItem.Should().BeSameAs(second);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Edge_is_excluded_and_rows_start_inside_the_client()
    {
        var (tree, root, window) = Create();
        try
        {
            var first = tree.Nodes.Add(text: "first");
            var second = tree.Nodes.Add(text: "second");
            tree.Nodes.SelectedItem = second;
            tree.Focus();
            root.Calls.Clear();
            var edge = tree.TranslatePoint(new Point(44, 1), window)!.Value;
            window.MouseDown(edge, MouseButton.Right);
            window.MouseUp(edge, MouseButton.Right);
            tree.Nodes.SelectedItem.Should().BeSameAs(second);
            Names(root).Should().NotContain("NodeClick");
            var inside = tree.TranslatePoint(new Point(44, 17), window)!.Value;
            window.MouseDown(inside, MouseButton.Right);
            window.MouseUp(inside, MouseButton.Right);
            tree.Nodes.SelectedItem.Should().BeSameAs(first);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, 21, 21, 285f, 285f)]
    [InlineData(false, 40, 30, 570f, 420f)]
    [InlineData(true, 21, 21, 315f, 315f)]
    [InlineData(true, 40, 30, 600f, 450f)]
    public void Mouse_coordinates_start_inside_the_client_edge(bool flat, int x, int y, float expectedX, float expectedY)
    {
        var (tree, root, window) = Create();
        try
        {
            if (flat) tree.LoadEdge(0, 0);
            var point = tree.TranslatePoint(new Point(x, y), window)!.Value;
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            foreach (var name in new[] { "MouseMove", "MouseDown", "MouseUp" })
            {
                var calls = root.Calls.Where(c => c.Name == "TreeView1_" + name).ToArray();
                calls.Should().NotBeEmpty();
                foreach (var call in calls)
                {
                    call.Args![2].Value.Should().Be(expectedX);
                    call.Args[3].Value.Should().Be(expectedY);
                }
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Code_expansion_and_ensure_visible_raise_only_expansion_events()
    {
        var (tree, root, window) = Create();
        try
        {
            var a = tree.Nodes.Add(text: "a");
            var b = tree.Nodes.Add(a, 4, text: "b");
            var c = tree.Nodes.Add(b, 4, text: "c");
            c.EnsureVisible().Should().BeFalse();
            c.EnsureVisible().Should().BeFalse();
            tree.Nodes.SelectedItem = c;
            a.Expanded = false;
            tree.Nodes.SelectedItem.Should().BeSameAs(a);
            c.Selected = false;
            c.Checked = true;
            c.Text = "new";
            tree.Nodes.Sorted = true;
            tree.Nodes.Remove(3);
            tree.Nodes.Clear();
            Names(root).Should().Equal("Expand", "Expand", "Collapse");
            var opened = root.Calls.Take(2).Select(c => c.Args!.Single().Value).ToArray();
            opened.Any(n => ReferenceEquals(n, a)).Should().BeTrue();
            opened.Any(n => ReferenceEquals(n, b)).Should().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Button_click_changes_expansion_without_node_click(bool collapse)
    {
        var (tree, root, window) = Create();
        try
        {
            var a = tree.Nodes.Add(text: "a");
            var child = tree.Nodes.Add(a, 4, text: "child");
            a.Expanded = collapse;
            var other = tree.Nodes.Add(text: "other");
            tree.Nodes.SelectedItem = collapse ? child : other;
            tree.TrySetProperty("LineStyle", new Vb6Value(1));
            root.Calls.Clear();
            var point = tree.TranslatePoint(new Point(19, 10), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Names(root).Where(n => n != "GotFocus").Should().Equal("MouseDown", collapse ? "Collapse" : "Expand", "MouseUp", "Click");
            tree.Nodes.SelectedItem.Should().BeSameAs(collapse ? a : other);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Checkbox_click_checks_without_moving_selection()
    {
        var (tree, root, window) = Create();
        try
        {
            var a = tree.Nodes.Add(text: "a");
            var b = tree.Nodes.Add(text: "b");
            tree.Nodes.SelectedItem = b;
            tree.Nodes.Checkboxes = true;
            var point = tree.TranslatePoint(new Point(42, 10), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Names(root).Where(n => n != "GotFocus").Should().Equal("MouseDown", "NodeCheck", "MouseUp", "Click");
            a.Checked.Should().BeTrue();
            tree.Nodes.SelectedItem.Should().BeSameAs(b);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Right_click_selects_and_raises_node_click_each_time()
    {
        var (tree, root, window) = Create();
        try
        {
            var a = tree.Nodes.Add(text: "a long label");
            var point = tree.TranslatePoint(new Point(42, 10), window)!.Value;
            window.MouseDown(point, MouseButton.Right);
            window.MouseUp(point, MouseButton.Right);
            Names(root).Where(n => n != "GotFocus").Should().Equal("MouseDown", "NodeClick", "MouseUp", "Click");
            tree.Nodes.SelectedItem.Should().BeSameAs(a);
            var down = root.Calls.First(c => c.Name.EndsWith("MouseDown")).Args!;
            down[0].Value.Should().Be(2);
            down[2].Value.Should().Be(600f);
            down[3].Value.Should().Be(120f);
            root.Calls.Clear();
            point += new Vector(30, 0);
            window.MouseDown(point, MouseButton.Right);
            window.MouseUp(point, MouseButton.Right);
            Names(root).Should().Equal("MouseDown", "NodeClick", "MouseUp", "Click");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Focus_selects_first_node_but_add_while_focused_does_not()
    {
        var (tree, root, window) = Create();
        try
        {
            var a = tree.Nodes.Add(text: "a");
            tree.Focus();
            tree.Nodes.SelectedItem.Should().BeSameAs(a);
            tree.Nodes.Clear();
            tree.Nodes.Add(text: "b");
            tree.Nodes.SelectedItem.Should().BeNull();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Enter_expands_before_key_down_and_space_checks_without_node_click()
    {
        var (tree, root, window) = Create();
        try
        {
            var a = tree.Nodes.Add(text: "a");
            tree.Nodes.Add(a, 4, text: "child");
            tree.Nodes.SelectedItem = a;
            tree.Focus();
            root.Calls.Clear();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Names(root).Should().Equal("Expand", "KeyDown", "KeyUp");
            root.Calls.Clear();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Names(root).Should().Equal("Collapse", "KeyDown", "KeyUp");
            tree.Nodes.Checkboxes = true;
            root.Calls.Clear();
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            Names(root).Should().Equal("KeyDown", "NodeCheck", "KeyUp");
            a.Checked.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Single_selection_collapses_old_except_an_ancestor_then_expands_new_then_clicks(bool child)
    {
        var (tree, root, window) = Create();
        try
        {
            tree.TrySetProperty("SingleSel", new Vb6Value(true));
            var b = tree.Nodes.Add(text: "b");
            var a = child ? tree.Nodes.Add(b, 4, text: "a") : tree.Nodes.Add(text: "a");
            b.Expanded = true;
            tree.Nodes.SelectedItem = b;
            root.Calls.Clear();
            var point = tree.TranslatePoint(new Point(child ? 80 : 42, 30), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Names(root).Where(n => n != "GotFocus").Should().Equal(child
                ? new[] { "MouseDown", "Expand", "NodeClick", "MouseUp", "Click" }
                : new[] { "MouseDown", "Collapse", "Expand", "NodeClick", "MouseUp", "Click" });
            b.Expanded.Should().Be(child);
            a.Expanded.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(Key.Home, "NodeClick", false, false)]
    [InlineData(Key.Down, "NodeClick", true, false)]
    [InlineData(Key.Right, "Expand", false, false)]
    [InlineData(Key.Right, "NodeClick", true, false)]
    [InlineData(Key.Left, "Collapse", true, false)]
    [InlineData(Key.Left, "NodeClick", true, true)]
    [InlineData(Key.Multiply, "Expand", false, false)]
    [InlineData(Key.Subtract, "Collapse", true, false)]
    [InlineData(Key.Q, null, false, false)]
    [InlineData(Key.A, null, false, false)]
    [InlineData(Key.R, null, false, false)]
    public void Navigation_raises_key_down_then_the_node_event(Key key, string? expected, bool expanded, bool selectChild)
    {
        var (tree, root, window) = Create();
        try
        {
            var a = tree.Nodes.Add(text: "a");
            var b = tree.Nodes.Add(a, 4, text: "b");
            a.Expanded = expanded;
            tree.Nodes.SelectedItem = selectChild ? b : a;
            tree.Focus();
            root.Calls.Clear();
            window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
            Names(root).Should().Equal(expected is null
                ? new[] { "KeyDown", "KeyUp" } : new[] { "KeyDown", expected, "KeyUp" });
            if (expected == "NodeClick")
                tree.Nodes.SelectedItem.Should().BeSameAs(key is Key.Down or Key.Right ? b : a);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Double_click_toggles_before_double_click_event_without_second_mouse_down()
    {
        var (tree, root, window) = Create();
        try
        {
            var a = tree.Nodes.Add(text: "a");
            tree.Nodes.Add(a, 4, text: "child");
            tree.Nodes.SelectedItem = a;
            tree.Focus();
            root.Calls.Clear();
            var point = tree.TranslatePoint(new Point(42, 10), window)!.Value;
            // Control gesture time, as MouseEventTests does, so render/JIT time cannot split the double click.
            var platform = window.PlatformImpl!;
            var property = platform.GetType().GetProperty("Input")!;
            var input = (System.Action<RawInputEventArgs>)property.GetValue(platform)!;
            var timestampProperty = typeof(RawInputEventArgs).GetProperty("Timestamp")!;
            ulong timestamp = 100;
            property.SetValue(platform, (System.Action<RawInputEventArgs>)(e =>
            {
                if (e is RawPointerEventArgs) timestampProperty.SetValue(e, timestamp);
                input(e);
            }));
            try
            {
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                timestamp += 100;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
            }
            finally { property.SetValue(platform, input); }
            Names(root).Should().Equal("MouseDown", "NodeClick", "MouseUp", "Click", "Expand", "DblClick", "MouseUp");
            a.Expanded.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Empty_area_click_raises_only_mouse_and_click_events()
    {
        var (tree, root, window) = Create();
        try
        {
            var a = tree.Nodes.Add(text: "a");
            tree.Nodes.SelectedItem = a;
            var point = tree.TranslatePoint(new Point(100, 100), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Names(root).Where(n => n != "GotFocus").Should().Equal("MouseDown", "MouseUp", "Click");
            tree.Nodes.SelectedItem.Should().BeSameAs(a);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Single_selection_loads_from_carried_properties(bool enabled)
    {
        var (_, canvas, _, _) = ContainerRuntimeHarness.Spawn($"""
            VERSION 5.00
            Begin VB.Form Form1
               Begin MSComctlLib.TreeView TreeView1
                  SingleSel = {(enabled ? -1 : 0)}
               End
            End
            Attribute VB_Name = "Form1"
            """);
        var tree = (VBTreeView)ContainerRuntimeHarness.Child(canvas, "TreeView1");
        tree.TryGetProperty("SingleSel", out var value).Should().BeTrue();
        value.Value.Should().Be(enabled);
    }
}
