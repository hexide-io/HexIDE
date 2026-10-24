using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A running program that reaches something HexIDE does not implement used to stop without a word: the
/// exception reached only the log, F5 on a Sub Main project opened nothing, and a click handler simply
/// ended part way while the program carried on (hexide-io/HexIDE#754).
///
/// <para>
/// These start where F5 starts, at <see cref="ProjectRunnerService.RunProject"/>, with the real form loader and
/// a real Click routed to a real button. The runner does not hand out the form's window, so it is picked up
/// as it opens, by a caption no other test uses.
/// </para>
///
/// <para>
/// <c>GoSub</c> and fixed-length strings stand for any construct the interpreter does not implement. They were
/// chosen as the least likely to be implemented soon (<c>GoSub</c> is the tree-walk limit in
/// <c>interpreter-gaps.md</c>). If either is implemented, substitute another.
/// </para>
/// </summary>
public class RunFaultTests
{
    private const string Caption = "RunFaultTests";

    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    private static readonly string FormWithButton =
        "VERSION 5.00\r\n"
      + "Begin VB.Form Form1 \r\n"
      + $"   Caption         =   \"{Caption}\"\r\n"
      + "   ClientHeight    =   3000\r\n"
      + "   ClientWidth     =   4000\r\n"
      + "   Begin VB.CommandButton Command1 \r\n"
      + "      Caption         =   \"Command1\"\r\n"
      + "      Height          =   495\r\n"
      + "      Left            =   120\r\n"
      + "      Top             =   120\r\n"
      + "      Width           =   1215\r\n"
      + "   End\r\n"
      + "End\r\n"
      + "Attribute VB_Name = \"Form1\"\r\n";

    private static readonly string FormWithButtonAndTextBox = FormWithButton.Replace(
        "End\r\nAttribute",
        "   Begin VB.TextBox Text1 \r\n"
      + "      Height          =   375\r\n"
      + "      Left            =   120\r\n"
      + "      Top             =   840\r\n"
      + "      Width           =   1215\r\n"
      + "   End\r\n"
      + "End\r\nAttribute");

    /// <summary>Reaches GoSub with an error trap set, so a trap that swallowed it would print "after".</summary>
    private static string ReachesGoSub(string procedure) =>
        $"Private Sub {procedure}()\r\n"
      + "    On Error Resume Next\r\n"
      + "    Debug.Print \"before\"\r\n"
      + "    GoSub Later\r\n"
      + "    Debug.Print \"after\"\r\n"
      + "    Exit Sub\r\n"
      + "Later:\r\n"
      + "    Return\r\n"
      + "End Sub\r\n";

    private sealed class Run
    {
        public required ProjectRunnerService Runner { get; init; }
        public required DebugController Controller { get; init; }
        public required IWindowManager WindowManager { get; init; }
        public required ProjectDefinition Project { get; init; }
        public List<string> StartFailed { get; } = new();
        public List<string> RunFailed { get; } = new();
        // Where each RunFailed error was raised, as the runner says while it reports it (#955).
        public List<HexIDE.Runtime.Interpreter.ErrorLocation?> RunFailedAt { get; } = new();
    }

    private static Run Build(ProjectDefinition project)
    {
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.ProjectRunner.StartupFormFailedToLoad").Returns("Form '{0}' failed: {1}");
        localization.GetString("Str.ProjectRunner.NotImplementedYet").Returns("Not implemented yet: {0}");

        var windowManager = Substitute.For<IWindowManager>();
        // A yield at every statement: the worst case for the time slice. A yield inside an event procedure that another
        // statement raised used to let that statement run on before the procedure had finished, which made the
        // nested-event test below fail on a busy box and pass on an idle one. Since #766 the raiser waits, so these run
        // with the slice at its most disruptive rather than off.
        var controller = new DebugController { TimeSliceMs = -1 };
        var runner = new ProjectRunnerService(
            Substitute.For<IEventBus>(), windowManager, Substitute.For<IProjectManager>(),
            localization, controller, Substitute.For<IBreakpointService>(), new WatchService(), new RunScope());
        var run = new Run { Runner = runner, Controller = controller, WindowManager = windowManager, Project = project };
        runner.StartFailed += run.StartFailed.Add;
        runner.RunFailed += run.RunFailed.Add;
        runner.RunFailed += _ => run.RunFailedAt.Add(runner.FailureLocation);
        return run;
    }

