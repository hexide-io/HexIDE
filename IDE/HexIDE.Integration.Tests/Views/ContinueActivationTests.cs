using Avalonia.Controls;
using Avalonia.Threading;
using HexIDE.IDE;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using Avalonia.Interactivity;
using HexIDE.Runtime.Running;

namespace HexIDE.Integration.Tests.Views;

public class ContinueActivationTests
{
    // SP6 measurements: docs/vb6-fidelity-oracle/break-on-an-idle-program-and-a-step-off-an-event-handler.md
    // Headless Activate is a no-op; observe the platform request, as IdeDialogOwnershipTests does.
    [AvaloniaTheory]
    [InlineData(false, "continue", true)]
    [InlineData(true, "continue", true)]
    [InlineData(false, "into", true)]
    [InlineData(false, "into", false)]
    [InlineData(false, "over", true)]
    [InlineData(false, "over", false)]
    public async Task Continue_restores_the_pause_window_even_after_steps(bool modal, string step, bool resume)
    {
        await using var owned = new RealComposition();
        var composition = owned.Composition;
        var root = composition.Root;
        var runner = (ProjectRunnerService)composition.ProjectRunnerService;
        var debug = (DebugController)owned.Engine.Debugger;
        var ideProbe = new IdeActivationProbe();
        var ide = ideProbe.Window;
        ide.Content = new MainView { DataContext = root };
        var formProbe = new IdeActivationProbe();
        var form = formProbe.Window;
        var owner = new Window();
        // Model the activation callbacks the headless platform omits, after the view's request.
        root.PauseFocusRequested += () =>
        {
            Deactivate(form);
            IdeDialogOwnershipTests.Activate(ide);
        };
        var previousProgram = RunningProgram.Current;
        var program = Substitute.For<IRunningProgram>();
        program.Form(form).Returns(new RunningForm(form, "Form2", true));
        RunningProgram.Current = program;
        Task? dialog = null;
        try
        {
            ide.Show();
            if (modal)
            {
                owner.Show();
                dialog = form.ShowDialog(owner);
            }
            else form.Show();
            runner.RunHandle = Substitute.For<IDisposable>();
            debug.Reset();
            debug.NotifyDispatchIdle();
            IdeDialogOwnershipTests.Activate(form);
            await Dispatcher.UIThread.InvokeAsync(runner.BreakCurrentProject);
            debug.CurrentStop!.Value.Reason.Should().Be(StopReason.Pause);
            ideProbe.Requests.Should().Be(1);
            var before = formProbe.Requests;
            if (step != "continue")
            {
                if (step == "into") runner.StepIntoProject();
                else runner.StepOverProject();
                formProbe.Requests.Should().Be(before, "a step leaves the IDE in front (already true on main)");
                var pending = debug.OnStatementAsync(0, "", null);
                debug.CurrentStop!.Value.Reason.Should().Be(StopReason.Step);
                if (resume) runner.ContinueProject();
                else debug.Continue();
                await pending;
            }
            else runner.ContinueProject();
            formProbe.Requests.Should().Be(before + (resume ? 1 : 0));
            if (!resume) return;
            runner.ContinueProject();
            formProbe.Requests.Should().Be(before + 1, "the remembered window is consumed once");
        }
        finally
        {
            runner.EndProject();
            form.Close();
            if (dialog is not null) await dialog;
            owner.Close();
            ide.Close();
            RunningProgram.Current = previousProgram;
        }
    }

