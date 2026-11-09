using HexIDE.Debugging;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Debugging;

namespace HexIDE.IDE;

/// <summary>A project runner whose engine is the interpreter, as DI builds one, from the arguments a test already has.</summary>
internal static class TestRunners
{
    public static ProjectRunnerService WithInterpreter(IEventBus eventBus, IWindowManager windowManager, IProjectManager projectManager,
        ILocalizationService localization, IDebugController debugController, IBreakpointService breakpointService,
        WatchService watchService, RunScope runScope, ExternalDebugService? externalDebugger = null,
        ArchiveProjectService? archives = null, DebugLaunchService? debugLaunches = null, IRunChoiceStore? runChoices = null,
        IStatusBarService? statusBar = null) =>
        new(eventBus, projectManager, localization, debugController, breakpointService, watchService, runScope,
            new InterpreterRunEngine(windowManager, localization, debugController), externalDebugger, archives, debugLaunches,
            runChoices, statusBar);
}
