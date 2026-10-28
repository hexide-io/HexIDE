using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.VisualTree;
using HexIDE.Runtime;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

public class TreeViewLabelEditTests
{
    private sealed class Root : Decorator, IModuleExecutionRoot
    {
        internal Action<string, IList<Vb6Value>>? Handler;
        internal readonly List<string> Calls = new();
        internal readonly List<string> Keys = new();
        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null)
        {
            if (name.EndsWith("KeyDown", StringComparison.Ordinal)) Keys.Add(name);
        }
        public Task ExecuteEditEventAsync(string name, IList<Vb6Value> args)
        {
            Calls.Add(name);
            Handler?.Invoke(name, args);
            return Task.CompletedTask;
        }
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task StartLabelEdit_creates_editor_before_event_and_places_and_selects_it_after(int mode)
    {
        var tree = new VBTreeView { Width = 300, Height = 200 };
        VBProps.SetName(tree, "TreeView1");
        tree.TrySetProperty("LabelEdit", new Vb6Value(mode));
        tree.Nodes.Add(text: "Node 1");
        var node = tree.Nodes.Add(text: "Node 2");
        var root = new Root { Child = tree };
        var window = new Window { Content = root };
        window.Show();
        try
        {
            tree.Nodes.SelectedItem = node;
            root.Handler = (name, args) =>
            {
                name.Should().Be("TreeView1_BeforeLabelEdit");
                args[0].Type.Should().Be(Vb6Value.ValueType.Integer);
                args[0].Value.Should().Be((short)0);
                var pending = tree.GetVisualDescendants().OfType<TextBox>().Single();
                pending.Text.Should().Be("Node 2");
                Canvas.GetLeft(pending).Should().Be(0);
                Canvas.GetTop(pending).Should().Be(0);
                pending.Width.Should().Be(0);
                pending.Height.Should().Be(0);
            };
            await tree.CallAsync("StartLabelEdit", []);
            var editor = tree.GetVisualDescendants().OfType<TextBox>().Single();
            editor.Height.Should().Be(17);
            editor.SelectionStart.Should().Be(0);
            editor.SelectionEnd.Should().Be(6);
            node.Text.Should().Be("Node 2");
            Func<Task> repeat = async () => await tree.CallAsync("StartLabelEdit", []);
            await repeat.Should().ThrowAsync<NotImplementedException>();
            tree.GetVisualDescendants().OfType<TextBox>().Single().Should().BeSameAs(editor);
            root.Calls.Should().Equal("TreeView1_BeforeLabelEdit");
        }
        finally { window.Close(); }
    }

