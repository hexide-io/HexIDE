using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Media;
using HexIDE.Runtime;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

public class ListViewLabelEditTests
{
    private sealed class Recorder : Decorator, IModuleExecutionRoot
    {
        public List<string> Calls { get; } = [];
        public bool CancelBefore { get; set; }
        public bool CancelAfter { get; set; }
        public bool RewriteLabel { get; set; }
        public string? OldText { get; private set; }
        public object? NewText { get; private set; }
        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null) => Calls.Add(name);
        public Task ExecuteControlEventAsync(string name, IReadOnlyList<CallArg> args)
        {
            Calls.Add(name);
            if (name.EndsWith("AfterLabelEdit"))
            {
                OldText = ((VBListView)Child!).SelectedItem!.Caption;
                NewText = args[1].Value.Value;
                if (RewriteLabel)
                    args[1].Ref!.Write(new Vb6Value("replacement"));
            }
            if (name.EndsWith("BeforeLabelEdit") ? CancelBefore : name.EndsWith("AfterLabelEdit") && CancelAfter)
                args[0].Ref!.Write(new Vb6Value(-1));
            return Task.CompletedTask;
        }
    }

    private static async Task<(Window Window, VBListView List, Recorder Recorder)> Open(long mode = 0)
    {
        var list = new VBListView();
        VBProps.SetName(list, "ListView1");
        list.TrySetProperty("LabelEdit", new Vb6Value(mode));
        await list.Items.CallAsync("Add", [Vb6Value.Missing, Vb6Value.Missing, new("Old")]);
        var recorder = new Recorder { Child = list };
        var window = new Window { Content = recorder, Width = 320, Height = 200 };
        window.Show();
        return (window, list, recorder);
    }

    private static TextBox? Editor(VBListView list) => list.GetVisualDescendants().OfType<TextBox>().SingleOrDefault();
    private static void Press(Control control, Key key) => control.RaiseEvent(new KeyEventArgs
        { RoutedEvent = InputElement.KeyDownEvent, Key = key });

    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    [AvaloniaFact]
    public async Task Editor_fits_the_label_without_scrollbars()
    {
        var (window, list, _) = await Open();
        try
        {
            list.TrySetProperty("View", new Vb6Value(1L));
            list.SelectedItem!.TrySetProperty("Text", new Vb6Value("Readable item label"));
            await list.CallAsync("StartLabelEdit", []);
            window.UpdateLayout();
            var editor = Editor(list)!;
            var label = list.GetItemLabelBounds(0);
            var text = new FormattedText(editor.Text!, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(editor.FontFamily, editor.FontStyle, editor.FontWeight, editor.FontStretch),
                editor.FontSize, Brushes.Black);
            editor.Bounds.Width.Should().BeGreaterThanOrEqualTo(text.Width + editor.Padding.Left
                + editor.Padding.Right + editor.BorderThickness.Left + editor.BorderThickness.Right);
            editor.Bounds.Width.Should().BeGreaterThanOrEqualTo(label.Width);
            editor.Bounds.Height.Should().BeGreaterThanOrEqualTo(editor.DesiredSize.Height);
            ScrollViewer.GetHorizontalScrollBarVisibility(editor).Should().Be(ScrollBarVisibility.Disabled);
            ScrollViewer.GetVerticalScrollBarVisibility(editor).Should().Be(ScrollBarVisibility.Disabled);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Real_form_events_write_Cancel_and_finish_before_the_call_returns(bool cancelBefore, bool cancelAfter)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var source = """
            VERSION 5.00
            Begin VB.Form Form1
               Caption = "Form1"
               ClientHeight = 3000
               ClientWidth = 4800
               Begin MSComctlLib.ListView ListView1
                  Height = 2400
                  Width = 4000
                  BorderStyle = 1
               End
            End
            Attribute VB_Name = "Form1"
            Private Sub Form_Load()
                ListView1.ListItems.Add , , "Old"
                ListView1.StartLabelEdit
                Debug.Print "returned"
            End Sub
            Private Sub ListView1_BeforeLabelEdit(Cancel As Integer)
                Debug.Print "before"
                Cancel = BEFORE
            End Sub
            Private Sub ListView1_AfterLabelEdit(Cancel As Integer, NewString As String)
                Debug.Print ListView1.SelectedItem.Text
                Debug.Print NewString
                Cancel = AFTER
            End Sub
            """.Replace("BEFORE", cancelBefore ? "True" : "False").Replace("AFTER", cancelAfter ? "True" : "False");
        var form = new FormDeserializer().Deserialize(project, source.ReplaceLineEndings("\r\n"), new NullSink())!;
        project.AddForm(form);
        var printed = new List<string>();
        var errors = new List<System.Exception>();
        VBFormRuntime? window = null;
        void Print(string value) => printed.Add(value);
        void Fault(VBWindowContext _, System.Exception error) => errors.Add(error);
        VBDebugConsole.Output += Print;
        VBWindowContext.Faulted += Fault;
        var watch = Window.WindowOpenedEvent.AddClassHandler<VBFormRuntime>((opened, _) => window = opened);
        using var cancel = new System.Threading.CancellationTokenSource();
        try
        {
            _ = VBLoader.RunForm(form, cancel.Token, out _);
            Dispatcher.UIThread.RunJobs();
            errors.Should().BeEmpty();
            printed.Should().Equal("before", "returned");
            window.Should().NotBeNull();
            var list = window!.GetVisualDescendants().OfType<VBListView>().Single();
            if (cancelBefore)
                Editor(list).Should().BeNull();
            else
            {
                Editor(list)!.Text = "New";
                Press(Editor(list)!, Key.Enter);
                Dispatcher.UIThread.RunJobs();
                errors.Should().BeEmpty();
                printed.Should().Equal("before", "returned", "Old", "New");
                list.SelectedItem!.Caption.Should().Be(cancelAfter ? "Old" : "New");
            }
        }
        finally
        {
            cancel.Cancel();
            Dispatcher.UIThread.RunJobs();
            watch.Dispose();
            window?.Close();
            VBDebugConsole.Output -= Print;
            VBWindowContext.Faulted -= Fault;
        }
    }

    [AvaloniaTheory]
    [InlineData(0L, true)]
    [InlineData(1L, false)]
    public async Task Clicking_selected_label_starts_only_automatic_editing(long mode, bool edits)
    {
        var (window, list, recorder) = await Open(mode);
        try
        {
            list.TrySetProperty("View", new Vb6Value(1L));
            window.MouseDown(new Point(10, 10), MouseButton.Left);
            window.MouseUp(new Point(10, 10), MouseButton.Left);
            await Task.Delay(650, TestContext.Current.CancellationToken);
            var context = System.Threading.ExecutionContext.Capture();
            try { Dispatcher.UIThread.RunJobs(); }
            finally { System.Threading.ExecutionContext.Restore(context!); }
            (Editor(list) is not null).Should().Be(edits);
            recorder.Calls.Count(name => name.EndsWith("BeforeLabelEdit")).Should().Be(edits ? 1 : 0);
            if (!edits)
            {
                await list.CallAsync("StartLabelEdit", []);
                Editor(list).Should().NotBeNull();
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Editing_keys_do_not_navigate_or_raise_ListView_KeyDown_and_Enter_KeyUp_returns_to_list()
    {
        var (window, list, recorder) = await Open();
        try
        {
            await list.Items.CallAsync("Add", []);
            var first = list.SelectedItem;
            await list.CallAsync("StartLabelEdit", []);
            Press(Editor(list)!, Key.Down);
            list.SelectedItem.Should().BeSameAs(first);
            recorder.Calls.Should().Equal("ListView1_BeforeLabelEdit");
            Press(Editor(list)!, Key.Enter);
            list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.Enter });
            recorder.Calls.Should().Equal("ListView1_BeforeLabelEdit", "ListView1_KeyUp");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0L)]
    [InlineData(1L)]
    public async Task Start_is_synchronous_and_changed_Enter_raises_before_committing(long mode)
    {
        var (window, list, recorder) = await Open(mode);
        try
        {
            await list.CallAsync("StartLabelEdit", []);
            recorder.Calls.Should().Equal("ListView1_BeforeLabelEdit");
            var editor = Editor(list);
            editor.Should().NotBeNull();
            editor!.Text = "New";
            Press(editor, Key.Enter);
            recorder.Calls.Should().Equal("ListView1_BeforeLabelEdit", "ListView1_AfterLabelEdit");
            recorder.OldText.Should().Be("Old");
            recorder.NewText.Should().BeOfType<string>().Which.Should().Be("New");
            list.SelectedItem!.Caption.Should().Be("New");
            Editor(list).Should().BeNull();
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(Key.Escape, "Changed")]
    [InlineData(Key.Enter, "Old")]
    public async Task Escape_and_unchanged_Enter_raise_no_AfterLabelEdit(Key key, string text)
    {
        var (window, list, recorder) = await Open();
        try
        {
            await list.CallAsync("StartLabelEdit", []);
            Editor(list)!.Text = text;
            Press(Editor(list)!, key);
            recorder.Calls.Should().Equal("ListView1_BeforeLabelEdit");
            list.SelectedItem!.Caption.Should().Be("Old");
            Editor(list).Should().BeNull();
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cancel_is_passed_by_reference(bool before)
    {
        var (window, list, recorder) = await Open();
        try
        {
            recorder.CancelBefore = before;
            recorder.CancelAfter = !before;
            await list.CallAsync("StartLabelEdit", []);
            if (!before)
            {
                Editor(list)!.Text = "Changed";
                Press(Editor(list)!, Key.Enter);
            }
            Editor(list).Should().BeNull();
            list.SelectedItem!.Caption.Should().Be("Old");
            recorder.Calls.Should().HaveCount(before ? 1 : 2);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Deleting_the_whole_label_commits_an_empty_String()
    {
        var (window, list, recorder) = await Open();
        try
        {
            await list.CallAsync("StartLabelEdit", []);
            Editor(list)!.Text = "";
            Press(Editor(list)!, Key.Enter);
            recorder.NewText.Should().Be("");
            list.SelectedItem!.Caption.Should().Be("");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Unmeasured_NewString_mutation_is_refused()
    {
        var (window, list, recorder) = await Open();
        try
        {
            await list.CallAsync("StartLabelEdit", []);
            recorder.RewriteLabel = true;
            Editor(list)!.Text = "New";
            System.Action enter = () => Press(Editor(list)!, Key.Enter);
            enter.Should().Throw<System.NotImplementedException>().WithMessage("*argument mutation*unmeasured*");
            list.SelectedItem!.Caption.Should().Be("Old");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Without_focus_Start_does_nothing()
    {
        var (window, list, recorder) = await Open();
        try
        {
            list.TrySetProperty("SelectedItem", Vb6Value.Nothing);
            await list.CallAsync("StartLabelEdit", []);
            recorder.Calls.Should().BeEmpty();
            Editor(list).Should().BeNull();
        }
        finally { window.Close(); }
    }
}
