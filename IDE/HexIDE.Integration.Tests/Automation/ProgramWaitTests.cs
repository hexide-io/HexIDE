using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
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
/// An action on a running form replies once the program has finished what it set off (#1232). A handler gives up the
/// UI thread at the debugger's time slice (#766), so the reply used to go out part way through it: after Minesweeper's
/// Game > Intermediate, a snapshot showed the new field with the old mine count.
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
      + "   Begin VB.TextBox Text1 \r\n"
      + "      Height          =   375\r\n"
      + "      Left            =   120\r\n"
      + "      Top             =   840\r\n"
      + "      Width           =   1215\r\n"
      + "   End\r\n"
      + "End\r\n"
      + "Attribute VB_Name = \"Form1\"\r\n";

    private sealed class Run : IDisposable
    {
        public required ProjectRunnerService Runner { get; init; }
        public required VBFormRuntime Window { get; init; }

        public Control Named(string name) =>
            Window.GetLogicalDescendants().OfType<Control>().Single(c => VBProps.GetName(c) == name);

        public void Dispose()
        {
            Runner.EndProject();
            Pump();
        }
    }

    private static Run Start(string code)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, Form, new NullSink())!;
        form.UpdateCode(code.ReplaceLineEndings("\r\n"));
        project.AddForm(form);

        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>() + ": {0}");
        var runner = new ProjectRunnerService(
            Substitute.For<IEventBus>(), Substitute.For<IWindowManager>(), Substitute.For<IProjectManager>(),
            localization, new DebugController { TimeSliceMs = -1 }, Substitute.For<IBreakpointService>(),
            new WatchService(), new RunScope());

        VBFormRuntime? window = null;
        using var opened = Avalonia.Controls.Window.WindowOpenedEvent.AddClassHandler<VBFormRuntime>((w, _) =>
        {
            if (w.Title == Caption)
                window = w;
        });
        runner.RunProject(project);
        Pump();
        window.Should().NotBeNull("the startup form opens before anything is clicked");
        return new Run { Runner = runner, Window = window! };
    }

    private static void Pump()
    {
        for (var i = 0; i < 20; i++)
            Dispatcher.UIThread.RunJobs();
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

    [AvaloniaFact]
    public async Task An_action_replies_once_the_handler_it_set_off_has_finished()
    {
        using var run = Start(SlowClick);
        var outcome = UiAutomationDriver.Interact(run.Named("Command1"), "invoke", null);
        ((TextBox)run.Named("Text1")).Text.Should().NotBe("done", "the handler yields at every statement, so it is part way through");

        outcome = await UiAutomationDriver.WaitForProgramAsync(run.Window, outcome);

        outcome.Success.Should().BeTrue(outcome.Error);
        ((TextBox)run.Named("Text1")).Text.Should().Be("done");
        outcome.Detail.Should().NotContain("still running");
    }

    [AvaloniaFact]
    public async Task A_handler_still_running_at_the_limit_is_named_in_the_reply()
    {
        // Runs on well past the limit, as a handler held by a MsgBox or a breakpoint does, then ends of itself.
        using var run = Start("""
            Private Sub Command1_Click()
                Dim t As Single, i As Long
                t = Timer
                Do While Timer < t + 2
                    i = i + 1
                Loop
            End Sub
            """);
        var outcome = UiAutomationDriver.Interact(run.Named("Command1"), "invoke", null);

        outcome = await UiAutomationDriver.WaitForProgramAsync(run.Window, outcome, TimeSpan.FromMilliseconds(200));

        outcome.Success.Should().BeTrue(outcome.Error);
        outcome.Detail.Should().Contain("the program was still running Command1_Click 0.2s later");
    }

    // A MsgBox holds its handler until it is answered, so the reply says so at once rather than waiting out the limit.
    [AvaloniaFact]
    public async Task A_handler_with_a_window_open_over_the_form_is_reported_without_waiting_out_the_limit()
    {
        using var run = Start("""
            Private Sub Command1_Click()
                Dim t As Single, i As Long
                t = Timer
                Do While Timer < t + 2
                    i = i + 1
                Loop
            End Sub
            """);
        var box = new Window { Title = "Project1" };
        var outcome = UiAutomationDriver.Interact(run.Named("Command1"), "invoke", null);
        box.Show(run.Window);
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            outcome = await UiAutomationDriver.WaitForProgramAsync(run.Window, outcome, TimeSpan.FromSeconds(30));

            watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
            outcome.Detail.Should().Contain("the program is still running Command1_Click, and has a window open over the "
                                            + "form, 'Project1'");
        }
        finally { box.Close(); }
    }

    [AvaloniaFact]
    public async Task A_refused_action_does_not_wait()
    {
        using var run = Start(SlowClick);
        var refused = new InteractOutcome(false, "peer", null, "refused");

        var outcome = await UiAutomationDriver.WaitForProgramAsync(run.Window, refused, TimeSpan.FromSeconds(30));

        outcome.Should().Be(refused);
    }

    [AvaloniaFact]
    public async Task A_window_that_is_not_a_running_form_does_not_wait()
    {
        var window = new Window();
        window.Show();
        try
        {
            var done = new InteractOutcome(true, "peer", "invoked", null);

            var outcome = await UiAutomationDriver.WaitForProgramAsync(window, done, TimeSpan.FromSeconds(30));

            outcome.Should().Be(done);
        }
        finally { window.Close(); }
    }
}