    [AvaloniaTheory]
    [InlineData("hidden")]
    [InlineData("closed")]
    [InlineData("ended")]
    [InlineData("unrelated")]
    [InlineData("no-active")]
    [InlineData("breakpoint")]
    [InlineData("stop")]
    public async Task Continue_does_not_restore_an_ineligible_or_forgotten_window(string state)
    {
        await using var owned = new RealComposition();
        var runner = (ProjectRunnerService)owned.Composition.ProjectRunnerService;
        var debug = (DebugController)owned.Engine.Debugger;
        var probe = new IdeActivationProbe();
        var form = probe.Window;
        var previous = RunningProgram.Current;
        var program = Substitute.For<IRunningProgram>();
        if (state != "unrelated") program.Form(form).Returns(new RunningForm(form, "Form1", true));
        RunningProgram.Current = program;
        try
        {
            form.Show();
            if (state != "no-active") IdeDialogOwnershipTests.Activate(form);
            runner.RunHandle = Substitute.For<IDisposable>();
            debug.Reset();
            debug.NotifyDispatchIdle();
            await Dispatcher.UIThread.InvokeAsync(runner.BreakCurrentProject);
            var before = probe.Requests;
            if (state == "hidden") form.Hide();
            if (state == "closed") form.Close();
            if (state == "ended")
            {
                runner.EndProject();
                debug.Reset();
                debug.NotifyDispatchIdle();
                // A new run's Step stop must not inherit the old run's Pause window.
                debug.StepInto();
            }
            Task? pending = null;
            if (state == "ended") pending = debug.OnStatementAsync(0, "", null).AsTask();
            if (state is "breakpoint" or "stop")
            {
                debug.Continue();
                if (state == "breakpoint")
                {
                    debug.SetBreakpoints("M", [1]);
                    pending = debug.OnStatementAsync(1, "M", null).AsTask();
                }
                else pending = debug.EnterBreakFromStopStatementAsync(1, "M", null).AsTask();
            }
            debug.State.Should().Be(DebugState.Paused);
            runner.ContinueProject();
            if (pending is not null) await pending;
            probe.Requests.Should().Be(before);
            if (state == "hidden")
            {
                form.Show();
                before = probe.Requests;
                runner.ContinueProject();
                probe.Requests.Should().Be(before, "a hidden window was forgotten on Continue");
            }
        }
        finally
        {
            runner.EndProject();
            form.Close();
            RunningProgram.Current = previous;
        }
    }

    // #4667's corrected supervisor comment: row c raises Form3, Form1; row d raises Form1, Form3.
    [AvaloniaTheory]
    [InlineData("Form3", "Form1")]
    [InlineData("Form1", "Form3")]
    public async Task Continue_raises_other_forms_in_order_before_activating_the_paused_form(string first, string second)
    {
        await using var owned = new RealComposition();
        var runner = (ProjectRunnerService)owned.Composition.ProjectRunnerService;
        var debug = (DebugController)owned.Engine.Debugger;
        var probe = new IdeActivationProbe();
        var paused = probe.Window;
        var one = new Window { Title = first };
        var two = new Window { Title = second };
        var raised = new List<Window>();
        var before = 0;
        runner.WindowRaiser = new RecordingRaiser(window =>
        {
            probe.Requests.Should().Be(before, "restacking precedes activation");
            debug.State.Should().Be(DebugState.Paused, "restacking precedes release of the interpreter gate");
            raised.Add(window);
        });
        var previous = RunningProgram.Current;
        var program = Substitute.For<IRunningProgram>();
        program.Form(paused).Returns(new RunningForm(paused, "Form2", true));
        program.ShownWindows(paused).Returns(new object[] { one, two, paused });
        RunningProgram.Current = program;
        try
        {
            one.Show();
            two.Show();
            paused.Show();
            runner.RunHandle = Substitute.For<IDisposable>();
            debug.Reset();
            debug.NotifyDispatchIdle();
            IdeDialogOwnershipTests.Activate(paused);
            await Dispatcher.UIThread.InvokeAsync(runner.BreakCurrentProject);
            before = probe.Requests;
            await Dispatcher.UIThread.InvokeAsync(runner.ContinueProject);
            raised.Should().Equal(one, two);
            probe.Requests.Should().Be(before + 1);
            debug.State.Should().Be(DebugState.Running);
        }
        finally
        {
            runner.EndProject();
            paused.Close();
            two.Close();
            one.Close();
            RunningProgram.Current = previous;
        }
    }

