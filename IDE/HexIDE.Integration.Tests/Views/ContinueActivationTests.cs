using Avalonia.Controls;
using Avalonia.Threading;
using HexIDE.IDE;
using HexIDE.Runtime.Debugging;

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
        var debug = (DebugController)composition.DebugController;
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
        var debug = (DebugController)owned.Composition.DebugController;
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

    private static void Deactivate(Window window) =>
        typeof(WindowBase).GetMethod("HandleDeactivated", System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, null);
}
