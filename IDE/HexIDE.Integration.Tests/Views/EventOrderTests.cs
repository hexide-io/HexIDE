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
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A program's handlers run one at a time, as VB6's single thread runs them (#766). Each case runs through the IDE's own
/// F5 path, under a debugger whose time slice is either off or <c>-1</c>, which yields at every statement: the yield that
/// used to let another event start inside a handler, deterministically rather than on a loaded machine.
/// </summary>
/// <remarks>
/// What VB6 does is measured (vb6-fidelity-oracle/one-handler-at-a-time.md, <em>One handler at a time</em>): a handler a statement raises runs to
/// its end inside that statement; nothing else runs inside a handler unless it calls <c>DoEvents</c>; and a Timer is never
/// delivered inside its own handler, and delivers one tick, not several, once the handler keeping it waiting ends.
/// </remarks>
public class EventOrderTests
{
    private const string Caption = "EventOrderTests";

    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private static readonly string Form =
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
      + "   Begin VB.CommandButton Command2 \r\n"
      + "      Caption         =   \"Command2\"\r\n"
      + "      Height          =   495\r\n"
      + "      Left            =   1440\r\n"
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

    private sealed class Run : IDisposable
    {
        public required ProjectRunnerService Runner { get; init; }
        public required VBFormRuntime Window { get; init; }
        public required Action Detach { get; init; }
        public required List<string> Printed { get; init; }
        public required List<string> Failed { get; init; }

        public void Click(string name) => Window.GetLogicalDescendants().OfType<VBCommandButton>()
            .Single(b => VBProps.GetName(b) == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        public void Tick() => Window.ExecuteTimer("Timer1_Timer");

        public void Dispose()
        {
            Detach();
            Runner.EndProject();
            Pump();
        }
    }

    /// <param name="timeSliceMs">The debugger's time slice: null for none, -1 to yield at every statement.</param>
    private static Run Start(string code, long? timeSliceMs)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, Form, new NullSink())!;
        form.UpdateCode(code.ReplaceLineEndings("\r\n"));
        project.AddForm(form);

        var localization = Substitute.For<ILocalizationService>();
        // Each message's own placeholders, as the packs have them. A template with one too many made the fault report
        // throw before it was raised, so a fault in one of these runs went unreported and `Failed` read empty.
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>() switch
        {
            "Str.ProjectRunner.StartupFormFailedToLoad" => "Form '{0}' failed: {1}",
            var key => key + ": {0}",
        });
        var controller = new DebugController { TimeSliceMs = timeSliceMs };
        var runner = new ProjectRunnerService(
            Substitute.For<IEventBus>(), Substitute.For<IWindowManager>(), Substitute.For<IProjectManager>(),
            localization, controller, Substitute.For<IBreakpointService>(), new WatchService(), new RunScope());

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
        window.Should().NotBeNull("the startup form opens before anything is clicked");
        return new Run
        {
            Runner = runner,
            Window = window!,
            Printed = printed,
            Failed = failed,
            Detach = () => VBDebugConsole.Output -= print,
        };
    }

    private static void Pump()
    {
        for (var i = 0; i < 20; i++)
            Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData(-1L)]
    public void A_handler_a_statement_raised_runs_to_its_end_before_the_next_statement(long? timeSliceMs)
    {
        // Measured: Change begins and ends inside the setter, before the statement after it.
        using var run = Start("""
            Private Sub Command1_Click()
                Debug.Print "click begins"
                Text1.Text = "x"
                Debug.Print "click ends"
            End Sub
            Private Sub Text1_Change()
                Debug.Print "change begins"
                Debug.Print "change ends"
            End Sub
            """, timeSliceMs);

        run.Click("Command1");
        Pump();

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal("click begins", "change begins", "change ends", "click ends");
    }

    [AvaloniaFact]
    public void A_loop_condition_sees_what_a_handler_its_last_statement_raised_has_set()
    {
        // The body's last statement raises Change, which sets done; the condition is read after it, as in VB6, so the
        // loop runs once. Read before Change had finished, it ran twice.
        using var run = Start("""
            Private done As Boolean
            Private Sub Command1_Click()
                Dim n As Integer
                Do
                    n = n + 1
                    Text1.Text = Text1.Text & "x"
                Loop Until done Or n > 5
                Debug.Print "loops " & n
            End Sub
            Private Sub Text1_Change()
                Debug.Print "change"
                done = True
            End Sub
            """, -1);

        run.Click("Command1");
        Pump();

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal("change", "loops 1");
    }

    [AvaloniaFact]
    public void An_event_that_comes_while_a_handler_runs_waits_until_it_has_finished()
    {
        // Command1's handler yields at its first statement; Command2 is clicked while it is suspended there.
        using var run = Start("""
            Private Sub Command1_Click()
                Debug.Print "one begins"
                Debug.Print "one goes on"
                Debug.Print "one ends"
            End Sub
            Private Sub Command2_Click()
                Debug.Print "two"
            End Sub
            """, -1);

        run.Click("Command1");
        run.Click("Command2");
        Pump();

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal("one begins", "one goes on", "one ends", "two");
    }

    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData(-1L)]
    public void DoEvents_runs_an_event_that_waits_inside_the_caller(long? timeSliceMs)
    {
        // The loop that must not hang: before DoEvents ran what waited, only the debugger's time slice let the click in.
        using var run = Start("""
            Private clicked As Boolean
            Private Sub Command1_Click()
                Dim n As Long
                Debug.Print "loop begins"
                Do While Not clicked
                    DoEvents
                    n = n + 1
                    If n > 100000 Then Exit Do
                Loop
                Debug.Print "loop ends " & clicked
            End Sub
            Private Sub Command2_Click()
                clicked = True
                Debug.Print "two"
            End Sub
            """, timeSliceMs);

        run.Click("Command1");
        run.Click("Command2");
        Pump();

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal("loop begins", "two", "loop ends True");
    }

    [AvaloniaFact]
    public void Without_DoEvents_a_loop_never_sees_an_event_that_came_while_it_ran()
    {
        // VB6 runs nothing inside a handler that does not call DoEvents, so the click is delivered once the loop is done.
        using var run = Start("""
            Private clicked As Boolean
            Private Sub Command1_Click()
                Dim i As Integer
                For i = 1 To 20
                    If clicked Then Exit For
                Next
                Debug.Print "saw it " & clicked
            End Sub
            Private Sub Command2_Click()
                clicked = True
                Debug.Print "two"
            End Sub
            """, -1);

        run.Click("Command1");
        run.Click("Command2");
        Pump();

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal("saw it False", "two");
    }

    [AvaloniaFact]
    public void Ticks_that_come_due_behind_another_handler_are_delivered_as_one()
    {
        // Measured: a 50 ms Timer kept waiting 400 ms delivers a single tick once the handler in its way ends.
        using var run = Start("""
            Private n As Integer
            Private Sub Command1_Click()
                Debug.Print "click begins"
                Debug.Print "click ends"
            End Sub
            Private Sub Timer1_Timer()
                n = n + 1
                Debug.Print "tick " & n
            End Sub
            """, -1);

        run.Click("Command1");
        run.Tick();
        run.Tick();
        run.Tick();
        Pump();

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal("click begins", "click ends", "tick 1");
    }

    [AvaloniaFact]
    public void A_tick_that_comes_while_its_own_handler_runs_is_dropped()
    {
        // Measured: a Timer never runs inside its own handler, even under DoEvents; its next tick comes an interval later.
        using var run = Start("""
            Private n As Integer
            Private Sub Timer1_Timer()
                n = n + 1
                Debug.Print "tick " & n & " begins"
                DoEvents
                Debug.Print "tick " & n & " ends"
            End Sub
            """, -1);

        run.Tick();
        run.Tick();
        Pump();
        run.Tick();
        Pump();

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal("tick 1 begins", "tick 1 ends", "tick 2 begins", "tick 2 ends");
    }
}