    // Real interpreter activation history, including the corrected row c Load sequence (#4667, oracle PR #4665).
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Continue_uses_shown_activation_history_without_new_form_events(bool rowD, bool leaveOut)
    {
        await using var owned = new RealComposition();
        var runner = (ProjectRunnerService)owned.Composition.ProjectRunnerService;
        var debug = (DebugController)owned.Engine.Debugger;
        var previous = RunningProgram.Current;
        var observer = new InterpreterRunningProgram();
        RunningProgram.Current = observer;
        var opened = new Dictionary<string, VBFormRuntime>();
        using var watch = Window.WindowOpenedEvent.AddClassHandler<VBFormRuntime>((w, _) => opened[w.Title!] = w);
        var printed = new List<string>();
        VBDebugConsole.Output += printed.Add;
        using var cancel = new CancellationTokenSource();
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        static string Events(string name) => $$"""
            Private Sub Form_Activate()
                Debug.Print "{{name}} activate"
            End Sub
            Private Sub Form_Deactivate()
                Debug.Print "{{name}} deactivate"
            End Sub

            """;
        void Add(string name, string code)
        {
            var source = $"VERSION 5.00\nBegin VB.Form {name}\n Caption = \"{name}\"\n ClientHeight = 3000\n ClientWidth = 4000\nEnd\nAttribute VB_Name = \"{name}\"\n"
                + Events(name) + code;
            project.AddForm(new FormDeserializer().Deserialize(project, source.ReplaceLineEndings("\r\n"),
                Substitute.For<IDeserializeErrorSink>())!);
        }
        Add("Form1", "Private Sub Form_Load()\nForm3.Show\nEnd Sub\n"
            + "Private Sub OpenModal()\nForm2.Show vbModal, " + (rowD ? "Form1" : "Form3") + "\nEnd Sub\n"
            + "Private Sub Others()\nForm4.Show\nForm5.Show\nEnd Sub\n"
            + "Private Sub HideOthers()\nForm4.Hide\nForm5.WindowState = 1\nEnd Sub\n");
        Add("Form2", "");
        Add("Form3", "");
        Add("Form4", "");
        Add("Form5", "");
        try
        {
            _ = VBLoader.RunForm(project.Forms.First(), cancel.Token, out var form1);
            Pump();
            if (leaveOut)
            {
                form1.ExecuteSub("Others");
                Pump();
                observer.ShownWindows(form1).Should().Contain(opened["Form4"]).And.Contain(opened["Form5"]);
                form1.ExecuteSub("HideOthers");
                Pump();
                Deactivate(opened["Form3"]);
                IdeDialogOwnershipTests.Activate(opened["Form3"]);
                Deactivate(form1);
                IdeDialogOwnershipTests.Activate(form1);
            }
            if (rowD)
            {
                Deactivate(opened["Form3"]);
                opened["Form3"].Activate();
                IdeDialogOwnershipTests.Activate(opened["Form3"]);
            }
            form1.ExecuteSub("OpenModal");
            Pump();
            var modal = opened["Form2"];
            modal.Owner.Should().BeSameAs(opened[rowD ? "Form1" : "Form3"]);
            var expected = new object[] { opened[rowD ? "Form1" : "Form3"], opened[rowD ? "Form3" : "Form1"], modal };
            observer.ShownWindows(modal).Cast<Window>().Select(w => w.Title).Should().Equal(expected.Cast<Window>().Select(w => w.Title));
            var log = printed.ToArray();
            log.Should().Contain("Form2 activate");
            var raised = new List<Window>();
            runner.WindowRaiser = new RecordingRaiser(raised.Add);
            runner.RunHandle = Substitute.For<IDisposable>();
            // Compare the original resume sequence (empty restack list) with the new one on the same modal form.
            foreach (var restack in new[] { false, true })
            {
                var baseline = Substitute.For<IRunningProgram>();
                baseline.Form(modal).Returns(observer.Form(modal));
                RunningProgram.Current = restack ? observer : baseline;
                debug.Reset();
                debug.NotifyDispatchIdle();
                foreach (var form in opened.Values) Deactivate(form);
                IdeDialogOwnershipTests.Activate(modal);
                await Dispatcher.UIThread.InvokeAsync(runner.BreakCurrentProject);
                Deactivate(modal);
                await Dispatcher.UIThread.InvokeAsync(runner.ContinueProject);
                IdeDialogOwnershipTests.Activate(modal);
                Pump();
                printed.Should().Equal(log, "neither resume sequence adds Activate or Deactivate events");
            }
            raised.Cast<object>().Should().Equal(expected.Take(2));
        }
        finally
        {
            runner.EndProject();
            cancel.Cancel();
            Pump();
            VBDebugConsole.Output -= printed.Add;
            RunningProgram.Current = previous;
        }
    }

    private static void Pump()
    {
        var context = System.Threading.ExecutionContext.Capture();
        try
        {
            for (var i = 0; i < 20; i++) Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            if (context is not null) System.Threading.ExecutionContext.Restore(context);
        }
    }

    private static void Deactivate(Window window) =>
        typeof(WindowBase).GetMethod("HandleDeactivated", System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, null);

    // A hand-written fake: IWindowRaiser is internal, and mocking it would need the production assembly's internals
    // opened to DynamicProxyGenAssembly2 (#4667 review).
    private sealed class RecordingRaiser(Action<Window> raise) : IWindowRaiser
    {
        public void Raise(Window window) => raise(window);
    }
}
