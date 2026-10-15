using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A run-time error nothing handles halts the program at the statement that raised it, as VB6's IDE does (#1011). Each
/// case runs through the IDE's own F5 path, with a stand-in for the IDE's dialog that holds the halt until the test
/// answers it, as a person would.
/// </summary>
/// <remarks>
/// Measured (vb6-fidelity-oracle.md, <em>An unhandled run-time error</em>): the dialog offers End and Debug; nothing runs
/// behind it; Debug breaks at the faulting statement with its procedure live; Continue runs that statement again, all of
/// it; End raises no Unload or Terminate. A handler in the code that raised an event never sees the event's error.
/// </remarks>
public class HaltOnErrorTests
{
    private const string Caption = "HaltOnErrorTests";

    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private static readonly string Header =
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
      + "   Begin VB.TextBox Text1 \r\n"
      + "      Height          =   375\r\n"
      + "      Left            =   120\r\n"
      + "      Top             =   840\r\n"
      + "      Width           =   1215\r\n"
      + "   End\r\n"
      + "   Begin VB.Timer Timer1 \r\n"
      + "      Enabled         =   0   'False\r\n"
      + "      Interval        =   1000\r\n"
      + "      Left            =   2400\r\n"
      + "      Top             =   840\r\n"
      + "   End\r\n"
      + "End\r\n"
      + "Attribute VB_Name = \"Form1\"\r\n";

    // A ticking Timer, for every case that asks whether anything runs behind a wait.
    private const string Ticker = """
        Private ticks As Integer
        Private Sub Timer1_Timer()
            ticks = ticks + 1
            Debug.Print "tick " & ticks
        End Sub
        """;

    private sealed class Run : IDisposable
    {
        public required ProjectRunnerService Runner { get; init; }
        public required DebugController Controller { get; init; }
        public VBFormRuntime? Window { get; init; }
        public required List<string> Printed { get; init; }
        public required List<string> Failed { get; init; }
        public required List<string> Halts { get; init; }
        public required Queue<TaskCompletionSource<bool>> Pending { get; init; }
        public required Action Detach { get; init; }

        public void Click(string name) => Window!.GetLogicalDescendants().OfType<VBCommandButton>()
            .Single(b => VBProps.GetName(b) == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        public void Tick() => Window!.ExecuteTimer("Timer1_Timer");

        /// <summary>Answer the oldest halt's dialog: Debug, or End.</summary>
        public void Answer(bool debug)
        {
            Pending.Should().NotBeEmpty("a halt is waiting on its dialog");
            Pending.Dequeue().SetResult(debug);
            Pump();
        }

        public void Dispose()
        {
            Detach();
            while (Pending.Count > 0)
                Pending.Dequeue().TrySetResult(false);
            Runner.EndProject();
            Pump();
        }
    }

    private static Run Start(string formCode, params (string Name, ModuleKind Kind, string Code)[] modules) =>
        Start(formCode, subMain: false, modules);

