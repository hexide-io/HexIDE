using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using HexIDE.Addins;
using HexIDE.Debugging;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.Addins;

/// <summary>
/// Who may register the engine that runs projects, what the IDE holds while none is registered, and how a registration is
/// withdrawn (#5616). The engine runs the developer's code in the IDE's process, so this is deny by default, and a
/// withdrawal must never wait on the UI thread: the registry disposes it under its lock. Each test reads what the IDE then
/// holds, not only what the call returned.
/// </summary>
public sealed class RunEngineRegistrationTests : IDisposable
{
    private readonly IRunningProgram programBefore = RunningProgram.Current;
    private readonly string root = Path.Combine(Path.GetTempPath(), "hexide-run-engine-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        RunningProgram.Current = programBefore;
        try { Directory.Delete(root, true); } catch { /* best effort: Windows may still hold the loaded probe */ }
    }

    private static AddinOrigin Owner(string id) => new(id, "Owner " + id) { IsComposed = true };

    private static RegistrationGrant Grant(string id, bool engine = true) => new(id, Composed: true) { RunEngine = engine };

    private static AddinRunEngineService Service(params RegistrationGrant[] grants) =>
        new(() => new RunEngineServices(Substitute.For<IProgramHost>(), Substitute.For<IProgramDialogWindows>(), () => false, () => false))
        {
            Grants = grants,
        };

    private static HexIdeHost Ide(AddinRunEngineService? engines, IAddinRegistry? addins = null) =>
        new(null!, null!, null!, addins!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            new AddinResourceService(), new AddinLanguagePackService(), new AddinLanguageEditingService(), engines);

    internal static IRunEngine Engine()
    {
        var engine = Substitute.For<IRunEngine>();
        engine.Debugger.Returns(Substitute.For<IDebugController>());
        engine.Program.Returns(Substitute.For<IRunningProgram>());
        engine.Runs(default!).ReturnsForAnyArgs(true);
        engine.RefusesToStart(default!).ReturnsForAnyArgs((string?)null);
        return engine;
    }

