using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Automation;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// An action on a running form replies once the program has finished what it set off and is waiting for input again
/// (#1232). A handler gives up the UI thread at the debugger's time slice (#766), so the reply used to go out part way
/// through it: after Minesweeper's Game > Intermediate, a snapshot showed the new field with the old mine count.
/// </summary>
/// <remarks>
/// Each case runs through the IDE's own F5 path under a time slice of <c>-1</c>, which yields at every statement, so the
/// handler is sure to be part way through when the action returns.
/// </remarks>
public class ProgramWaitTests
{
    private const string Caption = "ProgramWaitTests";

    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private static string FormText(string name, string caption) =>
        "VERSION 5.00\r\n"
      + $"Begin VB.Form {name} \r\n"
      + $"   Caption         =   \"{caption}\"\r\n"
      + "   ClientHeight    =   3000\r\n"
      + "   ClientWidth     =   4000\r\n"
      + "   Begin VB.CommandButton Command1 \r\n"
      + "      Caption         =   \"Command1\"\r\n"
      + "      Height          =   495\r\n"
      + "      Left            =   120\r\n"
      + "      Top             =   120\r\n"
      + "      Width           =   1215\r\n"
      + "   End\r\n"
      + "   Begin VB.TextBox Text1 \r\n"
      + "      Height          =   375\r\n"
      + "      Left            =   120\r\n"
      + "      Top             =   840\r\n"
      + "      Width           =   1215\r\n"
      + "   End\r\n"
      + "   Begin VB.Menu mnuGrid \r\n"
      + "      Caption         =   \"&Grid\"\r\n"
      + "   End\r\n"
      + "   Begin VB.Menu mnuRows \r\n"
      + "      Caption         =   \"&Rows\"\r\n"
      + "      Checked         =   -1  'True\r\n"
      + "   End\r\n"
      + "End\r\n"
      + $"Attribute VB_Name = \"{name}\"\r\n";

    private sealed class Run : IDisposable
    {
        public required ProjectRunnerService Runner { get; init; }
        public required DebugController Controller { get; init; }
        public required VBFormRuntime Window { get; init; }

        public Control Named(string name) => Named(Window, name);

        public static Control Named(Window window, string name) =>
            window.GetLogicalDescendants().OfType<Control>().First(c => VBProps.GetName(c) == name);

        public string Text => ((TextBox)Named("Text1")).Text ?? "";

        public UiAutomationDriver.ProgramWatch? Watch(Window? window = null) =>
            UiAutomationDriver.WatchProgram(window ?? Window, () => Controller.CurrentStop);

        public void Dispose()
        {
            if (Controller.State == DebugState.Paused)
                Controller.Continue();
            Runner.EndProject();
            Pump();
        }
    }