    // The box is a line high, as VB6's is; a scroll bar in it covered the end of the label as it opened (#2360).
    [AvaloniaFact]
    public async Task The_opened_editor_shows_its_text_with_no_scroll_bar()
    {
        var tree = new VBTreeView { Width = 300, Height = 200 };
        VBProps.SetName(tree, "TreeView1");
        tree.Nodes.Add(text: "Node 1");
        var node = tree.Nodes.Add(text: "Node 2");
        var window = new Window { Content = new Root { Child = tree } };
        window.Show();
        try
        {
            tree.Nodes.SelectedItem = node;
            await tree.CallAsync("StartLabelEdit", []);
            window.UpdateLayout();
            var editor = tree.GetVisualDescendants().OfType<TextBox>().Single();
            editor.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>()
                .Where(bar => bar.IsEffectivelyVisible).Should().BeEmpty();
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(Key.Enter, false, false)]
    [InlineData(Key.Enter, true, false)]
    [InlineData(Key.Enter, false, true)]
    [InlineData(Key.Escape, false, false)]
    [InlineData(Key.Tab, false, false)]
    public async Task Ending_preserves_old_text_during_event_and_honours_cancel_and_NewString(Key key, bool cancel, bool replace)
    {
        var tree = new VBTreeView { Width = 300, Height = 200 };
        VBProps.SetName(tree, "TreeView1");
        var node = tree.Nodes.Add(text: "Node 2");
        var root = new Root { Child = tree };
        var window = new Window { Content = root };
        window.Show();
        try
        {
            tree.Nodes.SelectedItem = node;
            await tree.CallAsync("StartLabelEdit", []);
            var editor = tree.GetVisualDescendants().OfType<TextBox>().Single();
            editor.Text = "xyz";
            root.Handler = (name, args) =>
            {
                name.Should().Be("TreeView1_AfterLabelEdit");
                node.Text.Should().Be("Node 2");
                args[0].Value.Should().Be((short)0);
                args[1].Value.Should().Be("xyz");
                if (cancel) args[0] = new Vb6Value((short)-1);
                if (replace) args[1] = new Vb6Value("Changed");
            };
            window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
            node.Text.Should().Be(key == Key.Escape || cancel ? "Node 2" : replace ? "Changed" : "xyz");
            tree.GetVisualDescendants().OfType<TextBox>().Should().BeEmpty();
            root.Calls.Should().Equal(key == Key.Escape
                ? new[] { "TreeView1_BeforeLabelEdit" }
                : new[] { "TreeView1_BeforeLabelEdit", "TreeView1_AfterLabelEdit" });
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancel_before_and_unchanged_commit_raise_no_after_event(bool cancel)
    {
        var tree = new VBTreeView { Width = 300, Height = 200 };
        VBProps.SetName(tree, "TreeView1");
        var node = tree.Nodes.Add(text: "Node 2");
        var root = new Root { Child = tree };
        var window = new Window { Content = root };
        window.Show();
        try
        {
            tree.Nodes.SelectedItem = node;
            root.Handler = (_, args) => { if (cancel) args[0] = new Vb6Value((short)-1); };
            await tree.CallAsync("StartLabelEdit", []);
            if (!cancel) window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            tree.GetVisualDescendants().OfType<TextBox>().Should().BeEmpty();
            root.Calls.Should().Equal("TreeView1_BeforeLabelEdit");
            node.Text.Should().Be("Node 2");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task StartLabelEdit_without_selection_does_nothing()
    {
        var tree = new VBTreeView();
        await tree.CallAsync("StartLabelEdit", []);
        tree.GetVisualDescendants().OfType<TextBox>().Should().BeEmpty();
    }
    [AvaloniaTheory]
    [InlineData("Cancel = True", "", "Node 2", false)]
    [InlineData("", "Cancel = True", "Node 2", true)]
    [InlineData("", "NewString = \"Changed\"", "Changed", true)]
    public async Task Real_form_handlers_write_back_edit_arguments(string before, string after, string expected, bool opens)
    {
        var tree = new VBTreeView { Width = 300, Height = 200 };
        VBProps.SetName(tree, "TreeView1");
        var node = tree.Nodes.Add(text: "Node 2");
        var window = new VBFormRuntime { Content = tree };
        window.Context.SetCode($"""
            Private Sub TreeView1_BeforeLabelEdit(Cancel As Integer)
                {before}
            End Sub
            Private Sub TreeView1_AfterLabelEdit(Cancel As Integer, NewString As String)
                {after}
            End Sub
            """, "Form1");
        window.Show();
        try
        {
            tree.Nodes.SelectedItem = node;
            await tree.CallAsync("StartLabelEdit", []);
            var editors = tree.GetVisualDescendants().OfType<TextBox>().ToArray();
            editors.Length.Should().Be(opens ? 1 : 0);
            if (opens)
            {
                editors[0].Text = "xyz";
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            }
            node.Text.Should().Be(expected);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Tab_and_focus_loss_commit_and_move_to_next_control(bool tab)
    {
        var tree = new VBTreeView { Width = 300, Height = 200 };
        VBProps.SetName(tree, "TreeView1");
        var node = tree.Nodes.Add(text: "Node 2");
        var other = new TextBox();
        var panel = new StackPanel();
        panel.Children.Add(tree);
        panel.Children.Add(other);
        var root = new Root { Child = panel };
        var window = new Window { Content = root };
        window.Show();
        try
        {
            tree.Nodes.SelectedItem = node;
            await tree.CallAsync("StartLabelEdit", []);
            var editor = tree.GetVisualDescendants().OfType<TextBox>().Single();
            editor.Text = "xyz";
            root.Handler = (_, _) => other.IsFocused.Should().BeFalse();
            if (tab) window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            else other.Focus();
            node.Text.Should().Be("xyz");
            other.IsFocused.Should().BeTrue();
            root.Calls.Should().Equal("TreeView1_BeforeLabelEdit", "TreeView1_AfterLabelEdit");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task First_key_grows_and_raises_editor_and_text_width_follows_input()
    {
        var tree = new VBTreeView { Width = 300, Height = 200 };
        VBProps.SetName(tree, "TreeView1");
        var node = tree.Nodes.Add(text: "Node 2");
        var root = new Root { Child = tree };
        var window = new Window { Content = root };
        window.Show();
        try
        {
            tree.Nodes.SelectedItem = node;
            await tree.CallAsync("StartLabelEdit", []);
            var editor = tree.GetVisualDescendants().OfType<TextBox>().Single();
            var top = Canvas.GetTop(editor);
            var height = editor.Height;
            window.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.A, null);
            window.KeyTextInput("abc");
            Canvas.GetTop(editor).Should().Be(top - 2);
            editor.Height.Should().Be(height + 4);
            editor.Text.Should().Be("abc");
            root.Keys.Should().BeEmpty();
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
    public void F2_only_raises_KeyDown_113_and_never_opens_an_editor(int mode)
    {
        var tree = new VBTreeView { Width = 300, Height = 200 };
        VBProps.SetName(tree, "TreeView1");
        tree.TrySetProperty("LabelEdit", new Vb6Value(mode));
        tree.Nodes.Add(text: "Node 2");
        var root = new ContainerRuntimeHarness.RecordingRoot { Child = tree };
        var window = new Window { Content = root };
        window.Show();
        try
        {
            tree.Focus();
            root.Calls.Clear();
            window.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null);
            root.Calls.Select(c => c.Name).Should().Equal("TreeView1_KeyDown");
            root.Calls[0].Args![0].Value.Should().Be((short)113);
            tree.GetVisualDescendants().OfType<TextBox>().Should().BeEmpty();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Manual_mode_selected_label_click_does_not_edit_and_later_key_goes_to_tree()
    {
        var tree = new VBTreeView { Width = 300, Height = 200 };
        VBProps.SetName(tree, "TreeView1");
        tree.TrySetProperty("LabelEdit", new Vb6Value(1));
        tree.Nodes.Add(text: "Node 2");
        var root = new ContainerRuntimeHarness.RecordingRoot { Child = tree };
        var window = new Window { Content = root };
        window.Show();
        try
        {
            tree.Focus();
            root.Calls.Clear();
            var point = tree.TranslatePoint(new Avalonia.Point(42, 10), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            await Task.Delay(TimeSpan.FromMilliseconds(1800), Xunit.TestContext.Current.CancellationToken);
            window.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.A, null);
            root.Calls.Select(c => c.Name).Should().Equal("TreeView1_MouseDown", "TreeView1_NodeClick",
                "TreeView1_MouseUp", "TreeView1_Click", "TreeView1_KeyDown");
            tree.GetVisualDescendants().OfType<TextBox>().Should().BeEmpty();
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unmeasured_hidden_node_positioning_refuses_before_creating_editor(bool collapsed)
    {
        var tree = new VBTreeView { Width = 300, Height = collapsed ? 200 : 20 };
        VBProps.SetName(tree, "TreeView1");
        var parent = tree.Nodes.Add(text: "parent");
        var node = collapsed ? tree.Nodes.Add(parent, 4, text: "child") : tree.Nodes.Add(text: "other");
        var root = new Root { Child = tree };
        var window = new Window { Content = root };
        window.Show();
        try
        {
            tree.Nodes.SelectedItem = node;
            Func<Task> start = async () => await tree.CallAsync("StartLabelEdit", []);
            await start.Should().ThrowAsync<NotImplementedException>();
            root.Calls.Should().BeEmpty();
            tree.GetVisualDescendants().OfType<TextBox>().Should().BeEmpty();
        }
        finally { window.Close(); }
    }

}
