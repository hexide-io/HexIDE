using HexIDE.Debugging;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.Running;

namespace HexIDE.IDE;

/// <summary>A project runner whose engine is the interpreter, as DI builds one, from the arguments a test already has.</summary>
internal static class TestRunners
{
    public static ProjectRunnerService WithInterpreter(IEventBus eventBus, IWindowManager windowManager, IProjectManager projectManager,
        ILocalizationService localization, IDebugGate debugController, IBreakpointService breakpointService,
        WatchService watchService, RunScope runScope, ExternalDebugService? externalDebugger = null,
        ArchiveProjectService? archives = null, DebugLaunchService? debugLaunches = null, IRunChoiceStore? runChoices = null,
        IStatusBarService? statusBar = null) =>
        new(eventBus, projectManager, localization, debugController, breakpointService, watchService, runScope,
            new InterpreterRunEngine(Services(new ProgramHost(windowManager, localization),
                // The IDE always gives the engine its real window-maker (#5616), even where a test substitutes the rest.
                windows: windowManager as WindowManager ?? new WindowManager(localization)), debugController), externalDebugger, archives, debugLaunches,
            runChoices, statusBar);

    /// <summary>What an engine a test makes directly is given (#5616), with the IDE's setting read from <paramref name="settings"/>.</summary>
    public static RunEngineServices Services(IProgramHost host, ISettingsService? settings = null,
        IProgramDialogWindows? windows = null) =>
        new(host, windows ?? Substitute.For<IProgramDialogWindows>(), () => settings?.RDCoreWin64OnlySetOnWindows ?? false,
            () => Static.SingleView);
}
