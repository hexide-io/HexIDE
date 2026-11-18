using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Integration.Tests.Debugging;

// Measured in VB6's IDE: #4261's comments, oracle PR #4270 (also #4269).
// #4619: docs/vb6-fidelity-oracle/break-on-an-idle-program-and-a-step-off-an-event-handler.md.
public class IdleBreakTests
{
    private sealed class Sink : IDeserializeErrorSink { public void LogError(string message) => throw new Exception(message); }

    [AvaloniaFact]
    public async Task Step_over_a_statement_raising_an_event_keeps_the_outer_chains_depth()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project,
            "VERSION 5.00\r\nBegin VB.Form Form1\r\n   ClientHeight = 3000\r\n   ClientWidth = 4000\r\n"
            + "   Begin VB.TextBox Text1\r\n      Height = 300\r\n      Width = 1000\r\n   End\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n"
            + "Private Sub Form_Click()\r\n    Text1.Text = \"changed\"\r\n    Caption = \"after\"\r\nEnd Sub\r\n"
            + "Private Sub Text1_Change()\r\n    Caption = \"nested\"\r\nEnd Sub\r\n", new Sink())!;
        project.AddForm(form);
        var lines = FormCodeText.WholeFile(form).ReplaceLineEndings("\n").Split('\n');
        var first = Array.FindIndex(lines, l => l.Contains("Text1.Text =", StringComparison.Ordinal)) + 1;
        var debug = new DebugController { TimeSliceMs = null };
        debug.Reset();
        debug.SetBreakpoints("Form1", [first]);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = VBLoader.RunForm(form, cancellation.Token, out var window, debug);
        try
        {
            Dispatcher.UIThread.RunJobs();
            window.MouseDown(new Point(180, 150), MouseButton.Left);
            window.MouseUp(new Point(180, 150), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            debug.CurrentStop!.Value.Line.Should().Be(first);
            debug.StepOver();
            Dispatcher.UIThread.RunJobs();
            debug.CurrentStop!.Value.Line.Should().Be(first + 1, "the nested Change must not clear or steal Step Over (D4)");
            window.Title.Should().Be("nested");
            debug.GetCallStack().Select(f => f.ProcName).Should().Equal("Form_Click");
        }
        finally
        {
            debug.Stop();
            cancellation.Cancel();
            Dispatcher.UIThread.RunJobs();
            window.Close();
        }
        await run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [AvaloniaFact]
    public async Task Step_into_stops_on_a_Timer_handlers_end_then_the_next_ticks_declaration()
    {
        // #4619, the oracle page above: six F8s reach the next tick's declaration.
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, """
            VERSION 5.00
            Begin VB.Form Form1
               Begin VB.Timer Timer1
                  Interval = 500
               End
            End
            Attribute VB_Name = "Form1"
            Private n As Integer
            Private Sub Timer1_Timer()
                n = n + 1
                x = n * 2
                Caption = "Form1 " & n
            End Sub
            """.ReplaceLineEndings("\r\n"), new Sink())!;
        project.AddForm(form);
        var lines = FormCodeText.WholeFile(form).ReplaceLineEndings("\n").Split('\n');
        var declaration = Array.FindIndex(lines, l => l == "Private Sub Timer1_Timer()") + 1;
        var debug = new DebugController { TimeSliceMs = null };
        debug.Reset();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = VBLoader.RunForm(form, cancellation.Token, out var window, debug);
        async Task WaitForStop()
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (debug.State != DebugState.Paused && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20, TestContext.Current.CancellationToken);
                Dispatcher.UIThread.RunJobs();
            }
            debug.State.Should().Be(DebugState.Paused);
        }
        try
        {
            Dispatcher.UIThread.RunJobs();
            debug.Pause();
            debug.CurrentStop!.Value.Line.Should().Be(0);
            debug.StepInto();
            await WaitForStop();
            debug.CurrentStop.Should().Be(new StoppedInfo(StopReason.Step, "Form1", declaration));
            for (var offset = 1; offset <= 4; offset++)
            {
                debug.StepInto();
                await WaitForStop();
                debug.CurrentStop.Should().Be(new StoppedInfo(StopReason.Step, "Form1", declaration + offset));
            }
            window.Title.Should().Be("Form1 1");
            debug.StepInto();
            await WaitForStop();
            debug.CurrentStop.Should().Be(new StoppedInfo(StopReason.Step, "Form1", declaration));
            window.Title.Should().Be("Form1 1", "the next tick has not executed its first statement");
        }
        finally
        {
            debug.Stop();
            cancellation.Cancel();
            Dispatcher.UIThread.RunJobs();
            window.Close();
        }
        await run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [AvaloniaTheory]
    [InlineData("Continue")]
    [InlineData("Into")]
    [InlineData("Over")]
    [InlineData("Carry")]
    public async Task Idle_pause_and_resume_through_the_next_pointer_event(string resume)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project,
            "VERSION 5.00\r\nBegin VB.Form Form1\r\n   ClientHeight = 3000\r\n   ClientWidth = 4000\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n"
            + "Private Sub Form_Click()\r\n    Caption = \"clicked\"\r\n    Caption = \"done\"\r\nEnd Sub\r\n", new Sink())!;
        project.AddForm(form);
        var lines = FormCodeText.WholeFile(form).ReplaceLineEndings("\n").Split('\n');
        var declaration = Array.FindIndex(lines, l => l == "Private Sub Form_Click()") + 1;
        var debug = new DebugController { TimeSliceMs = null };
        debug.Reset();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = VBLoader.RunForm(form, cancellation.Token, out var window, debug);
        try
        {
            Dispatcher.UIThread.RunJobs();
            if (resume != "Carry")
            {
                debug.Pause();
                debug.State.Should().Be(DebugState.Paused);
                debug.CurrentStop!.Value.Reason.Should().Be(StopReason.Pause);
                debug.CurrentStop.Value.Line.Should().Be(0, "idle Break has no current statement");
                debug.GetCallStack().Should().BeEmpty();
                debug.GetLocals().Should().BeNull();
                if (resume == "Continue") debug.Continue();
                else if (resume == "Into") debug.StepInto();
                else debug.StepOver();
                debug.State.Should().Be(DebugState.Running);
                debug.CurrentStop.Should().BeNull();
            }
            else debug.SetBreakpoints("Form1", [declaration + 1]);
            window.MouseDown(new Point(180, 150), MouseButton.Left);
            window.MouseUp(new Point(180, 150), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            if (resume == "Continue")
            {
                debug.State.Should().Be(DebugState.Running);
                window.Title.Should().Be("done");
            }
            else
            {
                if (resume == "Carry")
                {
                    debug.CurrentStop!.Value.Line.Should().Be(declaration + 1);
                    debug.ClearBreakpoints();
                }
                else
                {
                    debug.CurrentStop.Should().Be(new StoppedInfo(StopReason.Step, "Form1", declaration));
                    debug.GetCallStack().Should().ContainSingle().Which.ProcName.Should().Be("Form_Click");
                    window.Title.Should().NotBe("clicked");
                    if (resume == "Into") debug.StepInto();
                    else debug.StepOver();
                    Dispatcher.UIThread.RunJobs();
                    debug.CurrentStop!.Value.Line.Should().Be(declaration + 1);
                }
                debug.StepOver();
                Dispatcher.UIThread.RunJobs();
                debug.CurrentStop!.Value.Line.Should().Be(declaration + 2);
                debug.StepOver();
                Dispatcher.UIThread.RunJobs();
                debug.CurrentStop.Should().Be(new StoppedInfo(StopReason.Step, "Form1", declaration + 3));
                debug.StepOver();
                Dispatcher.UIThread.RunJobs();
                debug.State.Should().Be(DebugState.Running);
                debug.CurrentStop.Should().BeNull();
                // The fourth Shift+F8 ran off the handler; its step survives until another click (#4269).
                window.MouseDown(new Point(210, 150), MouseButton.Left);
                window.MouseUp(new Point(210, 150), MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                debug.CurrentStop.Should().Be(new StoppedInfo(StopReason.Step, "Form1", declaration));
            }
        }
        finally
        {
            debug.Stop();
            cancellation.Cancel();
            Dispatcher.UIThread.RunJobs();
            window.Close();
        }
        await run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }
}
