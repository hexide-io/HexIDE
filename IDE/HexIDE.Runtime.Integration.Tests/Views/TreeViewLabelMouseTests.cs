using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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

namespace HexIDE.Runtime.Integration.Tests.Views;

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
    // The product still supplies the host interval; only the scheduler's clock is controlled (#3143).
    private sealed class EditClock : IDisposable
    {
        private TimeSpan now;
        private Pending? pending;
        private Action? restoreInput;

        public void Attach(Window window)
        {
            var platform = window.PlatformImpl!;
            var property = platform.GetType().GetProperty("Input")!;
            var input = (Action<RawInputEventArgs>)property.GetValue(platform)!;
            var timestampProperty = typeof(RawInputEventArgs).GetProperty("Timestamp")!;
            property.SetValue(platform, (Action<RawInputEventArgs>)(e =>
            {
                if (e is RawPointerEventArgs)
                    timestampProperty.SetValue(e, (ulong)Math.Ceiling(now.TotalMilliseconds) + 100);
                input(e);
            }));
            restoreInput = () => property.SetValue(platform, input);
        }

        public void Dispose() => restoreInput?.Invoke();

        public IDisposable Schedule(Action action, TimeSpan delay)
        {
            pending = new Pending(action, now + delay);
            return pending;
        }

        public void Advance(TimeSpan by)
        {
            now += by;
            if (pending is { Cancelled: false } edit && edit.Due <= now)
            {
                pending = null;
                edit.Action();
            }
            var context = ExecutionContext.Capture();
            try { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
            finally { if (context is not null) ExecutionContext.Restore(context); }
        }

        private sealed class Pending(Action action, TimeSpan due) : IDisposable
        {
            public Action Action { get; } = action;
            public TimeSpan Due { get; } = due;
            public bool Cancelled { get; private set; }
            public void Dispose() => Cancelled = true;
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Selected_label_opens_after_release_and_host_double_click_time(bool selectByClick)
    {
        using var clock = new EditClock();
        var tree = new VBTreeView { Width = 300, Height = 200, ScheduleLabelEdit = clock.Schedule };
        VBProps.SetName(tree, "TreeView1");
        var first = tree.Nodes.Add(text: "Node 1");
        var node = tree.Nodes.Add(text: "Node 2");
        var root = new Root { Child = tree };
        var window = new Window { Content = root };
        window.Show();
        clock.Attach(window);
        try
        {
            tree.Focus();
            tree.Nodes.SelectedItem = selectByClick ? first : node;
            var point = tree.TranslatePoint(new Point(44, 26), window)!.Value;
            if (selectByClick)
            {
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                clock.Advance(DoubleClickTime + TimeSpan.FromTicks(1));
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
            clock.Advance(DoubleClickTime + TimeSpan.FromTicks(1));
            tree.GetVisualDescendants().OfType<LabelEditor>().Should().BeEmpty();
            window.MouseUp(point, MouseButton.Left);
            root.Calls.Should().Equal("TreeView1_MouseDown", "TreeView1_NodeClick", "TreeView1_MouseUp", "TreeView1_Click");
            clock.Advance(DoubleClickTime - TimeSpan.FromTicks(1));
            tree.GetVisualDescendants().OfType<LabelEditor>().Should().BeEmpty();
            clock.Advance(TimeSpan.FromTicks(1));
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

    // UI regression from #3352, not a measured VB6 caret/selection contract.
    [AvaloniaFact]
    public async Task Page_keys_keep_the_editing_label_in_view()
    {
        var tree = new VBTreeView { Width = 300, Height = 200 };
        var node = tree.Nodes.Add(text: "Node 01");
        tree.Nodes.SelectedItem = node;
        var window = new Window { Content = new Root { Child = tree } };
        window.Show();
        try
        {
            await tree.StartLabelEditAsync();
            var editor = tree.GetVisualDescendants().OfType<LabelEditor>().Single();
            // Make the line taller than the fixed edit box, independently of the host's font metrics.
            editor.FontSize = 24;
            window.UpdateLayout();
            var scroll = editor.GetVisualDescendants().OfType<ScrollViewer>().Single();
            foreach (var key in new[] { Key.PageDown, Key.PageUp, Key.PageDown, Key.PageUp })
            {
                window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
                window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
                window.UpdateLayout();
                scroll.Offset.Y.Should().Be(0, "page keys must not scroll a single-line label out of sight");
                editor.Text.Should().Be("Node 01");
                editor.IsFocused.Should().BeTrue();
            }
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            tree.GetVisualDescendants().OfType<LabelEditor>().Should().BeEmpty();
            node.Text.Should().Be("Node 01");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void Double_click_does_not_start_an_edit(int mode)
    {
        using var clock = new EditClock();
        var tree = new VBTreeView { Width = 300, Height = 200, ScheduleLabelEdit = clock.Schedule };
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
            clock.Advance(DoubleClickTime + TimeSpan.FromTicks(1));
            root.Calls.Should().Equal("TreeView1_MouseDown", "TreeView1_NodeClick", "TreeView1_MouseUp", "TreeView1_Click",
                "TreeView1_Expand", "TreeView1_DblClick", "TreeView1_MouseUp");
            tree.GetVisualDescendants().OfType<LabelEditor>().Should().BeEmpty();
        }
        finally { window.Close(); }
    }
}