    private static Run FormProject(string code, string? frm = null)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, frm ?? FormWithButton, NullSink.Instance)!;
        form.UpdateCode(code);
        project.AddForm(form);
        return Build(project);
    }

    private static Run SubMainProject(string code)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode(code);
        project.AddModule(module);
        project.StartsAtSubMain = true;
        return Build(project);
    }

    /// <summary>Starts the run and returns the form's window once it is up, with what it prints captured.</summary>
    private static VBFormRuntime Start(Run run, List<string> printed)
    {
        VBFormRuntime? window = null;
        using var opened = Window.WindowOpenedEvent.AddClassHandler<VBFormRuntime>((w, _) =>
        {
            if (w.Title == Caption)
                window = w;
        });
        run.Runner.RunProject(run.Project);
        Pump();
        window.Should().NotBeNull("the startup form opens before anything is clicked");
        return window!;
    }

    private static void Click(VBFormRuntime window)
    {
        var button = window.GetLogicalDescendants().OfType<VBCommandButton>().Single();
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
    }

    // Everything here completes on the UI thread; the run-task's teardown is a continuation queued behind
    // the window closing. A few passes drain it without depending on how many hops it takes.
    private static void Pump()
    {
        for (var i = 0; i < 10; i++)
            Dispatcher.UIThread.RunJobs();
    }

    private static void AssertNothingIsRunning(Run run)
    {
        run.Runner.IsRunning.Should().BeFalse();
        run.Runner.RunningProject.Should().BeNull("a run that ended must not stay claimed");
        run.Controller.State.Should().Be(DebugState.Stopped);
        run.Controller.IsSessionActive.Should().BeFalse();
    }

    private static List<string> Printed(out Action<string> sink)
    {
        var printed = new List<string>();
        sink = printed.Add;
        return printed;
    }

    // A never-called handler must not stop Form_Load; reaching Print uses Me.Print's refusal (#993).
    [AvaloniaTheory]
    [InlineData("Print \"hello\"")]
    [InlineData("Print")]
    [InlineData("Print \"a\"; \"b\"")]
    [InlineData("Print \"a\",")]
    [InlineData("Print Spc(2); \"x\"")]
    [InlineData("Me.Print \"hello\"")]
    public void BarePrint_LoadsUntilReached(string statement)
    {
        var run = FormProject("Private Sub Form_Load()\r\nDebug.Print \"loaded\"\r\nEnd Sub\r\n"
            + $"Private Sub Command1_Click()\r\n{statement}\r\nDebug.Print \"after\"\r\nEnd Sub\r\n");
        var printed = Printed(out var sink);
        VBDebugConsole.Output += sink;
        try
        {
            var window = Start(run, printed);
            run.StartFailed.Should().BeEmpty();
            run.Runner.IsRunning.Should().BeTrue();
            printed.Should().Equal("loaded");
            Click(window);
            run.RunFailed.Should().ContainSingle().Which.Should()
                .Be("Not implemented yet: The Print method is not implemented for Form1");
            printed.Should().Equal("loaded");
            AssertNothingIsRunning(run);
        }
        finally
        {
            VBDebugConsole.Output -= sink;
            run.Runner.EndProject();
        }
    }

    // ── a form's event procedures ────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void A_click_handler_that_reaches_something_unimplemented_ends_the_run_and_says_so()
    {
        var run = FormProject(ReachesGoSub("Command1_Click"));
        var printed = Printed(out var sink);
        VBDebugConsole.Output += sink;
        try
        {
            var window = Start(run, printed);
            var closed = false;
            window.Closed += (_, _) => closed = true;

            Click(window);

            run.RunFailed.Should().ContainSingle("the fault is reported once, after the run is torn down")
                .Which.Should().StartWith("Not implemented yet: ").And.Contain("GoSub");
            run.StartFailed.Should().BeEmpty("the run started; it was the click that ended it");
            printed.Should().Equal(["before"], "On Error Resume Next must not step over what HexIDE cannot run");
            closed.Should().BeTrue("ending the run closes its form");
            AssertNothingIsRunning(run);
        }
        finally
        {
            VBDebugConsole.Output -= sink;
            run.Runner.EndProject();
        }
    }

    [AvaloniaTheory]
    [InlineData("    Text1.Text = \"x\"\r\n    Debug.Print \"the click carried on\"\r\n")]
    // A joined single-line If tail gets no pause-gate per statement, so it needs its own abort check.
    [InlineData("    If True Then Text1.Text = \"x\": Debug.Print \"the click carried on\"\r\n")]
    public void A_fault_in_an_event_another_procedure_raised_stops_that_procedure_too(string click)
    {
        // Setting Text1.Text raises Text1_Change there and then, and its fault is caught where Text1_Change was
        // dispatched, not in Command1_Click. Unless the whole run is stopped, the click carries on past a
        // statement whose effects never happened.
        //
        // That used to hold only while Text1_Change had not yielded its time slice before faulting. If it had, the
        // setter returned and the click ran to its end before the fault arrived (#766). Now the click waits for the
        // handler it raised, and Build yields at every statement to prove it.
        var run = FormProject(
            "Private Sub Command1_Click()\r\n"
          + click
          + "End Sub\r\n"
          + "\r\n"
          + ReachesGoSub("Text1_Change"),
            FormWithButtonAndTextBox);
        var printed = Printed(out var sink);
        VBDebugConsole.Output += sink;
        try
        {
            var window = Start(run, printed);

            Click(window);

            printed.Should().NotContain("the click carried on", "the click must stop with the event it raised");
            run.RunFailed.Should().ContainSingle().Which.Should().Contain("GoSub");
            AssertNothingIsRunning(run);
        }
        finally
        {
            VBDebugConsole.Output -= sink;
            run.Runner.EndProject();
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_function_whose_last_statement_raised_the_ending_event_hands_its_caller_nothing(bool end)
    {
        // Fn's last statement raises Text1_Change, which reaches End or faults. The event's boundary takes that as a
        // clean stop, so the setter returns and Fn has no statement left to gate. Unless Fn's return stops it, the
        // click's own expression carries on with Fn's result (#775 for End, #762 for a fault).
        var change = end
            ? "Private Sub Text1_Change()\r\n    End\r\nEnd Sub\r\n"
            : ReachesGoSub("Text1_Change");
        var run = FormProject(
            "Private Function Fn() As String\r\n"
          + "    Fn = \"result\"\r\n"
          + "    Text1.Text = \"x\"\r\n"
          + "End Function\r\n"
          + "\r\n"
          + "Private Sub Command1_Click()\r\n"
          + "    Debug.Print \"got \" & Fn()\r\n"
          + "End Sub\r\n"
          + "\r\n"
          + change,
            FormWithButtonAndTextBox);
        var printed = Printed(out var sink);
        VBDebugConsole.Output += sink;
        try
        {
            var window = Start(run, printed);

            Click(window);

            printed.Should().NotContain("got result", "the caller must stop with the event its callee raised");
            if (end)
                run.RunFailed.Should().BeEmpty("ending is not an error");
            else
                run.RunFailed.Should().ContainSingle().Which.Should().Contain("GoSub");
            AssertNothingIsRunning(run);
        }
        finally
        {
            VBDebugConsole.Output -= sink;
            run.Runner.EndProject();
        }
    }

    [AvaloniaFact]
    public void A_property_set_in_the_Immediate_window_while_paused_leaves_the_run_paused()
    {
        // Paused at Stop, the developer types `Text1.Text = "x"` into the Immediate window. That raises Text1_Change,
        // which the interpreter refuses to run in break mode. The refusal is the Immediate window's answer, not a
        // fault in the run, and must not tear down the run the developer is inspecting.
        var run = FormProject(
            "Private Sub Command1_Click()\r\n"
          + "    Stop\r\n"
          + "    Debug.Print \"the click finished\"\r\n"
          + "End Sub\r\n"
          + "\r\n"
          + "Private Sub Text1_Change()\r\n"
          + "    Debug.Print \"changed\"\r\n"
          + "End Sub\r\n",
            FormWithButtonAndTextBox);
        var printed = Printed(out var sink);
        VBDebugConsole.Output += sink;
        try
        {
            var window = Start(run, printed);
            var closed = false;
            window.Closed += (_, _) => closed = true;
            Click(window);
            run.Controller.State.Should().Be(DebugState.Paused, "the click stops at Stop");

            var evaluated = run.Controller.EvaluateAsync("Text1.Text = \"x\"");
            Pump();

            evaluated.IsCompleted.Should().BeTrue();
            run.RunFailed.Should().BeEmpty("refusing to run Text1_Change in break mode is not a fault");
            closed.Should().BeFalse();
            run.Controller.State.Should().Be(DebugState.Paused, "the run is still there to inspect");
            run.Runner.IsRunning.Should().BeTrue();

            run.Controller.Continue();
            Pump();

            printed.Should().Equal(["the click finished"], "the run carries on from where it was paused");
        }
        finally
        {
            VBDebugConsole.Output -= sink;
            run.Runner.EndProject();
        }
    }

    [AvaloniaFact]
    public void A_fault_in_Form_Load_ends_the_run_too()
    {
        // Form_Load is where a program's first statements run, so it is where most programs will meet a gap
        // first. The run must end, rather than leave an empty form standing. Load runs before the form is shown
        // (#822), so a fault there means the form is never shown at all.
        var run = FormProject(ReachesGoSub("Form_Load"));
        var printed = Printed(out var sink);
        VBDebugConsole.Output += sink;
        try
        {
            VBFormRuntime? window = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<VBFormRuntime>((w, _) =>
            {
                if (w.Title == Caption)
                    window = w;
            });
            run.Runner.RunProject(run.Project);
            Pump();

            run.RunFailed.Should().ContainSingle().Which.Should().Contain("GoSub");
            printed.Should().Equal(["before"]);
            window.Should().BeNull("a form whose Load faulted is never shown");
            AssertNothingIsRunning(run);
        }
        finally
        {
            VBDebugConsole.Output -= sink;
            run.Runner.EndProject();
        }
    }

    [AvaloniaFact]
    public void A_control_method_HexIDE_lacks_is_reported_as_not_implemented_rather_than_as_an_internal_error()
    {
        // VB6 controls have methods HexIDE does not, such as Refresh, ZOrder and ShowWhatsThis. One
        // reached through a control used to throw a plain Exception, which this change would otherwise have
        // reported as VB6's Internal error (51). ShowWhatsThis stands for the set, as the least likely to be
        // implemented soon.
        var run = FormProject("Private Sub Command1_Click()\r\n    Command1.ShowWhatsThis\r\nEnd Sub\r\n");
        try
        {
            var window = Start(run, []);

            Click(window);

            run.RunFailed.Should().ContainSingle()
                .Which.Should().StartWith("Not implemented yet: ").And.Contain("ShowWhatsThis")
                .And.NotContain("Internal error");
            AssertNothingIsRunning(run);
        }
        finally
        {
            run.Runner.EndProject();
        }
    }

    [AvaloniaFact]
    public void A_VB6_runtime_error_in_a_click_handler_still_leaves_the_run_going()
    {
        // The control, and #607's ground: a VB6 error is the runtime-error dialog's business, and the run
        // keeps going until End is pressed there. Only a fault ends a run on its own.
        var run = FormProject("Private Sub Command1_Click()\r\n    Error 5\r\nEnd Sub\r\n");
        var errors = new List<VBRunTimeException>();
        void OnRunTimeError(VBWindowContext _, VBRunTimeException e) => errors.Add(e);
        VBWindowContext.RunTimeError += OnRunTimeError;
        try
        {
            var window = Start(run, []);

            Click(window);

            errors.Should().ContainSingle().Which.Error.ErrNo.Should().Be(5);
            run.RunFailed.Should().BeEmpty();
            run.Runner.IsRunning.Should().BeTrue();
        }
        finally
        {
            VBWindowContext.RunTimeError -= OnRunTimeError;
            run.Runner.EndProject();
            Pump();
        }
    }

    [AvaloniaFact]
    public void A_fault_after_the_run_has_ended_is_only_logged()
    {
        var run = FormProject("Private Sub Command1_Click()\r\nEnd Sub\r\n");
        Start(run, []);
        run.Runner.EndProject();
        Pump();

        // A fault from a context outside any run, raised once nothing is running. What keeps it unreported is
        // that a run reports at most once, as it ends. The run also stops listening then, but that is housekeeping:
        // a leaked listener would stop an already stopped controller, and this test could not tell the difference.
        var stray = new VBWindowContext(Substitute.For<IBasicStandardLibrary>());
        stray.SetCode(ReachesGoSub("Command1_Click"), "Form1");
        var faults = new List<Exception>();
        void OnFaulted(VBWindowContext sender, Exception e)
        {
            if (ReferenceEquals(sender, stray))
                faults.Add(e);
        }
        VBWindowContext.Faulted += OnFaulted;
        try
        {
            stray.ExecuteSub("Command1_Click");
        }
        finally
        {
            VBWindowContext.Faulted -= OnFaulted;
        }

        faults.Should().ContainSingle("the fault itself was raised, so the silence below is not vacuous");
        run.RunFailed.Should().BeEmpty("a run that has ended no longer listens");
        AssertNothingIsRunning(run);
    }

    [AvaloniaTheory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void Another_runs_fault_or_End_cannot_end_the_current_run(bool subMain, bool end, bool stale)
    {
        var run = subMain
            ? SubMainProject("Sub Main()\r\nForm1.Show\r\nEnd Sub\r\n")
            : FormProject("Private Sub Form_Load()\r\nDebug.Print \"current\"\r\nEnd Sub\r\n");
        if (subMain)
        {
            var form = new FormDeserializer().Deserialize(run.Project, FormWithButton, NullSink.Instance)!;
            form.UpdateCode("Private Sub Form_Load()\r\nEnd Sub\r\n");
            run.Project.AddForm(form);
        }
        // A prior interpreter on the same controller, or an unrelated interpreter.
        var otherController = new DebugController();
        otherController.Reset();   // same generation as the new run, but a different controller
        var stray = new VBWindowContext(Substitute.For<IBasicStandardLibrary>());
        stray.SetCode(end ? "Sub Stray()\r\nEnd\r\nEnd Sub\r\n" : ReachesGoSub("Stray"),
            "OldForm", stale ? run.Controller : otherController);
        var raised = 0;
        void OnFaulted(VBWindowContext sender, Exception _) { if (ReferenceEquals(sender, stray)) raised++; }
        void OnEnd(BasicInterpreter sender) { if (ReferenceEquals(sender, stray.Interpreter)) raised++; }
        VBWindowContext.Faulted += OnFaulted;
        BasicInterpreter.EndReached += OnEnd;
        try
        {
            Start(run, []);
            run.Controller.State.Should().Be(DebugState.Running);
            // Run the leftover event past its debug gate to exercise listener ownership independently of the yield fix.
            stray.Interpreter!.DebugController = null;
            stray.ExecuteSub("Stray");
            Pump();
            raised.Should().Be(1, "the old event must actually reach the static listener");
            run.Runner.IsRunning.Should().BeTrue();
            run.Controller.State.Should().Be(DebugState.Running);
            run.RunFailed.Should().BeEmpty();
        }
        finally
        {
            VBWindowContext.Faulted -= OnFaulted;
            BasicInterpreter.EndReached -= OnEnd;
            run.Runner.EndProject();
            Pump();
        }
    }

    // ── Sub Main ─────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Sub_Main_that_reaches_something_unimplemented_ends_the_run_and_says_so()
    {
        var run = SubMainProject(ReachesGoSub("Main").Replace("Private Sub", "Sub"));
        var printed = Printed(out var sink);
        VBDebugConsole.Output += sink;
        try
        {
            run.Runner.RunProject(run.Project);
            Pump();
        }
        finally
        {
            VBDebugConsole.Output -= sink;
        }

        run.RunFailed.Should().ContainSingle()
            .Which.Should().StartWith("Not implemented yet: ").And.Contain("GoSub");
        run.StartFailed.Should().BeEmpty();
        printed.Should().Equal(["before"]);
        AssertNothingIsRunning(run);
    }

    [AvaloniaFact]
    public void Sub_Main_whose_code_cannot_be_loaded_is_a_start_that_never_became_a_run()
    {
        // The pre-pass runs while the interpreter is built. That used to happen outside the run's try, after
        // the debugger was reset to Running and the project claimed, so this left both that way (#590's shape).
        var run = SubMainProject("Dim s As String * 10\r\n\r\nSub Main()\r\n    Debug.Print \"never\"\r\nEnd Sub\r\n");

        run.Runner.RunProject(run.Project);

        // Before pumping: run_project reads StartFailed as raised before the start call returns, so a report that
        // arrived on a later dispatcher pass would leave it answering that the program ran.
        run.StartFailed.Should().ContainSingle("reported before the start call returns, as the form path does")
            .Which.Should().StartWith("Not implemented yet: ").And.Contain("fieldLength");
        AssertNothingIsRunning(run);

        Pump();
        run.StartFailed.Should().ContainSingle();
        run.RunFailed.Should().BeEmpty("nothing ran");
    }

    [AvaloniaTheory]
    [InlineData("    Error 5\r\n", "Run-time error '5':")]
    [InlineData("    NoSuchRoutine 1\r\n", "Compile error:")]
    public void An_error_Sub_Main_does_not_handle_is_reported_the_same_way(string body, string expected)
    {
        // These were plain message boxes, which no record kept, so get_last_runtime_error could not see them.
        var run = SubMainProject("Sub Main()\r\n" + body + "End Sub\r\n");

        run.Runner.RunProject(run.Project);
        Pump();

        run.RunFailed.Should().ContainSingle().Which.Should().StartWith(expected);
        // Line 3 of the file: a module's file begins with its Attribute VB_Name line, which the code window numbers too.
        run.RunFailedAt.Should().Equal([new HexIDE.Runtime.Interpreter.ErrorLocation("Module1", 3)], "where it was raised (#955)");
        run.Runner.FailureLocation.Should().BeNull("read only while the failure is reported");
        _ = run.WindowManager.DidNotReceiveWithAnyArgs().MessageBox(default!);
        AssertNothingIsRunning(run);
    }

    // ── End, which used to be one of these, is the program ending and nothing else (#775) ─────────

    [AvaloniaTheory]
    [InlineData("Command1_Click")]
    [InlineData("Form_Load")]
    public void End_in_an_event_procedure_ends_the_run_and_reports_nothing(string procedure)
    {
        var run = FormProject(
            $"Private Sub {procedure}()\r\n"
          + "    Debug.Print \"before\"\r\n"
          + "    End\r\n"
          + "    Debug.Print \"after\"\r\n"
          + "End Sub\r\n");
        var printed = Printed(out var sink);
        VBDebugConsole.Output += sink;
        try
        {
            VBFormRuntime? window = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<VBFormRuntime>((w, _) =>
            {
                if (w.Title == Caption)
                    window = w;
            });
            var closed = false;
            using var closing = Window.WindowClosedEvent.AddClassHandler<VBFormRuntime>((w, _) =>
            {
                if (w.Title == Caption)
                    closed = true;
            });

            run.Runner.RunProject(run.Project);
            Pump();
            if (procedure == "Command1_Click")
            {
                window.Should().NotBeNull();
                Click(window!);
            }

            printed.Should().Equal(["before"]);
            if (procedure == "Form_Load")
                // Load runs before the form is shown (#822), so a run that ends there never shows it.
                window.Should().BeNull("End in Form_Load ends the run before the form is shown");
            else
                closed.Should().BeTrue("End closes the program's form, as the End command does");
            run.RunFailed.Should().BeEmpty("ending is not an error");
            run.StartFailed.Should().BeEmpty();
            AssertNothingIsRunning(run);
        }
        finally
        {
            VBDebugConsole.Output -= sink;
            run.Runner.EndProject();
        }
    }

    [AvaloniaFact]
    public void End_in_Sub_Main_ends_the_run_and_reports_nothing()
    {
        // A Sub Main ending with End used to show a "does not implement End" dialog once #754 surfaced faults.
        var run = SubMainProject("Sub Main()\r\n    Debug.Print \"before\"\r\n    End\r\n    Debug.Print \"after\"\r\nEnd Sub\r\n");
        var printed = Printed(out var sink);
        VBDebugConsole.Output += sink;
        try
        {
            run.Runner.RunProject(run.Project);
            Pump();

            printed.Should().Equal(["before"]);
            run.RunFailed.Should().BeEmpty("ending is not an error");
            _ = run.WindowManager.DidNotReceiveWithAnyArgs().MessageBox(default!);
            AssertNothingIsRunning(run);
        }
        finally
        {
            VBDebugConsole.Output -= sink;
        }
    }
}
