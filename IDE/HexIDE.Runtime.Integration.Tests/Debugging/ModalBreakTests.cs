using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Integration.Tests.Debugging;

public class ModalBreakTests
{
    private sealed class Sink : IDeserializeErrorSink { public void LogError(string message) => throw new Exception(message); }

    // VB6 SP6: #4708's measured rows c/d and F5, measuring PR #4724. Nested waits are the
    // issue's chosen behavior, not a VB6 measurement; see docs/debugger-vb6-divergences.md.
    [AvaloniaTheory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task Pause_at_a_modal_wait_keeps_the_Show_frame_and_Continue_keeps_waiting(bool timer, bool nested)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        FormDefinition Form(string name, string code, bool withTimer = false)
        {
            var form = new FormDeserializer().Deserialize(project, ($"""
                VERSION 5.00
                Begin VB.Form {name}
                   Caption = "{name}"
                   ClientHeight = 3000
                   ClientWidth = 4000
                   Begin VB.CommandButton Command1
                      Height = 400
                      Width = 1200
                      Left = 120
                      Top = 120
                   End
                """ + (withTimer ? "\n   Begin VB.Timer Timer1\n      Interval = 100\n   End" : "")
                + $"\nEnd\nAttribute VB_Name = \"{name}\"\n" + code).ReplaceLineEndings("\r\n"), new Sink())!;
            project.AddForm(form);
            return form;
        }
        var procedure = timer ? "Timer1_Timer" : "Command1_Click";
        var form1 = Form("Form1", $"""
            Private Sub {procedure}()
                Dim marker As Integer
                marker = 42
                {(timer ? "Timer1.Enabled = False" : "Load Form3")}
                Form2.Show vbModal{(timer ? "" : ", Form3")}
                Caption = "after Show"
            End Sub
            """, timer);
        var form2 = Form("Form2", nested ? """
            Private Sub Command1_Click()
                Dim marker As Integer
                marker = 42
                Form3.Show vbModal
                Caption = "after Show"
            End Sub
            """ : "");
        Form("Form3", "");
        var waitingForm = nested ? form2 : form1;
        var lines = FormCodeText.WholeFile(waitingForm).ReplaceLineEndings("\n").Split('\n');
        var showLine = Array.FindIndex(lines, l => l.Contains(".Show vbModal", StringComparison.Ordinal)) + 1;
        var opened = new Dictionary<string, VBFormRuntime>();
        using var watch = Window.WindowOpenedEvent.AddClassHandler<VBFormRuntime>((w, _) => opened[w.Title!] = w);
        var debug = new DebugController { TimeSliceMs = null };
        debug.Reset();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = VBLoader.RunForm(form1, cancellation.Token, out var window, debug);
        void Click(VBFormRuntime target)
        {
            var button = target.GetLogicalDescendants().OfType<VBCommandButton>().Single();
            var centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), target)!.Value;
            target.MouseDown(centre, MouseButton.Left);
            target.MouseUp(centre, MouseButton.Left);
        }
        try
        {
            Dispatcher.UIThread.RunJobs();
            if (!timer) Click(window);
            await WaitUntil(() => opened.ContainsKey("Form2"));
            if (nested)
            {
                Click(opened["Form2"]);
                await WaitUntil(() => opened.ContainsKey("Form3"));
            }
            // Let posted activation events drain before requesting a pause: no next statement may rescue it.
            await Task.Delay(150, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            var modal = opened[nested ? "Form3" : "Form2"];
            for (var attempt = 0; attempt < 2; attempt++)
            {
                // The statement is already executing: a newly placed breakpoint must not steal Pause's reason.
                if (attempt == 1) debug.SetBreakpoints(nested ? "Form2" : "Form1", [showLine]);
                debug.Pause();
                await WaitUntil(() => debug.State == DebugState.Paused);
                debug.CurrentStop.Should().Be(new StoppedInfo(StopReason.Pause, nested ? "Form2" : "Form1", showLine));
                debug.GetCallStack()[0].ProcName.Should().Be(nested ? "Command1_Click" : procedure);
                debug.GetCallStack()[0].Line.Should().Be(showLine);
                debug.GetLocals().Should().NotBeNull();
                (await debug.EvaluateAsync("marker")).Should().Be("42");
                debug.SetNextStatement(nested ? "Form2" : "Form1", showLine).Should().BeFalse();
                debug.Continue();
                if (attempt == 0)
                {
                    // Pause again before the resumed modal loop has had a dispatcher turn to subscribe.
                    debug.Pause();
                    await WaitUntil(() => debug.State == DebugState.Paused);
                    debug.CurrentStop.Should().Be(new StoppedInfo(StopReason.Pause, nested ? "Form2" : "Form1", showLine));
                    debug.Continue();
                }
                await Task.Delay(100, TestContext.Current.CancellationToken);
                Dispatcher.UIThread.RunJobs();
                debug.State.Should().Be(DebugState.Running);
                modal.IsVisible.Should().BeTrue();
                window.HeldBy.Should().BeSameAs(modal);
                opened[nested ? "Form2" : "Form1"].Title.Should().NotBe("after Show");
            }
        }
        finally
        {
            debug.Stop();
            cancellation.Cancel();
            Dispatcher.UIThread.RunJobs();
            foreach (var shown in opened.Values) shown.Close();
            window.Close();
        }
        await run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private static async Task WaitUntil(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!ready() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
        }
        ready().Should().BeTrue("the dispatcher should reach the requested state within five seconds");
    }
}
