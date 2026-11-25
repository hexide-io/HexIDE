using Avalonia.Threading;
using HexIDE.Addins;
using HexIDE.Debugging;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Running;

namespace HexIDE.Integration.Tests.Addins;

/// <summary>
/// The provider's interpreter registered through the host as the IDE registers it (#5616): what the IDE's debugger answers
/// before it registers, what reaches it once it has, and its withdrawal while a program is paused at a breakpoint, with the
/// interpreter waiting in the debugger's gate.
/// </summary>
public sealed class RunEngineWithdrawalTests : IDisposable
{
    private static readonly AddinOrigin Provider = new(RegistrationGrants.BasicProvider, "HexIDE.Basic") { IsComposed = true };

    private readonly IRunningProgram programBefore = RunningProgram.Current;
    private readonly IBreakpointService breakpoints = Substitute.For<IBreakpointService>();
    private readonly AddinRunEngineService engines;
    private readonly DebuggerForwarder debugger;
    private readonly RunEngineForwarder forwarder;
    private readonly ProjectRunnerService runner;

    public RunEngineWithdrawalTests()
    {
        var localization = Substitute.For<ILocalizationService>();
        var windows = new WindowManager(localization);
        engines = new AddinRunEngineService(() => TestRunners.Services(new ProgramHost(windows, localization), windows: windows));
        debugger = new DebuggerForwarder(engines);
        breakpoints.GetBreakpoints(default!).ReturnsForAnyArgs(Array.Empty<int>());
        forwarder = new RunEngineForwarder(engines, debugger);
        runner = new ProjectRunnerService(Substitute.For<IEventBus>(), Substitute.For<IProjectManager>(), localization, debugger,
            breakpoints, new WatchService(), new RunScope(), forwarder);
    }

    public void Dispose() => RunningProgram.Current = programBefore;

    private static ProjectDefinition Project()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P") { StartsAtSubMain = true };
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode("Sub Main()\nDim i As Integer\ni = 1\ni = 2\nEnd Sub\n");
        project.AddModule(module);
        return project;
    }

    private IDisposable Register() => engines.Register(services => new InterpreterRunEngine(services), Provider);

    private static async Task Until(Func<bool> condition)
    {
        for (var turns = 0; !condition(); turns++)
        {
            turns.Should().BeLessThan(500, "the condition should hold within five seconds");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    [AvaloniaFact]
    public async Task With_no_engine_the_IDEs_debugger_answers_as_one_that_has_never_run()
    {
        var idle = new DebugController();

        debugger.State.Should().Be(idle.State);
        debugger.IsSessionActive.Should().Be(idle.IsSessionActive);
        debugger.CurrentStop.Should().Be(idle.CurrentStop);
        debugger.GetLocals().Should().Be(idle.GetLocals());
        debugger.GetCallStack().Should().BeEquivalentTo(idle.GetCallStack());
        debugger.SetNextStatement("Module1", 3).Should().Be(idle.SetNextStatement("Module1", 3));
        (await debugger.EvaluateAsync("1 + 1")).Should().Be(await idle.EvaluateAsync("1 + 1"));
        (await debugger.EvaluateWatchAsync("i")).Should().Be(await idle.EvaluateWatchAsync("i"));
        (await debugger.EvaluateScopedWatchAsync("i", "Module1.Main")).Should()
            .Be(await ((IDebugController)idle).EvaluateScopedWatchAsync("i", "Module1.Main"));
        FluentActions.Invoking(() =>
        {
            debugger.Reset();
            debugger.Continue();
            debugger.StepInto();
            debugger.Stop();
        }).Should().NotThrow();
    }

    [AvaloniaFact]
    public async Task A_breakpoint_removed_while_no_engine_was_registered_never_breaks_the_run()
    {
        debugger.SetBreakpoints("Module1", [3]);
        debugger.SetBreakpoints("Module1", []);
        var stops = 0;
        debugger.Stopped += _ => stops++;
        using var registration = Register();

        runner.RunProject(Project());
        await Until(() => !runner.IsRunning);

        stops.Should().Be(0, "the editor holds no breakpoint, so the engine was given none");
    }

    [AvaloniaFact]
    public async Task Withdrawn_while_paused_at_a_breakpoint_the_program_ends_and_the_IDE_leaves_Run_mode()
    {
        var project = Project();
        breakpoints.GetBreakpoints(DocumentIdentity.For(project.Modules.Single())).Returns(new[] { 3 });
        var registration = Register();
        RunningProgram.Current.Should().BeOfType<InterpreterRunningProgram>();
        runner.RunProject(project);
        await Until(() => debugger.State == DebugState.Paused);
        debugger.CurrentStop!.Value.Line.Should().Be(3);

        // From another thread, with the UI thread held: the interpreter is waiting in the debugger's gate.
        Task.Run(registration.Dispose).Wait(TimeSpan.FromSeconds(10)).Should().BeTrue("a withdrawal only posts");
        runner.IsRunning.Should().BeTrue("the program ends on the UI thread, once the post runs");
        HostDialogs.BoxWindows.Should().NotBeNull("the engine is disposed in the posted withdrawal, not under the caller's lock");
        forwarder.Runs(project).Should().BeFalse("nothing starts a run on a withdrawn engine before the post runs");
        await Until(() => !runner.IsRunning);
        await Until(() => engines.Current is null);

        runner.RunningProject.Should().BeNull();
        engines.Current.Should().BeNull();
        RunningProgram.Current.Should().BeOfType<EmptyRunningProgram>();
        debugger.State.Should().Be(DebugState.Running, "with no engine, what a debugger that has never run answers");
        HostDialogs.BoxWindows.Should().BeNull();
        HostDialogs.File.Should().BeNull();
        HostDialogs.Print.Should().BeNull();
        HostDialogs.Color.Should().BeNull();
        HostDialogs.Font.Should().BeNull();
    }

    [AvaloniaFact]
    public async Task Withdrawn_the_engine_hands_back_the_runtimes_own_dialogs_and_box_windows()
    {
        var registration = Register();
        HostDialogs.BoxWindows.Should().NotBeNull("the engine shows a form's boxes in windows the IDE makes");
        HostDialogs.File.Should().NotBeNull();
        FormMsgBoxIsTheProgramsByTheIDEsRecord().Should().BeTrue();

        registration.Dispose();
        await Until(() => engines.Current is null);

        HostDialogs.BoxWindows.Should().BeNull("nothing a withdrawn engine installed outlives it");
        HostDialogs.File.Should().BeNull();
        HostDialogs.Print.Should().BeNull();
        HostDialogs.Color.Should().BeNull();
        HostDialogs.Font.Should().BeNull();
        FormMsgBoxIsTheProgramsByTheIDEsRecord().Should().BeFalse("with no engine, the box is in a window of the runtime's own");
    }

    // A form's MsgBox, as the StandaloneStandardLib every running form has shows one, and whether the IDE made its window.
    private static bool FormMsgBoxIsTheProgramsByTheIDEsRecord()
    {
        var owner = new Avalonia.Controls.Window { Title = "Form1" };
        owner.Show();
        try
        {
            _ = new StandaloneStandardLib(owner, "Probe").MsgBox("Hello", "Probe", MessageBoxButtons.Ok, MessageBoxIcon.None);
            Dispatcher.UIThread.RunJobs();
            var box = owner.OwnedWindows.Should().ContainSingle().Subject;
            return ProgramBoxes.IsProgramBox(box.Content);
        }
        finally
        {
            // Closed, unanswered, with its owner.
            owner.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
