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
    }

    private static Run Build(ProjectDefinition project)
    {
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.ProjectRunner.StartupFormFailedToLoad").Returns("Form '{0}' failed: {1}");
        localization.GetString("Str.ProjectRunner.NotImplementedYet").Returns("Not implemented yet: {0}");

        var windowManager = Substitute.For<IWindowManager>();
        var controller = new DebugController();
        var runner = new ProjectRunnerService(
            Substitute.For<IEventBus>(), windowManager, Substitute.For<IProjectManager>(),
            localization, controller, Substitute.For<IBreakpointService>(), new WatchService(), new RunScope());
        var run = new Run { Runner = runner, Controller = controller, WindowManager = windowManager, Project = project };
        runner.StartFailed += run.StartFailed.Add;
        runner.RunFailed += run.RunFailed.Add;
        return run;
    }

    private static Run FormProject(string code)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, FormWithButton, NullSink.Instance)!;
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

    [AvaloniaFact]
    public void A_fault_in_Form_Load_ends_the_run_too()
    {
        // Form_Load runs while the form is still being shown, which is the case the run-task's late
        // cancellation exists for. The demo that found this failed in exactly this place.
        var run = FormProject(ReachesGoSub("Form_Load"));
        var printed = Printed(out var sink);
        VBDebugConsole.Output += sink;
        try
        {
            var window = Start(run, printed);

            run.RunFailed.Should().ContainSingle().Which.Should().Contain("GoSub");
            printed.Should().Equal(["before"]);
            window.IsVisible.Should().BeFalse("the form is closed with the run");
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
        // VB6 controls have methods HexIDE does not, such as Move, Refresh, SetFocus and ShowWhatsThis. One
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

        // A fault from a context outside any run, raised once nothing is running.
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
        Pump();

        run.StartFailed.Should().ContainSingle("reported before the start call returns, as the form path does")
            .Which.Should().StartWith("Not implemented yet: ").And.Contain("fieldLength");
        run.RunFailed.Should().BeEmpty("nothing ran");
        AssertNothingIsRunning(run);
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
        _ = run.WindowManager.DidNotReceiveWithAnyArgs().MessageBox(default!);
        AssertNothingIsRunning(run);
    }
}