    private static ProjectDefinition Project()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P") { StartsAtSubMain = true };
        project.AddModule(new ModuleDefinition(project, "Module1", ModuleKind.StandardModule));
        return project;
    }

    private static ProjectRunnerService Runner(IRunEngine engine, IDebugController debugger) =>
        TestRunners.WithEngine(engine, Substitute.For<IEventBus>(), Substitute.For<IProjectManager>(),
            Substitute.For<ILocalizationService>(), debugger, Substitute.For<IBreakpointService>(), new WatchService(), new RunScope());

    // A run that goes on until it's ended.
    private sealed class HeldRun(List<string> calls) : IEngineRun
    {
        private readonly TaskCompletionSource<RunFailure?> ended = new();

        public Task<RunFailure?> RunToEndAsync() => ended.Task;

        public void End()
        {
            calls.Add("End");
            ended.TrySetResult(null);
        }
    }

    [AvaloniaFact]
    public void An_owner_with_no_grant_is_refused_even_through_the_internal_member()
    {
        var engines = Service(Grant("granted"), Grant("themes-only", engine: false));
        var made = 0;
        Func<RunEngineServices, IRunEngine> factory = _ => { made++; return Engine(); };

        // The member is internal, which reflection reaches, so the grant is what refuses it.
        var stranger = (IRunEngineRegistration)new AddinOwnedHost(Ide(engines), null!, Owner("stranger"));
        FluentActions.Invoking(() => stranger.RegisterRunEngine(factory))
            .Should().Throw<InvalidOperationException>().WithMessage("Owner stranger isn't one HexIDE lets run projects.");
        FluentActions.Invoking(() => engines.Register(factory, Owner("themes-only")))
            .Should().Throw<InvalidOperationException>().WithMessage("Owner themes-only isn't one HexIDE lets run projects.");
        // A package naming itself as the composed provider holds no composed grant.
        FluentActions.Invoking(() => engines.Register(factory, new AddinOrigin("granted", "Impostor")))
            .Should().Throw<InvalidOperationException>().WithMessage("Impostor isn't one HexIDE lets run projects.");

        made.Should().Be(0, "a refused owner's code never runs");
        engines.Current.Should().BeNull();
    }

    [AvaloniaFact]
    public void The_IDEs_own_host_refuses_it_with_no_owner_to_check()
    {
        var engines = Service(Grant("granted"));

        FluentActions.Invoking(() => ((IRunEngineRegistration)Ide(engines)).RegisterRunEngine(_ => Engine()))
            .Should().Throw<InvalidOperationException>().WithMessage("*only through the host an add-in is given.");
        engines.Current.Should().BeNull();
    }

    [AvaloniaFact]
    public void A_granted_owner_runs_projects_and_its_program_is_the_IDEs()
    {
        var engines = Service(Grant("granted"));
        var engine = Engine();
        var forwarder = new RunEngineForwarder(engines, new DebuggerForwarder(engines));
        forwarder.Runs(Project()).Should().BeFalse("with no engine, nothing runs");

        using var registration = ((IRunEngineRegistration)new AddinOwnedHost(Ide(engines), null!, Owner("granted")))
            .RegisterRunEngine(_ => engine);

        engines.Current!.Engine.Should().BeSameAs(engine);
        forwarder.Runs(Project()).Should().BeTrue();
        RunningProgram.Current.Should().BeSameAs(engine.Program);
    }

    [AvaloniaFact]
    public void A_second_engine_is_refused_naming_both_owners_and_the_first_stands()
    {
        var engines = Service(Grant("a"), Grant("b"));
        var first = Engine();
        var made = 0;
        using var held = engines.Register(_ => first, Owner("a"));

        FluentActions.Invoking(() => engines.Register(_ => { made++; return Engine(); }, Owner("b")))
            .Should().Throw<InvalidOperationException>().WithMessage("Owner b can't run projects: Owner a (a) runs them already.");
        made.Should().Be(0);
        engines.Current!.Engine.Should().BeSameAs(first);
    }

    [AvaloniaFact]
    public void A_factory_that_throws_or_makes_nothing_registers_nothing()
    {
        var engines = Service(Grant("a"));

        FluentActions.Invoking(() => engines.Register(_ => throw new FormatException("the provider's own fault"), Owner("a")))
            .Should().Throw<InvalidOperationException>().WithMessage("Owner a couldn't make its run engine.")
            .WithInnerException<FormatException>();
        FluentActions.Invoking(() => engines.Register(_ => null!, Owner("a")))
            .Should().Throw<InvalidOperationException>().WithMessage("Owner a couldn't make its run engine.");
        engines.Current.Should().BeNull();
    }

    [AvaloniaFact]
    public void A_throwing_engine_answers_that_call_as_no_engine_and_stays_registered()
    {
        var engines = Service(Grant("a"));
        var engine = Engine();
        engine.Runs(default!).ReturnsForAnyArgs(_ => throw new FormatException("Runs"));
        engine.Debugger.State.Returns(_ => throw new FormatException("State"));
        engine.Debugger.EvaluateAsync(default!).ReturnsForAnyArgs(Task.FromException<string?>(new FormatException("Evaluate")));
        var debugger = new DebuggerForwarder(engines);
        var forwarder = new RunEngineForwarder(engines, debugger);
        using var registration = engines.Register(_ => engine, Owner("a"));

        forwarder.Runs(Project()).Should().BeFalse();
        debugger.State.Should().Be(DebugState.Running, "what a debugger that has never run answers");
        FluentActions.Awaiting(() => debugger.EvaluateAsync("1")).Should().NotThrowAsync();
        engines.Current.Should().BeSameAs(registration);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_start_the_engine_throws_on_leaves_the_IDE_out_of_Run_mode(bool atPrepare)
    {
        var engines = Service(Grant("a"));
        var engine = Engine();
        if (atPrepare)
            engine.Prepare(default!).ReturnsForAnyArgs(_ => throw new FormatException("Prepare"));
        else
        {
            var run = Substitute.For<IEngineRun>();
            run.RunToEndAsync().Returns(Task.FromException<RunFailure?>(new FormatException("RunToEndAsync")));
            engine.Prepare(default!).ReturnsForAnyArgs(RunStart.Started(run));
        }
        var debugger = new DebuggerForwarder(engines);
        var runner = Runner(new RunEngineForwarder(engines, debugger), debugger);
        var failures = new List<string>();
        runner.StartFailed += failures.Add;
        using var registration = engines.Register(_ => engine, Owner("a"));

        runner.RunProject(Project());
        await Task.Yield();

        runner.IsRunning.Should().BeFalse();
        runner.RunningProject.Should().BeNull();
        failures.Should().BeEmpty("a throw is logged, as before engines registered, not shown");
        engines.Current.Should().BeSameAs(registration);
    }

    [AvaloniaFact]
    public void How_the_IDE_is_wired_reaches_an_engine_that_registers_later()
    {
        var engines = Service(Grant("a"));
        var forwarder = new RunEngineForwarder(engines, new DebuggerForwarder(engines));
        Func<RunFailure, Task<bool>> halt = _ => Task.FromResult(true);
        var met = new List<RunFailure>();
        forwarder.HaltHost = halt;
        forwarder.ErrorTrapping = ErrorTrapping.BreakOnAllErrors;
        forwarder.ErrorMet += met.Add;
        var engine = Engine();

        using var registration = engines.Register(_ => engine, Owner("a"));
        var failure = new RunFailure("Run-time error '5'", null);
        engine.ErrorMet += Raise.Event<Action<RunFailure>>(failure);

        engine.HaltHost.Should().BeSameAs(halt);
        engine.ErrorTrapping.Should().Be(ErrorTrapping.BreakOnAllErrors);
        met.Should().Equal(failure);
    }

    [AvaloniaFact]
    public void Breakpoints_set_and_removed_with_no_engine_never_reach_one()
    {
        var engines = Service(Grant("a"));
        var debugger = new DebuggerForwarder(engines);
        debugger.SetBreakpoints("Module1", [3]);
        debugger.SetBreakpointConditions("Module1", new Dictionary<int, string> { [3] = "i > 1" });
        debugger.SetBreakpoints("Module1", []);
        var engine = Engine();

        using var registration = engines.Register(_ => engine, Owner("a"));

        engine.Debugger.DidNotReceiveWithAnyArgs().SetBreakpoints(default!, default!);
        engine.Debugger.DidNotReceiveWithAnyArgs().SetBreakpointConditions(default!, default!);
        debugger.SetBreakpoints("Module1", [4]);
        engine.Debugger.Received(1).SetBreakpoints("Module1", Arg.Is<IReadOnlyCollection<int>>(lines => lines.SequenceEqual(new[] { 4 })));
    }

    [AvaloniaFact]
    public async Task Disposing_from_another_thread_never_waits_and_ends_the_running_program_first()
    {
        var engines = Service(Grant("a"));
        var engine = Engine();
        var calls = new List<string>();
        engine.Debugger.When(d => d.Stop()).Do(_ => calls.Add("Stop"));
        var run = new HeldRun(calls);
        engine.Prepare(default!).ReturnsForAnyArgs(RunStart.Started(run));
        var debugger = new DebuggerForwarder(engines);
        var forwarder = new RunEngineForwarder(engines, debugger);
        var runner = Runner(forwarder, debugger);
        var registration = engines.Register(_ => engine, Owner("a"));
        runner.RunProject(Project());
        runner.IsRunning.Should().BeTrue();
        calls.Clear();

        // The UI thread is held in Wait here, so a dispose that waited on it would never return.
        Task.Run(registration.Dispose).Wait(TimeSpan.FromSeconds(10)).Should().BeTrue("a dispose that waited on the UI thread would hang");

        forwarder.Runs(Project()).Should().BeFalse("a withdrawn engine starts nothing more");
        runner.IsRunning.Should().BeTrue("the end is posted, not run under the caller's lock");
        Dispatcher.UIThread.RunJobs();
        await Task.Yield();

        calls.Take(2).Should().Equal("Stop", "End");
        runner.IsRunning.Should().BeFalse();
        engines.Current.Should().BeNull();
        RunningProgram.Current.Should().BeOfType<EmptyRunningProgram>();
        debugger.State.Should().Be(DebugState.Running, "with no engine, what a debugger that has never run answers");
    }

    [AvaloniaFact]
    public async Task Disabling_its_package_withdraws_it_through_the_registrys_lock_from_another_thread()
    {
        var (addins, package) = await LoadedProbe();
        var owner = new AddinOrigin(ProbeId, "Probe own", IsFirstParty: true);
        var engines = Service(new RegistrationGrant(ProbeId, Composed: false) { RunEngine = true });
        var registration = Ide(engines, addins).RegisterRunEngine(_ => Engine(), owner);
        engines.Current.Should().BeSameAs(registration);

        Task.Run(() => addins.SetEnabled(package, false)).Wait(TimeSpan.FromSeconds(10))
            .Should().BeTrue("the registry disposes under its lock, and the withdrawal only posts");
        engines.Current!.Withdrawing.Should().BeTrue();
        Dispatcher.UIThread.RunJobs();

        engines.Current.Should().BeNull();
    }

    [AvaloniaFact]
    public void A_window_is_the_programs_only_when_the_IDE_made_it_for_the_engine()
    {
        var localization = Substitute.For<ILocalizationService>();
        localization.FlowDirection.Returns(FlowDirection.RightToLeft);
        IProgramDialogWindows windows = new WindowManager(localization);
        var message = new TextBlock();
        var input = new TextBlock();

        ProgramBoxes.IsProgramBox(message).Should().BeFalse("nothing made a window for it");
        var window = (Window)windows.MakeWindow("Program", message, ProgramBox.Message);
        windows.MakeWindow("Program", input, ProgramBox.Input);

        ProgramBoxes.IsProgramBox(message).Should().BeTrue();
        ProgramBoxes.IsMessageBox(message).Should().BeTrue();
        ProgramBoxes.IsProgramBox(input).Should().BeTrue();
        ProgramBoxes.IsMessageBox(input).Should().BeFalse();
        window.FlowDirection.Should().Be(FlowDirection.LeftToRight, "a program's box is laid out as VB6 lays it out");
        window.Title.Should().Be("Program");
    }

    private const string ProbeId = "com.example.probe";

    // A loaded third-party package, consented to, as ComponentWithdrawalTests loads one.
    private async Task<(AddinRegistry Addins, string Package)> LoadedProbe()
    {
        var packages = Path.Combine(root, "addins");
        var package = Path.Combine(packages, "own");
        Directory.CreateDirectory(package);
        DuplicateAddinTests.WriteProbe(Path.Combine(package, "DuplicateProbe.dll"), "own");
        var verifier = Substitute.For<IPackageVerifier>();
        verifier.Verify(package).Returns(new PackageVerificationResult(PackageVerdict.Verified,
            new AddinManifest { Id = ProbeId, Title = "Probe own", Entry = "DuplicateProbe.dll" }, null, null, null)
            { ManifestHash = "hash-own" });
        var windows = Substitute.For<IWindowManager>();
        windows.ShowDialog(Arg.Any<IDialog>()).Returns(Task.FromResult(true));
        var localization = new LocalizationService();
        var loader = Substitute.For<IHexIdeHost>();
        loader.ToolWindows.Returns(new AddinToolWindowService(localization));
        loader.Menus.Returns(new AddinMenuService(localization));
        var addins = new AddinRegistry(verifier, Substitute.For<ISettingsService>(), Substitute.For<IDeveloperModeService>(), windows,
            localization, new ConsentStore(Path.Combine(root, "consent.json")),
            new RevocationStore(null, Path.Combine(root, "revocations")), Path.Combine(root, "state.json"), packages);
        addins.LoadAll(loader);
        await addins.PromptPendingConsentAsync();
        addins.GetStatus(package).Should().Be(AddinStatus.Loaded);
        return (addins, package);
    }
}

/// <summary>
/// The engine registered through the IDE's composed host is the one its runner and debugger reach (#5616): the host and the
/// forwarders share one registration, which the host's factory binding gives it.
/// </summary>
public sealed class RunEngineCompositionTests : CompositionTestBase
{
    [AvaloniaFact]
    public void The_composed_host_registers_the_engine_the_IDEs_debugger_forwards_to()
    {
        // The base registers the provider's engine through the composed host, as HexIDE.Desktop does.
        var composition = Composition;
        Engine.Should().NotBeNull("the composed host has run engines to register with");
        var engine = Engine!;
        RunningProgram.Current.Should().BeSameAs(engine.Program);
        engine.Debugger.Pause();
        composition.DebugController.State.Should().Be(engine.Debugger.State);

        var provider = new AddinOrigin(RegistrationGrants.BasicProvider, "HexIDE.Basic") { IsComposed = true };
        FluentActions.Invoking(() => ((IRunEngineRegistration)new AddinOwnedHost(Composition.HexIdeHost, null!, provider))
                .RegisterRunEngine(_ => RunEngineRegistrationTests.Engine()))
            .Should().Throw<InvalidOperationException>().WithMessage("*HexIDE.Basic (hexide.basic) runs them already.");
    }
}
