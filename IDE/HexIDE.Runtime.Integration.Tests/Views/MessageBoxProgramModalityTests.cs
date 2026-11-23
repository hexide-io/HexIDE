using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.IDE;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using MessageBox = HexIDE.Runtime.Dialogs.MessageBox;

namespace HexIDE.Runtime.Integration.Tests.Views;

// VB6 SP6, native and p-code: docs/vb6-fidelity-oracle/rgb-qbcolor-and-msgboxs-style.md,
// "A message box is modal to the whole program" (#834). Timer scheduling is separate (#5491).
public class MessageBoxProgramModalityTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string error) => throw new InvalidOperationException(error);
    }

    private sealed class Run : IDisposable
    {
        private readonly CancellationTokenSource cancel = new();
        private readonly IDisposable watch;
        internal readonly List<string> Printed = [];
        internal readonly List<Exception> Errors = [];
        internal readonly List<Window> Boxes = [];
        internal readonly VBFormRuntime Owner;
        internal VBFormRuntime Other => (VBFormRuntime)Owner.Context.Program!.LoadedForms[1].FormWindow!;
        internal readonly Task Running;

        internal Run(string controls = "", string code = "")
        {
            var project = new ProjectDefinition(VBProjectType.EXE, "P");
            foreach (var name in new[] { "Form1", "Form2" })
            {
                var form = new FormDeserializer().Deserialize(project, $$"""
                    VERSION 5.00
                    Begin VB.Form {{name}}
                       Caption = "{{name}}"
                       ClientWidth = 4500
                       ClientHeight = 3000
                    {{controls}}
                    End
                    Attribute VB_Name = "{{name}}"
                    Private Sub Form_MouseDown(Button As Integer, Shift As Integer, X As Single, Y As Single)
                        Debug.Print "{{name}} down"
                    End Sub
                    Private Sub Form_Click()
                        Debug.Print "{{name}} click"
                    End Sub
                    {{code}}
                    Public Sub HideSelf()
                        Me.Visible = False
                    End Sub
                    Public Sub ShowSelf()
                        Me.Visible = True
                    End Sub
                    """.ReplaceLineEndings("\r\n"), new Sink())!;
                project.AddForm(form);
            }
            watch = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (window.Content is MessageBox)
                    Boxes.Add(window);
            });
            VBDebugConsole.Output += Printed.Add;
            VBWindowContext.Faulted += Faulted;
            VBWindowContext.RunTimeError += RunTimeError;
            Running = VBLoader.RunForm(project.Forms[0], cancel.Token, out Owner);
            Pump();
            Code("Form2.Show").GetAwaiter().GetResult();
            Pump();
            Owner.Context.Program!.LoadedForms.Should().HaveCount(2);
        }

        private void Faulted(VBWindowContext _, Exception error) => Errors.Add(error);
        private void RunTimeError(VBWindowContext _, VBRunTimeException error) => Errors.Add(error);

        internal async Task Code(string code)
        {
            var context = Owner.Context;
            await new StatementExecutor(context.Interpreter!, context.InstanceEnv, context.FormInstance!.ClassDef)
                .Visit(context.Interpreter!.TryParseStatement(code)!);
            Pump();
        }

        internal Task OpenBox()
        {
            var done = new TaskCompletionSource();
            Owner.Events.Raise("probe", async () =>
            {
                try
                {
                    await Code("MsgBox \"probe\"");
                    done.SetResult();
                }
                catch (Exception error) { done.SetException(error); }
            });
            Pump();
            return done.Task;
        }

        internal Dictionary<Window, List<(bool Enabled, int Children)>> RecordNativeEnablement()
        {
            var writes = new Dictionary<Window, List<(bool Enabled, int Children)>>();
            var program = Owner.Context.Program!;
            program.Interpreter.BuiltIns.HoldFormsForMessageBox = () =>
                new Dialogs.MessageBoxFormHold(program.LoadedForms, (window, enabled) =>
                {
                    if (!writes.TryGetValue(window, out var changes))
                        writes[window] = changes = [];
                    changes.Add((enabled, window.OwnedWindows.Count));
                    Dialogs.MessageBoxFormHold.ApplyNativeEnabled(window, enabled);
                });
            return writes;
        }

        internal void Answer(int index)
        {
            var box = (MessageBox)Boxes[index].Content!;
            var button = box.GetVisualDescendants().OfType<Button>().Single();
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
        }

        public void Dispose()
        {
            cancel.Cancel();
            Pump();
            foreach (var box in Boxes)
                box.Close();
            Pump();
            watch.Dispose();
            VBWindowContext.RunTimeError -= RunTimeError;
            VBWindowContext.Faulted -= Faulted;
            VBDebugConsole.Output -= Printed.Add;
            cancel.Dispose();
        }
    }

    private static void Pump()
    {
        var context = System.Threading.ExecutionContext.Capture();
        try { Dispatcher.UIThread.RunJobs(); }
        finally { if (context is not null) System.Threading.ExecutionContext.Restore(context); }
    }

    private static void Click(VBFormRuntime window, int x = 80)
    {
        var point = new Point(x, 70);
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Pump();
    }

    [AvaloniaTheory]
    [InlineData(1.0, "visible")]
    [InlineData(1.5, "visible")]
    [InlineData(1.0, "hidden before")]
    [InlineData(1.5, "hidden before")]
    [InlineData(1.0, "hidden during")]
    [InlineData(1.5, "hidden during")]
    public async Task A_waiting_box_blocks_clicks_on_both_forms_until_answered(double scaling, string visibility)
    {
        using var run = new Run();
        run.Owner.SetRenderScaling(scaling);
        run.Other.SetRenderScaling(scaling);
        if (visibility == "hidden before")
            await run.Code("Form1.HideSelf");
        var waiting = run.OpenBox();
        Pump();
        if (visibility == "hidden during")
            await run.Code("Form1.HideSelf");
        if (visibility != "visible")
            await run.Code("Form1.ShowSelf");

        waiting.IsCompleted.Should().BeFalse();
        Click(run.Other);
        Click(run.Owner);
        run.Printed.Should().BeEmpty();
        // The pump already holds handlers behind the box on main. Check after answering too, so input queued
        // behind the statement cannot masquerade as discarded input.
        run.Answer(run.Boxes.Count - 1);
        await waiting;
        run.Printed.Should().BeEmpty("clicks made while the box waited must not run later either");

        // A different point makes this a single click, independent of the headless double-click interval.
        Click(run.Owner, 110);
        Click(run.Other, 110);
        run.Printed.Should().Equal("Form1 down", "Form1 click", "Form2 down", "Form2 click");
        run.Errors.Should().BeEmpty();
    }

    // The issue explicitly requires the program's own assignments to survive; their getters during the wait are
    // deliberately not pinned, since that VB6 value is unmeasured.
    [AvaloniaTheory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task An_Enabled_assignment_while_waiting_survives_the_answer(bool before, bool after)
    {
        using var run = new Run();
        await run.Code($"Form2.Enabled = {before}");
        var waiting = run.OpenBox();
        await run.Code($"Form2.Enabled = {after}");
        Click(run.Other);
        run.Printed.Should().BeEmpty();
        run.Answer(0);
        await waiting;
        run.Other.IsEnabled.Should().Be(after);
        run.Printed.Should().BeEmpty("an Enabled assignment cannot release the box's input hold");
        Click(run.Other, 110);
        run.Printed.Should().Equal(after ? ["Form2 down", "Form2 click"] : []);
        run.Errors.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void With_no_box_both_forms_take_real_clicks()
    {
        using var run = new Run();
        Click(run.Owner);
        Click(run.Other);
        run.Printed.Should().Equal("Form1 down", "Form1 click", "Form2 down", "Form2 click");
        run.Errors.Should().BeEmpty();
    }

    [AvaloniaFact]
    public async Task A_form_disabled_by_code_stays_disabled_after_the_answer()
    {
        using var run = new Run();
        await run.Code("Form2.Enabled = False");
        var waiting = run.OpenBox();
        Pump();
        run.Answer(0);
        await waiting;
        run.Other.IsEnabled.Should().BeFalse();
        Click(run.Owner);
        Click(run.Other);
        run.Printed.Should().Equal("Form1 down", "Form1 click");
        run.Errors.Should().BeEmpty();
    }

    // Counting overlapping boxes is #834's explicit design decision, not a VB6 measurement. Open them directly
    // through the shared built-in: Timer handlers can't open a second box while the event pump holds them.
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Forms_refuse_input_until_the_last_box_is_answered(int first)
    {
        using var run = new Run();
        var native = run.RecordNativeEnablement();
        var builtins = run.Owner.Context.Interpreter!.BuiltIns;
        var one = builtins.EvaluateBuiltInFunction("MsgBox", [new Vb6Value("one")]);
        var two = builtins.EvaluateBuiltInFunction("MsgBox", [new Vb6Value("two")]);
        Pump();
        run.Answer(first);
        Click(run.Other);
        Click(run.Owner);
        run.Printed.Should().BeEmpty();
        native.Should().ContainKeys(run.Owner, run.Other);
        native[run.Owner].Last().Enabled.Should().BeFalse();
        native[run.Other].Last().Enabled.Should().BeFalse();
        run.Answer(1 - first);
        await Task.WhenAll(one, two);
        run.Printed.Should().BeEmpty("input held by either box must not run after the last answer");
        native[run.Owner].Last().Should().Be((true, 0), "restoration follows Avalonia's owned-dialog cleanup");
        native[run.Other].Last().Enabled.Should().BeTrue();
        // A different point makes this a single click, independent of the headless double-click interval.
        Click(run.Owner, 110);
        Click(run.Other, 110);
        run.Printed.Should().Equal("Form1 down", "Form1 click", "Form2 down", "Form2 click");
        run.Errors.Should().BeEmpty();
    }

    // Headless SetEnabled is a no-op. Observe the real native bridge's calls on Linux, including the dispatcher
    // turn after Closed removes the owned dialog; actual window-system enablement remains a live check.
    [AvaloniaTheory]
    [InlineData("visible", "")]
    [InlineData("hidden before", "")]
    [InlineData("hidden during", "")]
    [InlineData("visible", "Form1")]
    [InlineData("visible", "Form2")]
    public async Task Native_windows_are_held_through_visibility_changes_and_restored_after_dialog_cleanup(
        string visibility, string disabledForm)
    {
        using var run = new Run();
        var native = run.RecordNativeEnablement();
        if (disabledForm.Length > 0)
            await run.Code($"{disabledForm}.Enabled = False");
        if (visibility == "hidden before")
            await run.Code("Form1.HideSelf");
        var waiting = run.OpenBox();
        native.Should().ContainKeys([run.Owner, run.Other], "both native windows must receive the hold");
        var ownerWrites = native[run.Owner].Count;
        if (visibility == "hidden during")
            await run.Code("Form1.HideSelf");
        if (visibility != "visible")
        {
            await run.Code("Form1.ShowSelf");
            native[run.Owner].Count.Should().BeGreaterThan(ownerWrites, "showing must reapply the native hold");
            run.Owner.OwnedWindows.Should().BeEmpty("the box moved to an unowned window");
        }

        waiting.IsCompleted.Should().BeFalse();
        native[run.Owner].Last().Enabled.Should().BeFalse();
        native[run.Other].Last().Enabled.Should().BeFalse();
        run.Answer(run.Boxes.Count - 1);
        await waiting;
        native[run.Owner].Last().Should().Be((disabledForm != "Form1", 0),
            "Avalonia must have removed the closed dialog before the last native restoration");
        native[run.Other].Last().Should().Be((disabledForm != "Form2", 0));
        run.Owner.IsEnabled.Should().Be(disabledForm != "Form1");
        run.Other.IsEnabled.Should().Be(disabledForm != "Form2");
        run.Errors.Should().BeEmpty();
    }

    // #834's review asks for unchanged drawing (reasoned from native top-level disablement, not a new oracle row).
    // TestApp renders with Skia and UseHeadlessDrawing = false, as ClassicRenderTests does.
    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public async Task A_held_forms_controls_keep_their_enabled_look(double scaling)
    {
        using var run = new Run("""
            Begin VB.CommandButton Command1
               Caption = "Press me"
               Left = 300
               Top = 300
               Width = 1500
               Height = 450
            End
            Begin VB.TextBox Text1
               Text = "Still enabled"
               Left = 300
               Top = 1050
               Width = 2250
               Height = 450
            End
            """);
        run.Other.SetRenderScaling(scaling);
        Pump();
        var button = run.Other.GetVisualDescendants().OfType<VBCommandButton>().Single();
        var text = run.Other.GetVisualDescendants().OfType<VBTextBox>().Single();
        button.IsEffectivelyEnabled.Should().BeTrue();
        text.IsEffectivelyEnabled.Should().BeTrue();
        using var before = run.Other.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame");
        var waiting = run.OpenBox();
        waiting.IsCompleted.Should().BeFalse();
        using var during = run.Other.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame");
        // Check pixels before enablement, so the baseline demonstrates the visible regression too.
        during.PixelSize.Should().Be(before.PixelSize);
        Pixels(during).Should().Equal(Pixels(before), "holding input must leave the form's appearance alone");
        button.IsEffectivelyEnabled.Should().BeTrue();
        text.IsEffectivelyEnabled.Should().BeTrue();
        run.Answer(0);
        await waiting;
        run.Errors.Should().BeEmpty();
    }

    private static byte[] Pixels(Avalonia.Media.Imaging.Bitmap bitmap)
    {
        var pixels = new byte[bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), pin.AddrOfPinnedObject(), pixels.Length, bitmap.PixelSize.Width * 4); }
        finally { pin.Free(); }
        return pixels;
    }

    // The managed route also covers hosted roots, which have no native window to disable (#834 review).
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Held_roots_block_all_seven_input_routes_without_disabling_controls(bool hosted)
    {
        using var run = new Run();
        Window window = run.Other;
        Control root = window;
        if (hosted)
        {
            window = new Window { Width = 300, Height = 200 };
            var host = Substitute.For<IHostedForm>();
            host.Window.Returns(window);
            host.When(h => h.SetContent(Arg.Any<object>())).Do(call => window.Content = call.Arg<object>());
            root = new HostedFormRoot(host, new StandaloneStandardLib(window, "P"));
        }
        var target = new Border { Background = Brushes.White, Focusable = true };
        if (root is HostedFormRoot hostedRoot)
            hostedRoot.SetContent(target);
        else
            window.Content = target;
        var events = new List<string>();
        foreach (var input in new RoutedEvent[] { InputElement.PointerPressedEvent, InputElement.PointerReleasedEvent,
                     InputElement.PointerMovedEvent, InputElement.PointerWheelChangedEvent, InputElement.KeyDownEvent,
                     InputElement.KeyUpEvent, InputElement.TextInputEvent })
            target.AddHandler(input, new EventHandler<RoutedEventArgs>((_, e) => events.Add(e.RoutedEvent!.Name)));
        var form = new VbObject(run.Owner.Context.FormInstance!.ClassDef, new()) { FormWindow = window };
        void Input()
        {
            window.MouseMove(new Point(40, 40));
            window.MouseMove(new Point(50, 50));
            window.MouseDown(new Point(50, 50), MouseButton.Left);
            window.MouseUp(new Point(50, 50), MouseButton.Left);
            window.MouseWheel(new Point(50, 50), new Vector(0, 1));
            window.KeyPress(Key.X, RawInputModifiers.None, PhysicalKey.X, null);
            window.KeyTextInput("x");
            window.KeyRelease(Key.X, RawInputModifiers.None, PhysicalKey.X, null);
            Pump();
        }
        try
        {
            window.Show();
            Pump();
            target.Focus().Should().BeTrue();
            Input();
            events.Distinct().Should().BeEquivalentTo("PointerPressed", "PointerReleased", "PointerMoved",
                "PointerWheelChanged", "KeyDown", "KeyUp", "TextInput");
            events.Clear();
            using (new Dialogs.MessageBoxFormHold([form]))
            {
                Input();
                events.Should().BeEmpty();
                target.IsEffectivelyEnabled.Should().BeTrue();
            }
            Input();
            events.Distinct().Should().BeEquivalentTo("PointerPressed", "PointerReleased", "PointerMoved",
                "PointerWheelChanged", "KeyDown", "KeyUp", "TextInput");
        }
        finally { if (hosted) window.Close(); }
    }

    [AvaloniaFact]
    public async Task Held_controls_discard_mouse_events_and_text_edits()
    {
        using var run = new Run("""
            Begin VB.CommandButton Command1
               Caption = "Press me"
               Left = 300
               Top = 300
               Width = 1500
               Height = 450
            End
            Begin VB.TextBox Text1
               Text = "unchanged"
               Left = 300
               Top = 1050
               Width = 2250
               Height = 450
            End
            Begin VB.ListBox List1
               Left = 2700
               Top = 300
               Width = 1500
               Height = 2100
            End
            """, """
            Private Sub Command1_Click()
                Debug.Print "button click"
            End Sub
            Private Sub Command1_MouseDown(Button As Integer, Shift As Integer, X As Single, Y As Single)
                Debug.Print "button down"
            End Sub
            Private Sub Command1_MouseUp(Button As Integer, Shift As Integer, X As Single, Y As Single)
                Debug.Print "button up"
            End Sub
            Private Sub Command1_MouseMove(Button As Integer, Shift As Integer, X As Single, Y As Single)
                Debug.Print "button move"
            End Sub
            Private Sub Text1_MouseDown(Button As Integer, Shift As Integer, X As Single, Y As Single)
                Debug.Print "text down"
            End Sub
            Private Sub List1_Click()
                Debug.Print "list click"
            End Sub
            """);
        var button = run.Other.GetVisualDescendants().OfType<VBCommandButton>().Single();
        var text = run.Other.GetVisualDescendants().OfType<VBTextBox>().Single();
        var list = run.Other.GetVisualDescendants().OfType<VBListBox>().Single();
        await run.Code("Form2.List1.AddItem \"first\"");
        await run.Code("Form2.List1.AddItem \"second\"");
        text.Focus().Should().BeTrue();
        var waiting = run.OpenBox();
        foreach (var control in new Control[] { button, text, list })
        {
            var point = control.TranslatePoint(new Point(10, 10), run.Other)!.Value;
            run.Other.MouseMove(point);
            run.Other.MouseDown(point, MouseButton.Left);
            run.Other.MouseUp(point, MouseButton.Left);
        }
        run.Other.KeyTextInput("x");
        Pump();
        text.Text.Should().Be("unchanged");
        list.SelectedIndex.Should().Be(-1);
        run.Answer(0);
        await waiting;
        run.Printed.Should().BeEmpty("handled-event mouse bridges must discard held input too");
        var click = button.TranslatePoint(new Point(10, 10), run.Other)!.Value;
        run.Other.MouseMove(click);
        run.Other.MouseDown(click, MouseButton.Left);
        run.Other.MouseUp(click, MouseButton.Left);
        Pump();
        run.Printed.Should().Contain("button down", "button click", "button up");
        run.Errors.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void A_hosted_forms_root_also_holds_input_without_changing_its_Enabled_setting()
    {
        using var run = new Run();
        var window = new Window { Width = 300, Height = 200 };
        var host = Substitute.For<IHostedForm>();
        host.Window.Returns(window);
        host.When(h => h.SetContent(Arg.Any<object>())).Do(call => window.Content = call.Arg<object>());
        var root = new HostedFormRoot(host, new StandaloneStandardLib(window, "P"));
        var canvas = new Canvas { Background = Brushes.White };
        root.SetContent(canvas);
        var presses = 0;
        canvas.PointerPressed += (_, _) => presses++;
        var form = new VbObject(run.Owner.Context.FormInstance!.ClassDef, new()) { FormWindow = window };
        try
        {
            window.Show();
            Pump();
            using (new Dialogs.MessageBoxFormHold([form]))
            {
                window.MouseDown(new Point(50, 50), MouseButton.Left);
                window.MouseUp(new Point(50, 50), MouseButton.Left);
                presses.Should().Be(0);
                root.IsEnabled.Should().BeTrue("the hold leaves the program's setting alone");
            }
            window.MouseDown(new Point(80, 70), MouseButton.Left);
            window.MouseUp(new Point(80, 70), MouseButton.Left);
            presses.Should().Be(1);
        }
        finally { window.Close(); }
    }
}