    // The time slice is -1 by default, so every statement yields and is held; null leaves DoEvents a loop's only yield.
    private static Run Start(string code, string? form2Code = null, int? timeSliceMs = -1, DISetup? composition = null)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        // The code follows the header in one file, so a breakpoint's line counts the header, as the code window does.
        var form = new FormDeserializer().Deserialize(
            project, FormText("Form1", Caption) + code.ReplaceLineEndings("\r\n"), new NullSink())!;
        project.AddForm(form);
        if (form2Code is not null)
            project.AddForm(new FormDeserializer().Deserialize(
                project, FormText("Form2", "Form2") + form2Code.ReplaceLineEndings("\r\n"), new NullSink())!);

        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>() + ": {0}");
        var controller = composition is null ? new DebugController() : (DebugController)composition.DebugController;
        controller.TimeSliceMs = timeSliceMs;
        var runner = composition?.ProjectRunnerService as ProjectRunnerService ?? new ProjectRunnerService(
            Substitute.For<IEventBus>(), Substitute.For<IWindowManager>(), Substitute.For<IProjectManager>(),
            localization, controller, Substitute.For<IBreakpointService>(), new WatchService(), new RunScope());

        VBFormRuntime? window = null;
        using var opened = Avalonia.Controls.Window.WindowOpenedEvent.AddClassHandler<VBFormRuntime>((w, _) =>
        {
            if (w.Title == Caption)
                window = w;
        });
        runner.RunProject(project);
        Pump();
        window.Should().NotBeNull("the startup form opens before anything is clicked");
        return new Run { Runner = runner, Controller = controller, Window = window! };
    }

    private static void Pump()
    {
        for (var i = 0; i < 20; i++)
            Dispatcher.UIThread.RunJobs();
    }

    // The headless host keeps no list of its windows, so the tests track windows as they open and close.
    private static readonly System.Collections.Generic.HashSet<Window> Shown;

    // Explicit, so the list starts before the first test opens anything rather than when it is first read.
    static ProgramWaitTests() => Shown = Track();

    private static System.Collections.Generic.HashSet<Window> Track()
    {
        var shown = new System.Collections.Generic.HashSet<Window>();
        Avalonia.Controls.Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => shown.Add(w));
        Avalonia.Controls.Window.WindowClosedEvent.AddClassHandler<Window>((w, _) => shown.Remove(w));
        return shown;
    }

    private static Window[] OpenWindows() => [.. Shown.Where(w => w.IsVisible)];

    private static int LineOf(string code, string marker)
    {
        var headerLines = FormText("Form1", Caption).Split("\r\n").Length - 1;
        var index = code.ReplaceLineEndings("\n").Split('\n').ToList().FindIndex(l => l.Contains(marker));
        index.Should().BeGreaterThanOrEqualTo(0, $"the code holds '{marker}'");
        return headerLines + index + 1;
    }

    // The handler's last statement is what a caller reads next.
    private const string SlowClick = """
        Private Sub Command1_Click()
            Dim i As Integer
            For i = 1 To 20
                Text1.Text = "working " & i
            Next i
            Text1.Text = "done"
        End Sub
        """;

    // Runs on well past a short limit, then ends of itself.
    private const string LongClick = """
        Private Sub Command1_Click()
            Dim t As Single, i As Long
            t = Timer
            Do While Timer < t + 2
                i = i + 1
            Loop
        End Sub
        """;

    [AvaloniaFact]
    public async Task An_action_replies_once_the_handler_it_set_off_has_finished()
    {
        using var run = Start(SlowClick);
        var watch = run.Watch();
        var outcome = UiAutomationDriver.Interact(run.Named("Command1"), "invoke", null);
        run.Text.Should().NotBe("done", "the handler yields at every statement, so it is part way through");

        outcome = await UiAutomationDriver.WaitForProgramAsync(watch, outcome);

        outcome.Success.Should().BeTrue(outcome.Error);
        run.Text.Should().Be("done");
        outcome.Detail.Should().Be("invoked Button 'Command1'");
    }

    [AvaloniaFact]
    public async Task A_handler_still_running_at_the_limit_is_named_in_the_reply()
    {
        using var run = Start(LongClick);
        var watch = run.Watch();
        var outcome = UiAutomationDriver.Interact(run.Named("Command1"), "invoke", null);

        outcome = await UiAutomationDriver.WaitForProgramAsync(watch, outcome, TimeSpan.FromMilliseconds(200));

        outcome.Success.Should().BeTrue(outcome.Error);
        outcome.Detail.Should().Be("invoked Button 'Command1' (the program was still running Command1_Click 0.2s later, "
                                   + "so the form may still change)");
    }

    // A window over the form that is not the program's own box, such as the IDE's compile error from another event, may
    // not hold the handler, so it is not said to (#1253).
    [AvaloniaFact]
    public async Task A_window_over_a_busy_form_that_is_not_a_MsgBox_is_not_said_to_hold_the_handler()
    {
        using var run = Start(LongClick);
        var watch = run.Watch();
        var outcome = UiAutomationDriver.Interact(run.Named("Command1"), "invoke", null);
        var shown = new Window { Title = "HexIDE" };
        _ = shown.ShowDialog(run.Window);
        try
        {
            outcome = await UiAutomationDriver.WaitForProgramAsync(watch, outcome, TimeSpan.FromSeconds(30));

            outcome.Detail.Should().Be("invoked Button 'Command1' (the program is still running Command1_Click, and has a "
                                       + "window open over its form, 'HexIDE'; it may hold the program until it is "
                                       + "answered, so the form may change after that)");
        }
        finally { shown.Close(); }
    }

    // A MsgBox holds its handler until it is answered, so the click that raised it is answered at once; and the click
    // that answers it waits for the rest of the handler, though the box has closed and lost its owner by then.
    [AvaloniaFact]
    public async Task A_MsgBox_is_reported_at_once_and_answering_it_waits_for_the_rest_of_the_handler()
    {
        using var run = Start("""
            Private Sub Command1_Click()
                Text1.Text = "asking"
                MsgBox "Proceed?", vbOKOnly, "Question"
                Dim i As Integer
                For i = 1 To 20
                    Text1.Text = "answered " & i
                Next i
            End Sub
            """);
        var watch = run.Watch();
        var outcome = UiAutomationDriver.Interact(run.Named("Command1"), "invoke", null);

        var started = System.Diagnostics.Stopwatch.StartNew();
        outcome = await UiAutomationDriver.WaitForProgramAsync(watch, outcome, TimeSpan.FromSeconds(30));

        started.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
        outcome.Detail.Should().Be("invoked Button 'Command1' (the program is still running Command1_Click, and has a "
                                   + "window open over its form, 'Question' (a message box: \"Proceed?\", OK); a MsgBox holds its handler until it is "
                                   + "answered, so the form may change after that)");

        Pump();
        var box = OpenWindows().Single(w => w.Content is HexIDE.Runtime.Dialogs.MessageBox);
        var ok = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(box).OfType<Button>().First();
        var answering = run.Watch(box);
        outcome = UiAutomationDriver.Interact(ok, "invoke", null);
        box.IsVisible.Should().BeFalse("the box closes as its button is clicked");

        outcome = await UiAutomationDriver.WaitForProgramAsync(answering, outcome, TimeSpan.FromSeconds(30));

        outcome.Success.Should().BeTrue(outcome.Error);
        outcome.Detail.Should().NotContain("(");
        run.Text.Should().Be("answered 20");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_MsgBox_reply_names_only_existing_handlers_and_distinguishes_the_queue(bool handlesChange)
    {
        using var run = Start("""
            Private Sub Command1_Click()
                MsgBox "Proceed?", vbOKOnly, "Question"
            End Sub
            """ + (handlesChange ? """

            Private Sub Text1_Change()
                Command1.Caption = "changed"
            End Sub
            """ : ""));
        UiAutomationDriver.Interact(run.Named("Command1"), "invoke", null);
        Pump();
        try
        {
            var watch = run.Watch();
            run.Named("Text1").Focus();
            var outcome = SetTextForWait(run.Named("Text1"), "typed");
            outcome = await UiAutomationDriver.WaitForProgramAsync(watch, outcome);

            outcome.Detail.Should().Contain("still running Command1_Click"
                + (handlesChange ? ", with Text1_Change waiting to run" : "")
                + ", and has a window open");
            outcome.Detail.Should().NotContain("GotFocus").And.NotContain("LostFocus");
            if (!handlesChange)
                outcome.Detail.Should().NotContain("Text1_Change");
        }
        finally
        {
            foreach (var box in OpenWindows().Where(w => w.Content is HexIDE.Runtime.Dialogs.MessageBox))
                box.Close();
            Pump();
        }
        ((Button)run.Named("Command1")).Content.Should().Be(handlesChange ? "changed" : "Command1");
    }

    // A form shown modally is the program waiting for input, so an action on it is not held to the limit, and the
    // action that showed it says it opened.
    [AvaloniaFact]
    public async Task A_form_shown_modally_is_named_and_actions_on_it_do_not_wait_out_the_limit()
    {
        using var run = Start("""
            Private Sub Command1_Click()
                Form2.Show vbModal
                Text1.Text = "back"
            End Sub
            """, form2Code: """
            Private Sub Command1_Click()
                Text1.Text = "clicked"
            End Sub
            """);
        var watch = run.Watch();
        var outcome = UiAutomationDriver.Interact(run.Named("Command1"), "invoke", null);

        var started = System.Diagnostics.Stopwatch.StartNew();
        outcome = await UiAutomationDriver.WaitForProgramAsync(watch, outcome, TimeSpan.FromSeconds(30));

        started.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
        outcome.Detail.Should().Be("invoked Button 'Command1' (the program opened 'Form2')");

        var form2 = OpenWindows().OfType<VBFormRuntime>().Single(w => w.Title == "Form2");
        var onForm2 = run.Watch(form2);
        outcome = UiAutomationDriver.Interact(Run.Named(form2, "Command1"), "invoke", null);
        started.Restart();
        outcome = await UiAutomationDriver.WaitForProgramAsync(onForm2, outcome, TimeSpan.FromSeconds(30));

        started.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
        outcome.Detail.Should().Be("invoked Button 'Command1'");
        ((TextBox)Run.Named(form2, "Text1")).Text.Should().Be("clicked");
        run.Text.Should().NotBe("back", "Form2 is still up");
    }

    [AvaloniaTheory]
    [InlineData("Standard.Break")]
    [InlineData("Debug.Break")]
    public async Task Break_remains_enabled_with_focus_in_a_running_DoEvents_loop(string id)
    {
        var composition = new DISetup();
        var shell = ToolbarAutomationNameTests.ShowMainView();
        var main = (MainView)shell.Content!;
        main.DataContext = composition.Root;
        // Show both toolbars locally without persisting a settings change.
        foreach (var toolbar in shell.GetVisualDescendants().OfType<Control>().Where(c =>
                     Avalonia.Automation.AutomationProperties.GetAutomationId(c) is "StandardToolbar" or "DebugToolbar"))
            toolbar.IsVisible = true;
        try
        {
            using var run = Start("""
                Private Sub Command1_Click()
                    Do
                        DoEvents
                    Loop
                End Sub
                """, composition: composition);
            var button = shell.GetVisualDescendants().OfType<Button>().Single(b =>
                Avalonia.Automation.AutomationProperties.GetAutomationId(b) == id);
            var command = run.Named("Command1");
            command.Focus().Should().BeTrue();
            UiAutomationDriver.Interact(command, "invoke", null).Success.Should().BeTrue();
            await Task.Delay(100);

            run.Runner.CanBreakProject.Should().BeTrue();
            button.IsEffectivelyEnabled.Should().BeTrue("Break must remain available while the program has focus");
            var outcome = UiAutomationDriver.Interact(button, "invoke", null);
            outcome.Success.Should().BeTrue(outcome.Error);
            await WaitFor(() => run.Controller.State == DebugState.Paused && !button.IsEffectivelyEnabled);
            run.Controller.State.Should().Be(DebugState.Paused);
            button.IsEffectivelyEnabled.Should().BeFalse();
            run.Controller.Continue();
            await WaitFor(() => button.IsEffectivelyEnabled);
            button.IsEffectivelyEnabled.Should().BeTrue();
            run.Runner.EndProject();
            await WaitFor(() => !button.IsEffectivelyEnabled);
            button.IsEffectivelyEnabled.Should().BeFalse();
        }
        finally { shell.Close(); }

        static async Task WaitFor(Func<bool> ready)
        {
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            while (!ready() && elapsed.Elapsed < TimeSpan.FromSeconds(10))
                await Task.Delay(10);
            ready().Should().BeTrue("the posted command state must reach the toolbar");
        }
    }

    // A handler that loops on DoEvents is the program's message loop: what an action sets off runs inside it.
    [AvaloniaFact]
    public async Task A_handler_looping_on_DoEvents_does_not_hold_a_reply_to_the_limit()
    {
        using var run = Start("""
            Private Sub Command1_Click()
                Do
                    DoEvents
                Loop Until Text1.Text = "stop"
            End Sub

            Private Sub Text1_Change()
                Command1.Caption = "changed"
            End Sub
            """);
        var outcome = UiAutomationDriver.Interact(run.Named("Command1"), "invoke", null);
        outcome.Success.Should().BeTrue(outcome.Error);
        // Not Pump(): a DoEvents loop keeps the dispatcher's queue from ever running dry.
        await Task.Delay(100);
        try
        {
            var watch = run.Watch();
            outcome = UiAutomationDriver.Interact(run.Named("Text1"), "set_value", "typed");
            var started = System.Diagnostics.Stopwatch.StartNew();
            outcome = await UiAutomationDriver.WaitForProgramAsync(watch, outcome, TimeSpan.FromSeconds(30));

            started.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "the loop goes on, but it is waiting for input");
            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Detail.Should().NotContain("(");
            ((Button)run.Named("Command1")).Content.Should().Be("changed", "the change ran inside the loop's DoEvents");
        }
        finally { ((TextBox)run.Named("Text1")).Text = "stop"; }
    }

    // A loop the action started is the action's work, DoEvents or not: replied to at its first DoEvents, with no note,
    // the reply read as a finished handler while the form was half way through (#1254). With no time slice, DoEvents is
    // the loop's only yield, so the wait cannot land on a held one by luck.
    [AvaloniaFact]
    public async Task A_DoEvents_loop_the_action_started_is_waited_for()
    {
        using var run = Start("""
            Private Sub Command1_Click()
                Dim i As Integer
                For i = 1 To 200
                    Text1.Text = "working " & i
                    DoEvents
                Next i
                Text1.Text = "done"
            End Sub
            """, timeSliceMs: null);
        var watch = run.Watch();

        var outcome = UiAutomationDriver.Interact(run.Named("Command1"), "invoke", null);
        outcome = await UiAutomationDriver.WaitForProgramAsync(watch, outcome, TimeSpan.FromSeconds(30));

        outcome.Success.Should().BeTrue(outcome.Error);
        outcome.Detail.Should().Be("invoked Button 'Command1'");
        run.Text.Should().Be("done");
    }

    // One that outlasts the limit gets the note a loop with no DoEvents gets.
    [AvaloniaFact]
    public async Task A_DoEvents_loop_the_action_started_that_outlasts_the_limit_is_noted()
    {
        using var run = Start("""
            Private Sub Command1_Click()
                Do
                    DoEvents
                Loop Until Text1.Text = "stop"
            End Sub
            """, timeSliceMs: null);
        var watch = run.Watch();
        try
        {
            var outcome = UiAutomationDriver.Interact(run.Named("Command1"), "invoke", null);
            outcome = await UiAutomationDriver.WaitForProgramAsync(watch, outcome, TimeSpan.FromMilliseconds(200));

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Detail.Should().Be("invoked Button 'Command1' (the program was still running Command1_Click 0.2s later, "
                                       + "so the form may still change)");
        }
        finally { ((TextBox)run.Named("Text1")).Text = "stop"; }
    }

    // A loop the action found running is the program's message loop wherever the action finds it: caught between two
    // DoEvents, it is the same loop when it parks at the next, not one the action started (#1254).
    [AvaloniaFact]
    public async Task A_DoEvents_loop_caught_between_two_DoEvents_is_still_the_program_s_loop()
    {
        using var run = Start("""
            Private Sub Command1_Click()
                Dim n As Long
                Do
                    n = n + 1
                    n = n + 1
                    n = n + 1
                    n = n + 1
                    DoEvents
                Loop Until Text1.Text = "stop"
            End Sub

            Private Sub Text1_Change()
                Command1.Caption = "changed"
            End Sub
            """);
        UiAutomationDriver.Interact(run.Named("Command1"), "invoke", null).Success.Should().BeTrue();
        await Task.Delay(100);
        try
        {
            var watch = run.Watch()!;
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (watch.Pump.AtDoEvents.Count > 0)
            {
                deadline.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "the loop spends most of its time between DoEvents");
                await Task.Delay(1);
                watch = run.Watch()!;
            }
            var outcome = UiAutomationDriver.Interact(run.Named("Text1"), "set_value", "typed");
            var started = System.Diagnostics.Stopwatch.StartNew();
            outcome = await UiAutomationDriver.WaitForProgramAsync(watch, outcome, TimeSpan.FromSeconds(30));

            started.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "the loop goes on, but it is waiting for input");
            outcome.Detail.Should().NotContain("(");
        }
        finally { ((TextBox)run.Named("Text1")).Text = "stop"; }
    }

    // A handler waiting at a MsgBox is running before the answer, but it is not a loop: the DoEvents loop the answer sets
    // going is the answer's work, waited for, where it was replied to at its first DoEvents (#1254 review).
    [AvaloniaFact]
    public async Task A_DoEvents_loop_set_going_by_answering_a_MsgBox_is_waited_for()
    {
        using var run = Start("""
            Private Sub Command1_Click()
                MsgBox "Go?", vbOKOnly, "Question"
                Dim i As Integer
                For i = 1 To 200
                    Text1.Text = "working " & i
                    DoEvents
                Next i
                Text1.Text = "done"
            End Sub
            """, timeSliceMs: null);
        var watch = run.Watch();
        var outcome = UiAutomationDriver.Interact(run.Named("Command1"), "invoke", null);
        await UiAutomationDriver.WaitForProgramAsync(watch, outcome, TimeSpan.FromSeconds(30));

        Pump();
        var box = OpenWindows().Single(w => w.Content is HexIDE.Runtime.Dialogs.MessageBox);
        var ok = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(box).OfType<Button>().First();
        var answering = run.Watch(box);
        outcome = UiAutomationDriver.Interact(ok, "invoke", null);
        outcome = await UiAutomationDriver.WaitForProgramAsync(answering, outcome, TimeSpan.FromSeconds(30));

        outcome.Success.Should().BeTrue(outcome.Error);
        outcome.Detail.Should().NotContain("(");
        run.Text.Should().Be("done");
    }

    // An earlier DoEvents does not make the loop after a window wait part of the program's old message loop (#1318).
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_loop_after_a_window_wait_is_waited_for_even_after_an_earlier_DoEvents(bool modal)
    {
        using var run = Start("""
            Private Sub Command1_Click()
                DoEvents
                WINDOW_WAIT
                Dim i As Integer
                For i = 1 To 200
                    Text1.Text = "working " & i
                    DoEvents
                Next i
                Text1.Text = "done"
            End Sub
            """.Replace("WINDOW_WAIT", modal ? "Form2.Show vbModal" : "MsgBox \"Go?\", vbOKOnly, \"Question\""),
            form2Code: modal ? "" : null, timeSliceMs: null);
        var outcome = UiAutomationDriver.Interact(run.Named("Command1"), "invoke", null);
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!OpenWindows().Any(w => modal ? w.Title == "Form2" : w.Content is HexIDE.Runtime.Dialogs.MessageBox))
        {
            deadline.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
            await Task.Delay(1);
        }
        Pump();
        var window = OpenWindows().Single(w => modal ? w.Title == "Form2" : w.Content is HexIDE.Runtime.Dialogs.MessageBox);
        var watch = run.Watch(window);
        if (modal)
        {
            outcome = UiAutomationDriver.Interact(window, "close", null);
        }
        else
        {
            var ok = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Button>().First();
            outcome = UiAutomationDriver.Interact(ok, "invoke", null);
        }
        outcome = await UiAutomationDriver.WaitForProgramAsync(watch, outcome, TimeSpan.FromSeconds(30));

        outcome.Success.Should().BeTrue(outcome.Error);
        outcome.Detail.Should().NotContain("(");
        run.Text.Should().Be("done");
    }

    // A time slice makes the program busy, but does not make an old unrelated dialog hold its handler (#1318).
    [AvaloniaFact]
    public async Task An_old_dialog_does_not_hold_a_reply_when_the_watch_finds_a_busy_loop()
    {
        using var run = Start("""
            Private Sub Command1_Click()
                Dim n As Long
                Do
                    n = n + 1
                    n = n + 1
                    n = n + 1
                    n = n + 1
                    DoEvents
                Loop Until Command1.Caption = "stop"
            End Sub

            Private Sub Text1_Change()
                Command1.Caption = "done"
            End Sub
            """);
        var earlier = new Window { Title = "HexIDE" };
        _ = earlier.ShowDialog(run.Window);
        ClickForWait(run.Named("Command1"));
        await Task.Delay(100);
        try
        {
            var watch = run.Watch()!;
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (watch.Pump.AtDoEvents.Count > 0)
            {
                deadline.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
                await Task.Delay(1);
                watch = run.Watch()!;
            }
            watch.Pump.IsIdle.Should().BeFalse();
            var outcome = SetTextForWait(run.Named("Text1"), "done");
            outcome = await UiAutomationDriver.WaitForProgramAsync(watch, outcome, TimeSpan.FromSeconds(30));

            outcome.Detail.Should().NotContain("'HexIDE'");
            ((Button)run.Named("Command1")).Content.Should().Be("done");
            run.Text.Should().Be("done");
        }
        finally
        {
            ((Button)run.Named("Command1")).Content = "stop";
            earlier.Close();
        }
    }

    // A Timer's tick is no action's work, though it may come after one: its DoEvents loop does not hold the reply to the
    // limit, as it did once a reply waited for loops it had not found running (#1254 review). Raised on the pump directly,
    // as a Timer raises it: a headless Timer does not tick within a click.
    [AvaloniaFact]
    public async Task A_Timer_s_DoEvents_loop_that_starts_after_the_action_does_not_hold_the_reply()
    {
        using var run = Start("""
            Private Sub Command1_Click()
                Text1.Text = "clicked"
            End Sub
            """, timeSliceMs: null);
        var stop = false;
        try
        {
            var watch = run.Watch()!;
            var outcome = UiAutomationDriver.Interact(run.Named("Command1"), "invoke", null);
            watch.Pump.Raise("Timer1_Timer", async () =>
            {
                while (!stop)
                    await watch.Pump.DoEvents();
            }, HexIDE.Runtime.EventCoalescing.Timer);
            outcome = await UiAutomationDriver.WaitForProgramAsync(watch, outcome, TimeSpan.FromSeconds(2));

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Detail.Should().Be("invoked Button 'Command1'");
            watch.Pump.Busy.Should().Contain("Timer1_Timer", "the tick's loop runs on");
        }
        finally { stop = true; }
    }

    // A loop the action started inside a loop already running outlasts the limit: the note names what the action set off,
    // not the loop it found, a game loop say, which would be named on every reply (#1254 review).
    [AvaloniaFact]
    public async Task A_note_names_the_loop_the_action_started_not_the_one_it_found()
    {
        using var run = Start("""
            Private Sub Command1_Click()
                Do
                    DoEvents
                Loop Until Text1.Text = "stop"
            End Sub

            Private Sub Text1_Change()
                If Text1.Text <> "go" Then Exit Sub
                Do
                    DoEvents
                Loop Until Text1.Text = "stop"
            End Sub
            """, timeSliceMs: null);
        UiAutomationDriver.Interact(run.Named("Command1"), "invoke", null).Success.Should().BeTrue();
        await Task.Delay(100);
        try
        {
            var watch = run.Watch();
            var outcome = UiAutomationDriver.Interact(run.Named("Text1"), "set_value", "go");
            outcome = await UiAutomationDriver.WaitForProgramAsync(watch, outcome, TimeSpan.FromMilliseconds(300));

            outcome.Detail.Should().EndWith("(the program was still running Text1_Change 0.3s later, so the form may still change)");
        }
        finally { ((TextBox)run.Named("Text1")).Text = "stop"; }
    }

    // A program stopped at a breakpoint goes no further until it is continued, so the reply says where at once.
    [AvaloniaFact]
    public async Task A_handler_stopped_at_a_breakpoint_is_reported_at_once_with_where()
    {
        const string code = """
            Private Sub Command1_Click()
                Text1.Text = "before"
                Text1.Text = "after" ' break here
            End Sub
            """;
        using var run = Start(code);
        run.Controller.SetBreakpoints("Form1", [LineOf(code, "' break here")]);
        var watch = run.Watch();
        var outcome = UiAutomationDriver.Interact(run.Named("Command1"), "invoke", null);

        var started = System.Diagnostics.Stopwatch.StartNew();
        outcome = await UiAutomationDriver.WaitForProgramAsync(watch, outcome, TimeSpan.FromSeconds(30));

        started.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
        outcome.Detail.Should().Be($"invoked Button 'Command1' (the program is paused at Form1 line "
                                   + $"{LineOf(code, "' break here")} (Breakpoint), with Command1_Click not finished; it "
                                   + "runs on, and the form may change, once the program continues)");
    }

    // A VB event runs through the program's queue, after interact returns, so what its Change handler makes of the
    // value is read once the wait finds the program idle; read at once, the reply named a value already rewritten (#1280).
    [AvaloniaFact]
    public async Task A_value_the_program_rewrites_is_read_once_it_has_run()
    {
        using var run = Start("""
            Private Sub Text1_Change()
                If Text1.Text <> UCase(Text1.Text) Then Text1.Text = UCase(Text1.Text)
            End Sub
            """);
        var watch = run.Watch();
        var outcome = UiAutomationDriver.Interact(run.Named("Text1"), "set_value", "abc");
        outcome.Settled.Should().NotBeNull("a running form's value is read once its program has run");

        outcome = await UiAutomationDriver.WaitForProgramAsync(watch, outcome, TimeSpan.FromSeconds(30));

        outcome.Success.Should().BeTrue(outcome.Error);
        outcome.Detail.Should().Be("set value of 'Edit' to 'abc', and the program then made it 'ABC'");
        run.Text.Should().Be("ABC");
    }

    [AvaloniaFact]
    public async Task A_compile_error_from_a_click_is_named_though_it_leaves_the_program_idle()
    {
        // The IDE shows a compile error over the form and does not await it, so the walk ends and the program is idle
        // with the dialog up; the wait returns at idle naming only forms, and the reply came back clean (#1253).
        using var run = Start("""
            Private Sub Command1_Click()
                Text1.Text = "before"
                NoSuchSub
            End Sub
            """);
        Window? shown = null;
        void Report(VBWindowContext context, HexIDE.Runtime.Interpreter.VBCompileErrorException e)
        {
            shown = new Window { Title = "HexIDE", Content = e.Message };
            _ = shown.ShowDialog(run.Window);
        }
        VBWindowContext.CompileError += Report;
        try
        {
            var dialogs = OpenDialogs.Watch(OpenWindows(), run.Window);
            OpenDialogs.During(dialogs);
            var watch = run.Watch();
            var outcome = UiAutomationDriver.Interact(run.Named("Command1"), "invoke", null);
            outcome = await UiAutomationDriver.WaitForProgramAsync(watch, outcome);
            Pump();

            shown.Should().NotBeNull("the click reaches the call to a Sub that does not exist");
            run.Window.Events!.IsIdle.Should().BeTrue();
            outcome.Detail.Should().Be("invoked Button 'Command1'");
            OpenDialogs.Note(dialogs, OpenWindows()).Should()
                .Contain($"'HexIDE', a modal dialog over the running form '{Caption}', opened during this call");
        }
        finally
        {
            VBWindowContext.CompileError -= Report;
            shown?.Close();
        }
    }

    // These wait fixtures deliberately start a handler behind an existing dialog. interact now refuses that (#2184);
    // raise the event directly to keep testing the wait and its note independently of the input guard.
    private static InteractOutcome ClickForWait(Control control)
    {
        control.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        return new InteractOutcome(true, "test-event", "Raised Click", null);
    }

    // Like ClickForWait, arrange a programmatic change while a modal dialog blocks input.
    private static InteractOutcome SetTextForWait(Control control, string text)
    {
        ((TextBox)control).Text = text;
        return new InteractOutcome(true, "test-event", "Set Text", null);
    }

    // An error dialog an earlier call left open over the form, and a MsgBox this click raised: the wait names the MsgBox,
    // which holds the handler, and the note the dialog; each once (#1253).
    [AvaloniaFact]
    public async Task A_MsgBox_raised_beside_a_dialog_left_open_is_named_by_the_wait_and_the_dialog_by_the_note()
    {
        using var run = Start("""
            Private Sub Command1_Click()
                MsgBox "Proceed?", vbOKOnly, "Question"
            End Sub
            """);
        var earlier = new Window { Title = "HexIDE" };
        _ = earlier.ShowDialog(run.Window);
        Pump();
        try
        {
            var dialogs = OpenDialogs.Watch(OpenWindows(), run.Window);
            OpenDialogs.During(dialogs);
            var watch = run.Watch();
            var outcome = ClickForWait(run.Named("Command1"));
            outcome = await UiAutomationDriver.WaitForProgramAsync(watch, outcome, TimeSpan.FromSeconds(30));
            Pump();

            outcome.Detail.Should().Contain("has a window open over its form, 'Question' (a message box: \"Proceed?\", OK); a MsgBox holds its handler");
            var note = OpenDialogs.Note(dialogs, OpenWindows());
            note.Should().Contain($"'HexIDE', a modal dialog over the running form '{Caption}', is still open");
            note.Should().NotContain("'Question'");

            // Clicked again while the MsgBox holds the program: the box, which holds it, is named, not the other dialog.
            dialogs = OpenDialogs.Watch(OpenWindows(), run.Window);
            OpenDialogs.During(dialogs);
            watch = run.Watch();
            outcome = ClickForWait(run.Named("Command1"));
            outcome = await UiAutomationDriver.WaitForProgramAsync(watch, outcome, TimeSpan.FromSeconds(30));
            Pump();

            outcome.Detail.Should().Contain("has a window open over its form, 'Question' (a message box: \"Proceed?\", OK); a MsgBox holds its handler");
            OpenDialogs.Note(dialogs, OpenWindows()).Should()
                .Contain($"'HexIDE', a modal dialog over the running form '{Caption}', is still open").And.NotContain("'Question'");
        }
        finally
        {
            // Closing the box lets the second click run, and raise a box of its own.
            for (var i = 0; i < 10 && OpenWindows().Any(w => w.Content is HexIDE.Runtime.Dialogs.MessageBox); i++)
            {
                foreach (var box in OpenWindows().Where(w => w.Content is HexIDE.Runtime.Dialogs.MessageBox))
                    box.Close();
                Pump();
            }
            earlier.Close();
        }
    }

    // A running form's tick is the program's to set, in a Click that runs through its queue after the item is clicked:
    // read at once, the reply said nothing while the program ticked it (#1255). It is read once the program is idle, across
    // the form's menu, as a level menu ticks one item and unticks another, and inspect_element reads each tick back.
    [AvaloniaTheory]
    [InlineData("mnuGrid.Checked = True", "; the program ticked 'Grid'", true, true)]
    [InlineData("mnuRows.Checked = False", "; the program unticked 'Rows'", false, false)]
    [InlineData("mnuGrid.Checked = True: mnuRows.Checked = False", "; the program ticked 'Grid' and unticked 'Rows'", true, false)]
    [InlineData("", "", false, true)]
    public async Task A_running_form_s_menu_ticks_are_read_once_the_program_has_run(
        string body, string said, bool grid, bool rows)
    {
        using var run = Start($"""
            Private Sub mnuGrid_Click()
                {body}
                Text1.Text = "ran"
            End Sub
            """);
        var item = (MenuItem)run.Named("mnuGrid");
        var watch = run.Watch();

        var outcome = UiAutomationDriver.Interact(item, "invoke", null);
        outcome = await UiAutomationDriver.WaitForProgramAsync(watch, outcome, TimeSpan.FromSeconds(30));

        outcome.Success.Should().BeTrue(outcome.Error);
        outcome.Detail.Should().Be("invoked MenuItem 'Grid'" + said);
        run.Text.Should().Be("ran");
        UiAutomationDriver.Inspect(item, "Window/Menu/Grid").ToggleState.Should().Be(grid);
        UiAutomationDriver.Inspect(run.Named("mnuRows"), "Window/Menu/Rows").ToggleState.Should().Be(rows);
        UiAutomationDriver.Interact(item, "toggle", null).Error.Should()
            .Contain("its program sets the tick").And.Contain("inspect_element reads the tick");
    }

    // Whatever the program's Click has done by the time the click returns, the click itself names no tick on a running
    // form's item: the wait reads it once the program is idle, and a tick named here too would be named twice.
    [AvaloniaFact]
    public void Clicking_a_running_form_s_menu_item_names_no_tick_of_its_own()
    {
        var item = new HexIDE.Runtime.BuiltinControls.VBMenu { Header = "Grid", ToggleType = MenuItemToggleType.CheckBox };
        item.Click += (_, _) => item.Checked = true;
        var window = new Window { Content = new Menu { Items = { item } } };
        window.Show();
        try
        {
            UiAutomationDriver.ClickMenuItem(item).Should().NotContain("ticked");
            item.IsChecked.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task A_refused_action_does_not_wait()
    {
        using var run = Start(SlowClick);
        var refused = new InteractOutcome(false, "peer", null, "refused");

        var outcome = await UiAutomationDriver.WaitForProgramAsync(run.Watch(), refused, TimeSpan.FromSeconds(30));

        outcome.Should().Be(refused);
    }

    [AvaloniaFact]
    public void A_window_that_is_not_the_programs_is_not_watched()
    {
        var window = new Window();
        window.Show();
        try
        {
            UiAutomationDriver.WatchProgram(window).Should().BeNull();
        }
        finally { window.Close(); }
    }

    // ── close, as the server runs it: the close, the program's wait, then whether the window is still open (#829) ──

    private static async Task<InteractOutcome> CloseAsync(Run run, Window window)
    {
        var watch = run.Watch(window);
        var outcome = UiAutomationDriver.Interact(window, "close", null);
        outcome = await UiAutomationDriver.WaitForProgramAsync(watch, outcome, TimeSpan.FromSeconds(30));
        return UiAutomationDriver.AfterClose(window, outcome, watch);
    }

    [AvaloniaFact]
    public async Task A_close_the_program_is_still_deciding_is_not_reported_as_refused()
    {
        using var run = Start("""
            Private Sub Form_QueryUnload(Cancel As Integer, UnloadMode As Integer)
                If MsgBox("Quit?", vbYesNo, "Ask") = vbNo Then Cancel = 1
            End Sub
            """);

        var outcome = await CloseAsync(run, run.Window);

        outcome.Success.Should().BeTrue(outcome.Error);
        outcome.Detail.Should().Contain("'Ask'").And.EndWith("; it is still open while the program decides, as the note says");
        Pump();
        var box = OpenWindows().Single(w => w.Content is HexIDE.Runtime.Dialogs.MessageBox);
        var yes = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(box).OfType<Button>()
            .First(b => b.Content?.ToString()?.Replace("_", "") == "Yes");
        UiAutomationDriver.Interact(yes, "invoke", null).Success.Should().BeTrue();
        Pump();
        run.Window.IsVisible.Should().BeFalse("Yes lets the close through, once the program has decided");
    }

    [AvaloniaFact]
    public async Task A_form_its_QueryUnload_hides_is_reported_hidden_not_closed()
    {
        using var run = Start("""
            Private Sub Form_QueryUnload(Cancel As Integer, UnloadMode As Integer)
                Me.Hide
                Cancel = 1
            End Sub
            """);

        var outcome = await CloseAsync(run, run.Window);

        outcome.Detail.Should().EndWith("; it is hidden, not closed: the program behind it hid it, and it is still loaded");
    }

    [AvaloniaFact]
    public async Task A_form_that_unloads_is_reported_closed()
    {
        using var run = Start("""
            Private Sub Form_Unload(Cancel As Integer)
                Debug.Print "unloading"
            End Sub
            """);

        var outcome = await CloseAsync(run, run.Window);

        outcome.Detail.Should().EndWith("; it is closed");
        run.Window.IsVisible.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task A_form_with_a_MsgBox_open_over_it_refuses_close_as_a_person_finds()
    {
        using var run = Start("""
            Private Sub Command1_Click()
                MsgBox "Busy", vbOKOnly, "Busy"
            End Sub
            """);
        UiAutomationDriver.Interact(run.Named("Command1"), "invoke", null);
        Pump();

        var outcome = await CloseAsync(run, run.Window);

        outcome.Success.Should().BeFalse();
        outcome.Error.Should().Contain("'Busy'").And.Contain("nothing was done");
        run.Window.IsVisible.Should().BeTrue();
        var box = OpenWindows().Single(w => w.Content is HexIDE.Runtime.Dialogs.MessageBox);
        box.IsVisible.Should().BeTrue("the dialog is not closed with its owner");
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(box).OfType<Button>().First()
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Pump();
    }
}
