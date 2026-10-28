using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.VisualTree;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

public class TreeViewLabelMouseTests
{
    private sealed class Root : Decorator, IModuleExecutionRoot
    {
        internal readonly List<string> Calls = new();
        internal Action? Before;
        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null) => Calls.Add(name);
        public Task ExecuteEditEventAsync(string name, IList<Vb6Value> args)
        {
            Calls.Add(name);
            if (name.EndsWith("BeforeLabelEdit", StringComparison.Ordinal)) Before?.Invoke();
            return Task.CompletedTask;
        }
    }

    private static TimeSpan DoubleClickTime => Application.Current!.PlatformSettings!.GetDoubleTapTime(PointerType.Mouse);
    private static Task WaitForEdit() => Task.Delay(DoubleClickTime + TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Selected_label_opens_after_release_and_host_double_click_time(bool selectByClick)
    {
        var tree = new VBTreeView { Width = 300, Height = 200 };
        VBProps.SetName(tree, "TreeView1");
        var first = tree.Nodes.Add(text: "Node 1");
        var node = tree.Nodes.Add(text: "Node 2");
        var root = new Root { Child = tree };
        var window = new Window { Content = root };
        window.Show();
        try
        {
            tree.Focus();
            tree.Nodes.SelectedItem = selectByClick ? first : node;
            var point = tree.TranslatePoint(new Point(44, 26), window)!.Value;
            if (selectByClick)
            {
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                await WaitForEdit();
                tree.Nodes.SelectedItem.Should().BeSameAs(node);
                tree.GetVisualDescendants().OfType<LabelEditor>().Should().BeEmpty();
                root.Calls.Should().NotContain("TreeView1_BeforeLabelEdit");
            }
            root.Calls.Clear();
            root.Before = () =>
            {
                var pending = tree.GetVisualDescendants().OfType<LabelEditor>().Single();
                pending.Text.Should().Be("Node 2");
                pending.Width.Should().Be(0);
                pending.Height.Should().Be(0);
                Canvas.GetLeft(pending).Should().Be(0);
                Canvas.GetTop(pending).Should().Be(0);
            };
            window.MouseDown(point, MouseButton.Left);
            await WaitForEdit();
            tree.GetVisualDescendants().OfType<LabelEditor>().Should().BeEmpty();
            window.MouseUp(point, MouseButton.Left);
            root.Calls.Should().Equal("TreeView1_MouseDown", "TreeView1_NodeClick", "TreeView1_MouseUp", "TreeView1_Click");
            await Task.Delay(DoubleClickTime / 2, TestContext.Current.CancellationToken);
            tree.GetVisualDescendants().OfType<LabelEditor>().Should().BeEmpty();
            await WaitForEdit();
            var editor = tree.GetVisualDescendants().OfType<LabelEditor>().Single();
            root.Calls.Last().Should().Be("TreeView1_BeforeLabelEdit");
            Canvas.GetLeft(editor).Should().BeApproximately(tree.ClientEdge + 1000 * 96.0 / 2540 - 2, 0.01);
            Canvas.GetTop(editor).Should().Be(tree.ClientEdge + 16 - 1);
            editor.Height.Should().Be(17);
            editor.BorderThickness.Should().Be(new Thickness(1));
            editor.BorderBrush.Should().Be(Brushes.Black);
            editor.SelectionStart.Should().Be(0);
            editor.SelectionEnd.Should().Be(6);
            window.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.A, null);
            window.KeyTextInput("abc");
            editor.Height.Should().Be(21);
            Canvas.GetTop(editor).Should().Be(tree.ClientEdge + 16 - 3);
            var width = editor.Width;
            window.KeyTextInput(new string('x', 30));
            editor.Width.Should().BeGreaterThan(width);
            node.Text.Should().Be("Node 2");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Double_click_does_not_start_an_edit(int mode)
    {
        var tree = new VBTreeView { Width = 300, Height = 200 };
        VBProps.SetName(tree, "TreeView1");
        tree.TrySetProperty("LabelEdit", new Vb6Value(mode));
        tree.Nodes.Add(text: "Node 2");
        var root = new Root { Child = tree };
        var window = new Window { Content = root };
        window.Show();
        try
        {
            tree.Focus();
            root.Calls.Clear();
            var point = tree.TranslatePoint(new Point(44, 10), window)!.Value;
            // Use gesture timestamps, independently of build/render time, as TreeViewEventTests does.
            var platform = window.PlatformImpl!;
            var property = platform.GetType().GetProperty("Input")!;
            var input = (Action<RawInputEventArgs>)property.GetValue(platform)!;
            var timestampProperty = typeof(RawInputEventArgs).GetProperty("Timestamp")!;
            ulong timestamp = 100;
            property.SetValue(platform, (Action<RawInputEventArgs>)(e =>
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
            await WaitForEdit();
            root.Calls.Should().Equal("TreeView1_MouseDown", "TreeView1_NodeClick", "TreeView1_MouseUp", "TreeView1_Click",
                "TreeView1_Expand", "TreeView1_DblClick", "TreeView1_MouseUp");
            tree.GetVisualDescendants().OfType<LabelEditor>().Should().BeEmpty();
        }
        finally { window.Close(); }
    }
}
