using HexIDE.Debugging;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Debugging;

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
            new InterpreterRunEngine(windowManager, localization, debugController), externalDebugger, archives, debugLaunches,
            runChoices, statusBar);

    /// <summary>
    /// A project runner around <paramref name="engine"/>, for a test about what the runner decides rather than what a program
    /// does. It takes Core's debugger contract, so such a test names nothing of the interpreter's (#3663).
    /// </summary>
    public static ProjectRunnerService WithEngine(IRunEngine engine, IEventBus eventBus, IProjectManager projectManager,
        ILocalizationService localization, IDebugController debugController, IBreakpointService breakpointService,
        WatchService watchService, RunScope runScope, ExternalDebugService? externalDebugger = null,
        ArchiveProjectService? archives = null, DebugLaunchService? debugLaunches = null, IRunChoiceStore? runChoices = null,
        IStatusBarService? statusBar = null) =>
        new(eventBus, projectManager, localization, debugController, breakpointService, watchService, runScope, engine,
            externalDebugger, archives, debugLaunches, runChoices, statusBar);
}
