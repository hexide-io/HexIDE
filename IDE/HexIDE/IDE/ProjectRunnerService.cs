using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Labs.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using HexIDE.Controls;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Localization;
using HexIDE.Runtime;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Utils;
using HexIDE.VisualDesigner;
using PropertyChanged.SourceGenerator;

namespace HexIDE.IDE;

public partial class ProjectRunnerService : IProjectRunnerService
{
    // The program window that was active when a Pause stop brought the IDE forward, for Continue to restore (#4262).
    private Window? _pauseWindow;
    internal IWindowRaiser WindowRaiser { get; set; } = new WindowRaiser();
    // Every open window. The desktop lifetime lists them too, but a headless session has no desktop lifetime.
    private static readonly HashSet<Window> opened = TrackWindows();

    private static HashSet<Window> TrackWindows()
    {
        var windows = new HashSet<Window>();
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => windows.Add(window));
        Window.WindowClosedEvent.AddClassHandler<Window>((window, _) => windows.Remove(window));
        return windows;
    }

    private readonly IEventBus eventBus;
    private readonly IProjectManager projectManager;
    private readonly ILocalizationService localization;
    private readonly IDebugController debugController;
    private readonly IRunEngine runEngine;
    private readonly IBreakpointService breakpointService;
    private readonly WatchService watchService;
    private readonly RunScope runScope;
    private readonly ExternalDebugService? externalDebugger;
    private readonly DebugLaunchService? debugLaunches;
    private readonly IRunChoiceStore? runChoices;
    private readonly ChosenLaunchAnswer? chosenLaunch;
    private readonly IStatusBarService? statusBar;
    private readonly CodeTrust? trust;
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<ProjectDefinition, object> defaultNotices = new();

    // A Run-To-Cursor target requested while IDLE — applied AFTER the run-start Reset (which clears the controller's
    // target), so the fresh run breaks at it. Consumed once in RunProject.
    private (DocumentIdentity Document, int Line)? _pendingRunTo;

    [Notify]
    [AlsoNotify(nameof(IsRunning), nameof(CanStartDefaultProject), nameof(CanStartDefaultProjectWithFullCompile), nameof(CanBreakProject), nameof(CanContinueProject), nameof(CanStepIntoProject), nameof(CanStepOverProject), nameof(CanStepOutProject), nameof(CanRunToCursor), nameof(CanEndProject), nameof(CanRestartProject))]
    private System.IDisposable? runHandle;

    public bool IsRunning => runHandle != null || externalDebugger?.IsActive == true;

    /// <summary>F5's kept answer of the chosen launch, for tests to see what it holds; null without add-in launches.</summary>
    internal ChosenLaunchAnswer? ChosenLaunch => chosenLaunch;

    /// <inheritdoc />
    public event Action? RunStarting;

    /// <inheritdoc />
    public event Action<string>? StartFailed;

    /// <inheritdoc />
    public event Action<string>? RunFailed;

    /// <inheritdoc />
    public event Action<RunFailure>? ErrorMet
    {
        add => runEngine.ErrorMet += value;
        remove => runEngine.ErrorMet -= value;
    }

    /// <inheritdoc />
    public Func<RunFailure, Task<bool>>? HaltHost
    {
        get => runEngine.HaltHost;
        set => runEngine.HaltHost = value;
    }

    /// <inheritdoc />
    public ErrorLocation? FailureLocation { get; private set; }

    private ErrorTrapping errorTrapping = ErrorTrapping.BreakOnUnhandledErrors;
    public ErrorTrapping ErrorTrapping
    {
        get => errorTrapping;
        set
        {
            errorTrapping = value;
            ApplyErrorTrapping();
        }
    }

    private void ApplyErrorTrapping()
    {
        if (debugController is IDebugGate gate)
            gate.ErrorTrapping = errorTrapping;
    }

    // Raises a failure event, with where its error was raised readable while the handlers run (#955).
    private void Report(Action<string>? failed, RunFailure failure)
    {
        FailureLocation = failure.Location;
        try { failed?.Invoke(failure.Text); }
        finally { FailureLocation = null; }
    }

    /// <summary>
    /// The project currently running, or null when nothing is.
    /// </summary>
    /// <remarks>
    /// Separate from <c>RunHandle</c>, which is how a run is torn down. This is which project it is, and it
    /// is what scopes anything pushed into the run by name: the debug controller takes a bare module name,
    /// and a group's other project may hold a module called the same thing. Held in
    /// <see cref="RunScope"/> rather than here, because a code editor needs to read it and cannot depend on
    /// this service.
    /// </remarks>
    public ProjectDefinition? RunningProject
    {
        get => runScope.RunningProject;
        private set
        {
            _pauseWindow = null;
            runScope.RunningProject = value;
            if (value is null) runScope.End();
            else
            {
                runScope.Begin(projectManager.LoadedProjects.Contains(value) ? projectManager.LoadedProjects : [value]);
                RunStarting?.Invoke();
            }
        }
    }

    private readonly HexIDE.Projects.ArchiveProjectService? archives;

    public ProjectRunnerService(IEventBus eventBus,
        IProjectManager projectManager,
        ILocalizationService localization,
        IDebugController debugController,
        IBreakpointService breakpointService,
        WatchService watchService,
        RunScope runScope,
        IRunEngine runEngine,
        ExternalDebugService? externalDebugger = null,
        HexIDE.Projects.ArchiveProjectService? archives = null,
        DebugLaunchService? debugLaunches = null,
        IRunChoiceStore? runChoices = null,
        IStatusBarService? statusBar = null,
        CodeTrust? trust = null)
    {
        _ = opened;   // registers the tracker's handlers before a program opens its first window
        this.trust = trust;
        this.debugLaunches = debugLaunches;
        this.runChoices = runChoices;
        if (debugLaunches is not null && runChoices is not null)
        {
            chosenLaunch = new ChosenLaunchAnswer(debugLaunches, runChoices, projectManager);
            chosenLaunch.Forgotten += RequeryStart;
        }
        this.statusBar = statusBar;
        this.eventBus = eventBus;
        this.projectManager = projectManager;
        this.localization = localization;
        this.debugController = debugController;
        this.breakpointService = breakpointService;
        this.watchService = watchService;
        this.runScope = runScope;
        this.runEngine = runEngine;
        this.externalDebugger = externalDebugger;
        this.archives = archives;
        if (archives != null) archives.FocusChanged += RequeryStart;
        // What F5 starts for a project an add-in read is the launch its user chose, while that launch is registered.
        if (runChoices != null) runChoices.ChoiceChanged += _ => RequeryStart();
        if (debugLaunches != null) debugLaunches.LaunchesChanged += RequeryStart;
        if (externalDebugger is not null)
            externalDebugger.Changed += () =>
            {
                var running = externalDebugger.IsActive && externalDebugger.State is
                    HexIDE.Dap.DapSessionState.Starting or HexIDE.Dap.DapSessionState.Running or HexIDE.Dap.DapSessionState.Stopped;
                if (running && !externalRunTracked)
                {
                    runScope.Begin(projectManager.LoadedProjects);
                    externalRunTracked = true;
                    RunStarting?.Invoke();
                }
                else if (!running && externalRunTracked)
                {
                    runScope.End();
                    externalRunTracked = false;
                }
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsRunning)));
                CommandManager.InvalidateRequerySuggested();
            };
        // Break/Continue toggle the debug state without touching `runningProject`, so re-query the run commands
        // when the controller pauses/resumes (otherwise the Break/Continue buttons stay stale).
        // This subscription precedes the view model's Pause notification, which raises the IDE (#4262).
        debugController.Stopped += info =>
        {
            if (info.Reason == StopReason.Pause && externalDebugger?.IsActive != true)
            {
                var windows = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows ?? [];
                var active = windows.Concat(opened).FirstOrDefault(window => window.IsActive);
                _pauseWindow = active is not null && RunningProgram.Current.Form(active) is { IsRunning: true }
                    ? active : null;
            }
            else if (info.Reason != StopReason.Step) _pauseWindow = null;
            CommandManager.InvalidateRequerySuggested();
        };
        debugController.Continued += CommandManager.InvalidateRequerySuggested;

        // A breakpoint toggled (in the gutter or via MCP) while a run is live takes effect immediately, matching
        // VB6 — push just that document's set to the controller. Launch-time application is ApplyBreakpoints().
        // (Persistence/load is UserSidecarService's job, on project open/save.)
        breakpointService.BreakpointsChanged += document =>
        {
            // Scoped to the project that is actually running. The controller is told a bare module name,
            // because a run is one project; so a toggle in a group's OTHER project, which may hold a module
            // of the same name, would otherwise move the running project's breakpoints.
            if (RunningProject is { } running && document.IsIn(running))
            {
                PushBreakpoints(document);
            }
        };

        // A break-type watch added / edited / removed while running takes effect immediately (VB6 evaluates watches
        // live). CollectionChanged also fires on an in-place Edit (the dialog re-inserts the row), so a type change
        // (Expression <-> Break-When-True/Changed) is picked up too.
        watchService.Watches.CollectionChanged += (_, _) =>
        {
            if (IsRunning)
                ApplyWatchBreaks();
        };

        // The code editor raises this when the user confirms VB6's "reset your project?" prompt after editing while
        // running (routed via the bus so the editor doesn't take a cyclic dependency on this service).
        eventBus.Subscribe<EndProjectRequestedEvent>(_ => EndProject());
    }

    private void OnIsRunningChanged() => CommandManager.InvalidateRequerySuggested();

    private void RequeryStart()
    {
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(CanStartDefaultProject)));
        CommandManager.InvalidateRequerySuggested();
    }

    private bool externalRunTracked;

    public string? CodeEditNote(bool edit = false) => runScope.Note(
        externalDebugger?.IsActive == true ? externalDebugger.State == HexIDE.Dap.DapSessionState.Stopped
            : debugController.State == DebugState.Paused, edit);

    private void PushBreakpoints(DocumentIdentity document)
    {
        var lines = breakpointService.GetBreakpoints(document);
        debugController.SetBreakpoints(document.Name, lines);
        var conditions = new Dictionary<int, string>();
        foreach (var line in lines)
            if (breakpointService.GetCondition(document, line) is { } condition)
                conditions[line] = condition;
        debugController.SetBreakpointConditions(document.Name, conditions);
    }

    private void ApplyBreakpoints(ProjectDefinition project)
    {
        // Full resync from the store (a document cleared since the last run must not keep a stale controller set —
        // Reset() preserves breakpoints), scoped to THIS project's own documents so a different loaded project
        // (a project group with a colliding module name) can't push its breakpoints onto this run.
        debugController.ClearBreakpoints();
        foreach (var form in project.Forms)
            PushBreakpoints(DocumentIdentity.For(form));
        foreach (var module in project.Modules)
            PushBreakpoints(DocumentIdentity.For(module));
    }

    // Push the current break-type watches to the gate. Watch-Expression (display) watches don't affect execution, so
    // only Break-When-True / Break-When-Changed are sent; every statement re-evaluates them against the executing
    // frame (inherently slow — that's true of VB6 break-watches too), so no break-types = full-speed run.
    private void ApplyWatchBreaks()
    {
        var specs = watchService.Watches
            .Where(w => w.Type is WatchType.BreakWhenTrue or WatchType.BreakWhenChanged)
            .Select(w => new WatchBreakSpec(w.Expression, w.Type == WatchType.BreakWhenChanged))
            .ToList();
        debugController.SetWatchBreaks(specs);
    }

    public void RunProject(ProjectDefinition projectDefinition, bool stepInto = false)
    {
        if (externalDebugger?.IsActive == true) return;
        if (!runEngine.Runs(projectDefinition))
        {
            Serilog.Log.Warning("{Project} was not run: {Contributor} read it, and the run engine does not run it",
                projectDefinition.Name, projectDefinition.ContributedBy);
            return;
        }
        // Code from outside this computer runs only once the developer says so, asked before anything is claimed or
        // built (#1121). The start resumes from here on a Yes.
        if (AskFirst(projectDefinition, () => RunProject(projectDefinition, stepInto)))
            return;
        externalDebugger?.ClearSessionResult();
        eventBus.Publish(new ApplyAllUnsavedChangesEvent());

        // An idle Run To Cursor's target belongs to this start alone. Taken here, so a start that fails leaves
        // nothing armed: it used to stay set when a syntax error stopped the start, and the next plain run paused at
        // that line as though at a breakpoint.
        var runTo = _pendingRunTo;
        _pendingRunTo = null;

        // A start that never became a run, reported as one, in VB6's words. It was a message box nothing recorded (#759).
        if (runEngine.RefusesToStart(projectDefinition) is { } refusal)
        {
            StartFailed?.Invoke(refusal);
            return;
        }

        RunInEngine(projectDefinition, stepInto, runTo).ListenErrors();
    }

    private async Task RunInEngine(ProjectDefinition project, bool stepInto, (DocumentIdentity Document, int Line)? runTo)
    {
        // Claimed before anything can execute, not once the task is in hand: a breakpoint on the first statement of
        // Form_Load breaks while the form is still being loaded, and an editor asked to show the current-statement bar
        // needs to know which project is running by then.
        RunningProject = project;

        // Fresh debug session: clear any prior break/abort state, then apply the current breakpoints so the very first
        // statement can already be a break.
        debugController.Reset();
        ApplyBreakpoints(project);
        ApplyErrorTrapping();
        ApplyWatchBreaks();   // push Break-When-True/Changed watches so the run can break on them from line 1
        if (runTo is { } rt)   // an idle Run-To-Cursor: arm the target now that Reset has cleared it
            debugController.RunToCursor(rt.Document.Name, rt.Line);
        // F8-from-idle: arm step BEFORE the first statement runs, so the run breaks at line 1 (VB6 start-and-step).
        // Arming while Running (fresh Reset) simply sets the flag; the first gate breaks.
        if (stepInto)
            debugController.StepInto();

        // What ended the run, if anything did. Reported only once the run is torn down, so nothing reads as running while
        // the dialog is up.
        RunFailure? failure;
        try
        {
            var start = runEngine.Prepare(project);
            if (start.Failure is { } failed)
            {
                // The program could not be built, so the run never began, but Reset() above had already put the
                // controller in Running and the project was claimed. Both used to stay that way, the error went only to
                // the log, and the start was reported as having worked (#590). Torn down here, before RunHandle exists,
                // so nothing ever reads as running. Raised before the first await, so StartFailed comes before the start
                // call returns, as its contract says.
                debugController.Stop();
                RunningProject = null;
                Report(StartFailed, failed);
                return;
            }

            var run = start.Run!;
            RunHandle = new ActionDisposable(run.End);
            failure = await run.RunToEndAsync();
        }
        finally
        {
            // The run ended by ANY route: it ran to completion, the user closed the running form, End was pressed, or a
            // fault ended it. Tear the debug session down so a walk paused at a breakpoint is unwound and the controller
            // (and the editor's current-statement bar) return to a clean state. Idempotent with EndProject's own Stop();
            // on the plain window-close path this is the ONLY teardown.
            debugController.Stop();
            RunHandle = null;
            RunningProject = null;
        }

        if (failure is not null)
            Report(RunFailed, failure);
    }

    /// <inheritdoc />
    public async Task<bool> MayRunFileAsync(string path) =>
        trust?.Unapproved(path) is not { } file || await trust.AskAsync(System.IO.Path.GetFileName(path), [file]);

    /// <summary>The files HexIDE is asking whether to run, while it asks; null otherwise. For automation to report.</summary>
    public IReadOnlyList<UntrustedFile>? AskingToRun => trust?.Asking;

    /// <summary>
    /// Whether <paramref name="project"/>'s start waits on the question <see cref="CodeTrust"/> asks: true when its code
    /// came from outside this computer and hasn't been allowed. <paramref name="start"/> runs again on a Yes. A No, or a
    /// start while the question is already up, starts nothing, and leaves no idle Run To Cursor armed.
    /// </summary>
    private bool AskFirst(ProjectDefinition project, Action start)
    {
        if (trust?.Unapproved(project) is not { Count: > 0 } files)
            return false;
        if (trust.Asking is null)
            Ask().ListenErrors();
        return true;

        async Task Ask()
        {
            if (await trust.AskAsync(project.Name, files) && !IsRunning)
                start();
            else
                _pendingRunTo = null;
        }
    }

    /// <summary>Whether an idle Run To Cursor is still waiting for a start. For tests.</summary>
    internal bool HasPendingRunToCursor => _pendingRunTo is not null;

    public void RunStartupProject(bool stepInto = false)
    {
        if (archives?.IsActive == true) return;
        if (projectManager.StartupProject is {} startupProject)
        {
            RunProject(startupProject, stepInto);
        }
    }

    /// <inheritdoc />
    public void StartDefaultProject()
    {
        if (archives?.IsActive == true) return;
        if (projectManager.StartupProject is not { } startupProject) return;
        if (LaunchChosenFor(startupProject) is { } launch)
        {
            StartContributedLaunch(launch, startupProject);
            return;
        }
        RunProject(startupProject);
    }

    /// <inheritdoc />
    public void StartContributedLaunch(ContributedDebugLaunch launch, ProjectDefinition project)
    {
        if (debugLaunches is null || archives?.IsActive == true || IsRunning) return;
        if (AskFirst(project, () => StartContributedLaunch(launch, project)))
            return;
        externalDebugger?.ClearSessionResult();
        eventBus.Publish(new ApplyAllUnsavedChangesEvent());
        debugLaunches.StartAsync(launch, project).ListenErrors();
    }

    // The effective F5 launch, from the user's choice or the project's add-in default. A choice whose launch is gone,
    // or no longer runs this project, falls back to the interpreter and says so; it stays chosen, for when the add-in
    // is back. The launch is asked now, whatever F5's state kept of its last answer.
    private ContributedDebugLaunch? LaunchChosenFor(ProjectDefinition project)
    {
        if (chosenLaunch is null || runChoices is null || debugLaunches?.ChoiceFor(project, runChoices) is not { } key) return null;
        if (chosenLaunch.Ask(project) is { } launch)
        {
            NoticeDefault(project, launch);
            return launch;
        }
        statusBar?.SetTemporaryMessage(
            string.Format(localization.GetString("Str.Debug.RunWith.Unavailable"), ContributedDebugLaunch.TitleOf(key)),
            TimeSpan.FromSeconds(10));
        return null;
    }

    public void BreakCurrentProject()
    {
        if (externalDebugger?.IsActive == true) { _ = externalDebugger.CommandAsync("pause"); return; }
        // Break immediately while idle, otherwise at the next statement gate.
        debugController.Pause();
    }

    public void StepIntoProject()
    {
        if (externalDebugger?.IsActive == true) { _ = externalDebugger.CommandAsync("stepIn"); return; }
        // F8 (Step Into), VB6-faithful: from idle it starts the project and breaks at the first executed
        // statement; while paused it steps one statement; while running-not-paused it breaks at the next.
        if (!IsRunning)
            RunStartupProject(stepInto: true);
        else if (debugController.State == DebugState.Paused)
            debugController.StepInto();
        else
            debugController.Pause();
    }

    public void StepOverProject()
    {
        if (externalDebugger?.IsActive == true) { _ = externalDebugger.CommandAsync("next"); return; }
        // Shift+F8 (Step Over): while paused, run any called procedure to completion and break at the next
        // statement in the current frame. From idle/running it behaves like Step Into (start-and-step / break next).
        if (!IsRunning)
            RunStartupProject(stepInto: true);
        else if (debugController.State == DebugState.Paused)
            debugController.StepOver();
        else
            debugController.Pause();
    }

    public void StepOutProject()
    {
        if (externalDebugger?.IsActive == true) { _ = externalDebugger.CommandAsync("stepOut"); return; }
        // Ctrl+Shift+F8 (Step Out): while paused, run until the current procedure returns and break in its caller.
        // From idle/running it behaves like Step Into (start-and-step / break next).
        if (!IsRunning)
            RunStartupProject(stepInto: true);
        else if (debugController.State == DebugState.Paused)
            debugController.StepOut();
        else
            debugController.Pause();
    }

    public void RunToCursorProject(DocumentIdentity document, int line)
    {
        if (externalDebugger?.IsActive == true)
        {
            if (document.AbsolutePath is { } path)
                _ = externalDebugger.RunToCursorAsync(new Uri(System.IO.Path.GetFullPath(path)).AbsoluteUri, line);
            return;
        }
        // Run To Cursor (Ctrl+F8): a one-shot break at (module, line). Paused → arm + Continue; running → arm (breaks
        // when reached); idle → start the project and arm the target after the run-start sequence (a plain run, NOT
        // start-and-step — it runs freely to the cursor line).
        //
        // Deliberately NOT scoped to the running project, unlike the live breakpoint push and Set Next
        // Statement. Those act on a run that is already going; this one may START it, and from idle the
        // run that starts is the startup project's whatever document was pointed at. Refusing a document
        // in a sibling project would be the defensible rule, but this method has no channel to say so —
        // it returns void and is bound to a keystroke. Left as it was, and stated rather than hidden.
        if (!IsRunning)
        {
            _pendingRunTo = (document, line);
            RunStartupProject();
        }
        else if (debugController.State == DebugState.Paused)
        {
            debugController.RunToCursor(document.Name, line);
            debugController.Continue();
        }
        else
        {
            debugController.RunToCursor(document.Name, line);
        }
    }

    public void ContinueProject()
    {
        if (externalDebugger?.IsActive == true) { _ = externalDebugger.CommandAsync("continue"); return; }
        // Steps retain the Pause window; only F5 restores it, once, before releasing the interpreter gate.
        var window = _pauseWindow;
        _pauseWindow = null;
        if (window is { IsVisible: true } && debugController.State == DebugState.Paused)
        {
            foreach (var other in RunningProgram.Current.ShownWindows(window))
                if (other is Window form && !ReferenceEquals(form, window))
                    WindowRaiser.Raise(form);
            window.Activate();
        }
        debugController.Continue();
    }

    public void EndProject()
    {
        if (externalDebugger?.IsActive == true) { _ = externalDebugger.StopAsync(); return; }
        _pauseWindow = null;
        // Abort first so a paused interpreter unwinds via StopExecutionSignal, then cancel the token (closes the
        // window). Order matters: Stop() releases the break gate that Dispose()'s cancel alone cannot.
        debugController.Stop();
        RunHandle?.Dispose();
        RunHandle = null;
        RunningProject = null;
    }

    public void RestartProject()
    {
        if (externalDebugger?.IsActive == true) return;
        EndProject();
        RunStartupProject();
    }

    private void NoticeDefault(ProjectDefinition project, ContributedDebugLaunch launch)
    {
        if (runChoices?.HasChoice(project) != false || defaultNotices.TryGetValue(project, out _)) return;
        defaultNotices.Add(project, new object());
        statusBar?.SetTemporaryMessage(
            string.Format(localization.GetString("Str.Debug.RunWith.Default"), launch.Title), TimeSpan.FromSeconds(10));
    }

    // A launch's kept answer avoids calling add-in code at every edit; a start asks again.
    public bool CanStartDefaultProject
    {
        get
        {
            if (archives?.IsActive == true || IsRunning || projectManager.StartupProject is not { } project) return false;
            if (runEngine.Runs(project)) return true;
            if (chosenLaunch?.Kept(project) is not { } launch) return false;
            NoticeDefault(project, launch);
            return true;
        }
    }
    public bool CanRunStartupProject => archives?.IsActive != true && !IsRunning && projectManager.StartupProject is { } project && runEngine.Runs(project);
    public bool CanStartDefaultProjectWithFullCompile => CanRunStartupProject;
    public bool CanBreakProject => externalDebugger?.IsActive == true
        ? !externalDebugger.IsBusy && externalDebugger.State == Dap.DapSessionState.Running && externalDebugger.ThreadId is not null
        : IsRunning && debugController.State == DebugState.Running;
    public bool CanContinueProject => externalDebugger?.IsActive == true
        ? !externalDebugger.IsBusy && externalDebugger.State == Dap.DapSessionState.Stopped && externalDebugger.ThreadId is not null
        : IsRunning && debugController.State == DebugState.Paused;
    // F8 works from idle (start-and-step, needs a startup project) or any running/paused state.
    public bool CanStepIntoProject => externalDebugger?.IsActive == true ? CanContinueProject : CanRunStartupProject || IsRunning;
    public bool CanStepOverProject => CanStepIntoProject;
    public bool CanStepOutProject => CanStepIntoProject
        && (externalDebugger?.IsActive == true || debugController.CurrentStop is not { HasStatement: false });
    public bool CanRunToCursor => externalDebugger?.IsActive == true
        ? externalDebugger.CanRunToCursor
        : (CanRunStartupProject || IsRunning) && debugController.CurrentStop is not { HasStatement: false };
    public bool CanEndProject => IsRunning;
    public bool CanRestartProject => IsRunning && externalDebugger?.IsActive != true;

    /// <inheritdoc />
    public NoEngineStartup? StartupWithNoEngine =>
        !IsRunning && projectManager.StartupProject is { } project && !runEngine.Runs(project)
            ? new NoEngineStartup(project, debugLaunches?.For(project) ?? [])
            : null;
}