    private static Run Start(string formCode, bool subMain, params (string Name, ModuleKind Kind, string Code)[] modules)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, Header + formCode.ReplaceLineEndings("\r\n"), new NullSink())!;
        project.AddForm(form);
        foreach (var (name, kind, code) in modules)
        {
            var module = new ModuleDefinition(project, name, kind);
            module.UpdateCode(code.ReplaceLineEndings("\r\n"));
            project.AddModule(module);
        }
        project.StartsAtSubMain = subMain;

        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>() switch
        {
            "Str.ProjectRunner.StartupFormFailedToLoad" => "Form '{0}' failed: {1}",
            var key => key + ": {0}",
        });
        var controller = new DebugController { TimeSliceMs = -1 };
        var runner = new ProjectRunnerService(
            Substitute.For<IEventBus>(), Substitute.For<IWindowManager>(), Substitute.For<IProjectManager>(),
            localization, controller, Substitute.For<IBreakpointService>(), new WatchService(), new RunScope());

        // The IDE's dialog, held until the test answers it. End ends the run, as the IDE's does.
        var halts = new List<string>();
        var pending = new Queue<TaskCompletionSource<bool>>();
        controller.HaltHost = async error =>
        {
            halts.Add(error.Message);
            var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending.Enqueue(answer);
            var debug = await answer.Task;
            if (!debug && runner.CanEndProject)
                runner.EndProject();
            return debug;
        };

        VBFormRuntime? window = null;
        using var opened = Avalonia.Controls.Window.WindowOpenedEvent.AddClassHandler<VBFormRuntime>((w, _) =>
        {
            if (w.Title == Caption)
                window = w;
        });
        var printed = new List<string>();
        var failed = new List<string>();
        Action<string> print = printed.Add;
        VBDebugConsole.Output += print;
        runner.StartFailed += failed.Add;
        runner.RunFailed += failed.Add;
        runner.RunProject(project);
        Pump();
        return new Run
        {
            Runner = runner,
            Controller = controller,
            Window = window,
            Printed = printed,
            Failed = failed,
            Halts = halts,
            Pending = pending,
            Detach = () => VBDebugConsole.Output -= print,
        };
    }

    // The file line of the code line holding marker: the form's header comes first, as the code window counts.
    private static int LineOf(string formCode, string marker)
    {
        var headerLines = Header.Split("\r\n").Length - 1;
        var index = formCode.ReplaceLineEndings("\n").Split('\n').ToList().FindIndex(l => l.Contains(marker));
        index.Should().BeGreaterThanOrEqualTo(0, $"the code holds '{marker}'");
        return headerLines + index + 1;
    }

    private static void Pump()
    {
        for (var i = 0; i < 20; i++)
            Dispatcher.UIThread.RunJobs();
    }

    // ---- Nothing runs behind it: the review's three cases, each a wait inside a nested handler ---------------------

    [AvaloniaFact]
    public void A_tick_waits_behind_a_MsgBox_in_a_nested_handler()
    {
        // Measured in the IDE: no tick comes while a MsgBox is up.
        var boxes = new List<Window>();
        using var opened = Avalonia.Controls.Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) =>
        {
            if (w.Content is HexIDE.Runtime.Dialogs.MessageBox)
                boxes.Add(w);
        });
        using var run = Start(Ticker + """

            Private Sub Command1_Click()
                Debug.Print "click begins"
                Text1.Text = "x"
                Debug.Print "click ends"
            End Sub
            Private Sub Text1_Change()
                MsgBox "wait"
                Debug.Print "change ends"
            End Sub
            """);

        run.Click("Command1");
        Pump();
        boxes.Should().ContainSingle("Text1_Change is waiting on its box");
        run.Tick();
        Pump();
        run.Tick();
        Pump();
        run.Printed.Should().Equal("click begins");

        boxes[0].GetLogicalDescendants().OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal("click begins", "change ends", "click ends", "tick 1");
    }

    [AvaloniaFact]
    public void A_tick_waits_behind_a_breakpoint_in_a_nested_handler()
    {
        // Measured in the IDE: no tick runs in break mode.
        const string code = Ticker + """

            Private Sub Command1_Click()
                Debug.Print "click begins"
                Text1.Text = "x"
                Debug.Print "click ends"
            End Sub
            Private Sub Text1_Change()
                Debug.Print "change" ' break here
            End Sub
            """;
        using var run = Start(code);
        run.Controller.SetBreakpoints("Form1", [LineOf(code, "' break here")]);

        run.Click("Command1");
        Pump();
        run.Controller.State.Should().Be(DebugState.Paused);
        run.Tick();
        Pump();
        run.Tick();
        Pump();
        run.Printed.Should().Equal("click begins");

        run.Controller.Continue();
        Pump();

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal("click begins", "change", "click ends", "tick 1");
    }

    [AvaloniaFact]
    public void A_tick_waits_behind_a_halt_in_a_nested_handler_and_End_runs_nothing_more()
    {
        using var run = Start(Ticker + """

            Private Sub Command1_Click()
                Debug.Print "click begins"
                Text1.Text = "x"
                Debug.Print "click ends"
            End Sub
            Private Sub Text1_Change()
                Dim d As Integer
                Debug.Print 1 \ d
            End Sub
            Private Sub Form_Unload(Cancel As Integer)
                Debug.Print "unload"
            End Sub
            """);

        run.Click("Command1");
        Pump();
        run.Halts.Should().ContainSingle().Which.Should().Contain("Division by zero");
        run.Tick();
        Pump();
        run.Tick();
        Pump();
        run.Printed.Should().Equal("click begins");

        run.Answer(debug: false);

        // End raises no Unload, as VB6's End does (measured), and nothing waiting runs.
        run.Runner.IsRunning.Should().BeFalse();
        run.Printed.Should().Equal("click begins");
        run.Failed.Should().BeEmpty();
    }

    // ---- Debug, and the statement again ------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Debug_breaks_at_the_statement_and_Continue_runs_it_again()
    {
        const string code = """
            Private d As Integer
            Private Sub Command1_Click()
                Debug.Print "before"
                Debug.Print 10 \ d ' faults
                Debug.Print "after"
            End Sub
            """;
        using var run = Start(code);

        run.Click("Command1");
        Pump();
        run.Answer(debug: true);

        run.Controller.State.Should().Be(DebugState.Paused);
        run.Controller.CurrentStop.Should().Be(new StoppedInfo(StopReason.RuntimeError, "Form1", LineOf(code, "' faults")));
        // A statement typed in the Immediate window that raises answers with its error, and halts nothing.
        run.Controller.EvaluateAsync("d = 1 \\ 0").GetAwaiter().GetResult().Should().Contain("Division by zero");
        run.Halts.Should().ContainSingle();
        // The procedure is live: the Immediate window reaches its frame, and a statement typed there runs in it.
        run.Controller.EvaluateAsync("d = 2").GetAwaiter().GetResult();
        Pump();

        run.Controller.Continue();
        Pump();

        run.Halts.Should().ContainSingle();
        run.Printed.Should().Equal("before", "5", "after");
        run.Failed.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void The_statement_run_again_runs_all_of_it_and_halts_again()
    {
        // Measured: for x = F() + 1 \ 0, F is called again at the F5, and the same dialog comes straight back.
        using var run = Start("""
            Private Function F() As Integer
                Debug.Print "F"
                F = 1
            End Function
            Private Sub Command1_Click()
                Dim x As Integer
                x = F() + 1 \ 0
            End Sub
            """);

        run.Click("Command1");
        Pump();
        run.Answer(debug: true);
        run.Controller.Continue();
        Pump();

        run.Halts.Should().HaveCount(2);
        run.Printed.Should().Equal("F", "F");
        run.Answer(debug: false);
        run.Runner.IsRunning.Should().BeFalse();
    }

    [AvaloniaTheory]
    [InlineData("If True Then\n        n = n + 1\n        Debug.Print 1 \\ d\n    End If")]
    [InlineData("If True Then n = n + 1: Debug.Print 1 \\ d")]
    [InlineData("Do\n        n = n + 1\n    Loop Until 1 \\ d > 0")]
    [InlineData("If Counted() Then\n    ElseIf 1 \\ d > 0 Then\n        Debug.Print 1 \\ d\n    End If")]
    public void Only_the_statement_that_raised_runs_again_not_what_came_before_it(string statement)
    {
        // A statement in a block, one in a single-line If's tail, and a condition that is asked again on its own: a Loop
        // Until's without another pass through the body, an ElseIf's without the conditions before it.
        using var run = Start($$"""
            Private d As Integer
            Private n As Integer
            Private Function Counted() As Boolean
                n = n + 1
            End Function
            Private Sub Command1_Click()
                {{statement.Replace("\n", "\r\n")}}
                Debug.Print "n " & n
            End Sub
            """);

        run.Click("Command1");
        Pump();
        run.Answer(debug: true);
        run.Controller.EvaluateAsync("d = 1").GetAwaiter().GetResult();
        run.Controller.Continue();
        Pump();

        run.Halts.Should().ContainSingle();
        run.Printed.Last().Should().Be("n 1");
        run.Failed.Should().BeEmpty();
    }

    // ---- What handles it, and what doesn't -------------------------------------------------------------------------

    [AvaloniaFact]
    public void A_handler_in_the_code_that_raised_an_event_never_sees_its_error()
    {
        // b1, measured: On Error GoTo around Text1.Text = "x", and Text1_Change divides by zero. Not caught.
        using var run = Start("""
            Private Sub Command1_Click()
                On Error GoTo handled
                Text1.Text = "x"
                Exit Sub
            handled:
                Debug.Print "handled"
            End Sub
            Private Sub Text1_Change()
                Dim d As Integer
                Debug.Print 1 \ d
            End Sub
            """);

        run.Click("Command1");
        Pump();

        run.Halts.Should().ContainSingle();
        run.Answer(debug: false);
        run.Printed.Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData("form")]
    [InlineData("class")]
    public void A_handler_around_RaiseEvent_never_sees_the_sinks_error(string where)
    {
        // b2 and b3, measured: On Error GoTo around c.Fire in the form, or around RaiseEvent inside Fire. Not caught.
        var fire = where == "class"
            ? "Public Sub Fire()\n    On Error GoTo handled\n    RaiseEvent Boom\n    Exit Sub\nhandled:\n    Debug.Print \"handled in Fire\"\nEnd Sub"
            : "Public Sub Fire()\n    RaiseEvent Boom\nEnd Sub";
        using var run = Start("""
            Private WithEvents c As Source
            Private Sub Command1_Click()
                On Error GoTo handled
                Set c = New Source
                c.Fire
                Exit Sub
            handled:
                Debug.Print "handled in the form"
            End Sub
            Private Sub c_Boom()
                Dim d As Integer
                Debug.Print 1 \ d
            End Sub
            """, ("Source", ModuleKind.ClassModule, "Public Event Boom()\n" + fire));

        run.Click("Command1");
        Pump();

        run.Halts.Should().ContainSingle();
        run.Answer(debug: true);
        run.Controller.CurrentStop!.Value.Module.Should().Be("Form1", "the break is in the sink");
        run.Controller.Stop();
        Pump();
        run.Printed.Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData(ModuleKind.ClassModule)]
    [InlineData(ModuleKind.StandardModule)]
    public void A_handler_in_the_caller_catches_an_error_its_callee_does_not_handle(ModuleKind kind)
    {
        // c1, decided on #1011 as compiled programs do it, and c2, measured the same everywhere: the form's handler runs.
        var call = kind == ModuleKind.ClassModule ? "Dim o As Helper\n    Set o = New Helper\n    o.Divide" : "Divide";
        using var run = Start($$"""
            Private Sub Command1_Click()
                On Error GoTo handled
                {{call.Replace("\n", "\r\n")}}
                Exit Sub
            handled:
                Debug.Print "handled " & Err.Number
            End Sub
            """, ("Helper", kind, "Public Sub Divide()\n    Dim d As Integer\n    Debug.Print 1 \\ d\nEnd Sub"));

        run.Click("Command1");
        Pump();

        run.Failed.Should().BeEmpty();
        run.Halts.Should().BeEmpty();
        run.Printed.Should().Equal("handled 11");
    }

    [AvaloniaFact]
    public void An_error_in_a_called_procedure_halts_inside_it()
    {
        // Measured: the break is on the faulting line of the Sub the handler called, not on the call.
        using var run = Start("""
            Private Sub Command1_Click()
                Divide
                Debug.Print "after"
            End Sub
            """, ("Helper", ModuleKind.StandardModule, "Public Sub Divide()\n    Dim d As Integer\n    Debug.Print 1 \\ d\nEnd Sub"));

        run.Click("Command1");
        Pump();
        run.Answer(debug: true);

        run.Controller.CurrentStop!.Value.Module.Should().Be("Helper");
        run.Controller.CurrentStop!.Value.Reason.Should().Be(StopReason.RuntimeError);
    }

    [AvaloniaFact]
    public void An_error_in_Form_Load_halts_the_start()
    {
        using var run = Start("""
            Private Sub Form_Load()
                Dim d As Integer
                Debug.Print 1 \ d
                Debug.Print "loaded"
            End Sub
            """);

        run.Halts.Should().ContainSingle();
        run.Answer(debug: false);
        run.Runner.IsRunning.Should().BeFalse();
        run.Printed.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void An_error_in_Sub_Main_halts_it()
    {
        using var run = Start("", subMain: true, ("Module1", ModuleKind.StandardModule,
            "Sub Main()\n    Dim d As Integer\n    Debug.Print 1 \\ d\n    Debug.Print \"after\"\nEnd Sub"));

        run.Halts.Should().ContainSingle();
        run.Answer(debug: true);
        run.Controller.CurrentStop!.Value.Module.Should().Be("Module1");
        run.Controller.EvaluateAsync("d = 1").GetAwaiter().GetResult();
        run.Controller.Continue();
        Pump();

        run.Printed.Should().Equal("1", "after");
        run.Failed.Should().BeEmpty();
    }
}
