using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
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
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Utils;
using HexIDE.VisualDesigner;
using PropertyChanged.SourceGenerator;

namespace HexIDE.IDE;

public partial class ProjectRunnerService : IProjectRunnerService
{
    private readonly IEventBus eventBus;
    private readonly IWindowManager windowManager;
    private readonly IProjectManager projectManager;
    private readonly ILocalizationService localization;
    private readonly IDebugController debugController;
    private readonly IBreakpointService breakpointService;
    private readonly WatchService watchService;
    private readonly RunScope runScope;
    private readonly ExternalDebugService? externalDebugger;

    // A Run-To-Cursor target requested while IDLE — applied AFTER the run-start Reset (which clears the controller's
    // target), so the fresh run breaks at it. Consumed once in RunProject.
    private (DocumentIdentity Document, int Line)? _pendingRunTo;

    [Notify]
    [AlsoNotify(nameof(IsRunning), nameof(CanStartDefaultProject), nameof(CanStartDefaultProjectWithFullCompile), nameof(CanBreakProject), nameof(CanContinueProject), nameof(CanStepIntoProject), nameof(CanStepOverProject), nameof(CanStepOutProject), nameof(CanRunToCursor), nameof(CanEndProject), nameof(CanRestartProject))]
    private System.IDisposable? runHandle;

    public bool IsRunning => runHandle != null || externalDebugger?.IsActive == true;

    /// <inheritdoc />
    public event Action<string>? StartFailed;

    /// <inheritdoc />
    public event Action<string>? RunFailed;

    /// <inheritdoc />
    public ErrorLocation? FailureLocation { get; private set; }

    // Raises a failure event, with where its error was raised readable while the handlers run (#955).
    private void Report(Action<string>? failed, string text, Exception? cause)
    {
        FailureLocation = ErrorLocation.Of(cause);
        try { failed?.Invoke(text); }
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
        private set => runScope.RunningProject = value;
    }

    private readonly HexIDE.Projects.ArchiveProjectService? archives;

    public ProjectRunnerService(IEventBus eventBus,
        IWindowManager windowManager,
        IProjectManager projectManager,
        ILocalizationService localization,
        IDebugController debugController,
        IBreakpointService breakpointService,
        WatchService watchService,
        RunScope runScope,
        ExternalDebugService? externalDebugger = null,
        HexIDE.Projects.ArchiveProjectService? archives = null)
    {
        this.eventBus = eventBus;
        this.windowManager = windowManager;
        this.projectManager = projectManager;
        this.localization = localization;
        this.debugController = debugController;
        this.breakpointService = breakpointService;
        this.watchService = watchService;
        this.runScope = runScope;
        this.externalDebugger = externalDebugger;
        this.archives = archives;
        if (archives != null) archives.FocusChanged += () =>
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(CanStartDefaultProject)));
            CommandManager.InvalidateRequerySuggested();
        };
        if (externalDebugger is not null)
            externalDebugger.Changed += () =>
            {
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsRunning)));
                CommandManager.InvalidateRequerySuggested();
            };
        // A running program's CommonDialog shows the IDE's own dialogs (#1084), whichever window the form runs in.
        HostDialogs.File = request => MDIStandaloneStandardLib.ShowFileDialog(windowManager, request);
        HostDialogs.Print = windowManager.ShowPrintDialog;
        HostDialogs.Color = windowManager.ShowColorDialog;
        HostDialogs.Font = initial => MDIStandaloneStandardLib.ShowFontDialog(windowManager, initial);

        // Break/Continue toggle the debug state without touching `runningProject`, so re-query the run commands
        // when the controller pauses/resumes (otherwise the Break/Continue buttons stay stale).
        debugController.Stopped += _ => CommandManager.InvalidateRequerySuggested();
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
                debugController.SetBreakpoints(document.Name, breakpointService.GetBreakpoints(document));
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

        void PushBreakpoints(DocumentIdentity document)
        {
            var lines = breakpointService.GetBreakpoints(document);
            if (lines.Count > 0)
                debugController.SetBreakpoints(document.Name, lines);
        }
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
        eventBus.Publish(new ApplyAllUnsavedChangesEvent());

        // An idle Run To Cursor's target belongs to this start alone. Taken here, so a start that fails leaves
        // nothing armed: it used to stay set when a syntax error stopped the start, and the next plain run paused at
        // that line as though at a breakpoint.
        var runTo = _pendingRunTo;
        _pendingRunTo = null;

        if (projectDefinition.StartsAtSubMain)
        {
            RunSubMain(projectDefinition, stepInto, runTo);
            return;
        }

        if (projectDefinition.StartupForm is { } form)
        {
            async Task WindowTask()
            {
                var tokenSource = new CancellationTokenSource();


                // Claimed before anything can execute, not once the task is in hand: a breakpoint on the
                // first statement of Form_Load breaks while the form is still being loaded, and an editor
                // asked to show the current-statement bar needs to know which project is running by then.
                RunningProject = projectDefinition;

                // Fresh debug session: clear any prior break/abort state, then apply the current breakpoints so the
                // very first statement can already be a break.
                debugController.Reset();
                var session = debugController.Session;
                ApplyBreakpoints(projectDefinition);
                ApplyWatchBreaks();   // push Break-When-True/Changed watches so the run can break on them from line 1
                if (runTo is { } rt)   // an idle Run-To-Cursor: arm the target now that Reset has cleared it
                    debugController.RunToCursor(rt.Document.Name, rt.Line);
                // F8-from-idle: arm step BEFORE the first statement runs, so the run breaks at line 1 (VB6
                // start-and-step). Arming while Running (fresh Reset) simply sets the flag; the first gate breaks.
                if (stepInto)
                    debugController.StepInto();

                // The run's first fault, from an event procedure in any of its forms. A fault cannot be
                // trapped, so the procedure it hit stopped at that statement, and carrying on would run the
                // rest of the program as though the statement had worked. The run ends instead, and says why
                // (#754). Listened for only while this run is live, so a fault from a run that has already
                // ended is logged and nothing else.
                Exception? fault = null;
                void OnFaulted(VBWindowContext sender, Exception e)
                {
                    if (!sender.IsInDebugSession(debugController, session) || fault is not null)
                        return;   // the first fault ends the run; any after it come from the same collapse
                    fault = e;
                    // Abort first, as EndProject does. A fault is caught where its own event procedure was
                    // dispatched, so a procedure that raised that event (a Click that set Text1.Text, firing
                    // Text1_Change) would otherwise carry on past the statement, and so would any other walk
                    // still running. Stopping the controller ends each of them at its next statement. The raiser is
                    // still waiting on the event when it faults, however the event yielded on the way, since it waits
                    // for what it raised before its next statement (#766).
                    debugController.Stop();
                    // Then close the form. Posted, because a fault can arrive while RunForm is still showing the
                    // form, and a window closed before the run-task watches for it closing would leave the run
                    // waiting forever. A second cancel, after End has already cancelled, does nothing.
                    Dispatcher.UIThread.Post(() => tokenSource.Cancel());
                }

                // End in any of the run's procedures ends the run, closing the form as the End command does, and
                // reports nothing: ending is not an error (#775). The close is posted for the same reason as the
                // fault's. Unlike a fault, End needs no Stop() here: the interpreter's own Ending flag already stops
                // every walk of it, ahead of the debugger's gate, and a run has one interpreter today. The finally
                // below stops the controller once the form has closed.
                void OnEndReached(BasicInterpreter sender)
                {
                    if (sender.IsInDebugSession(debugController, session))
                        Dispatcher.UIThread.Post(() => tokenSource.Cancel());
                }

                VBWindowContext.Faulted += OnFaulted;
                BasicInterpreter.EndReached += OnEndReached;
                try
                {
                    Task task;
                    try
                    {
                        if (Static.SingleView)
                        {
                            task = RunFormInBrowser(form, tokenSource.Token, out _, debugController);
                        }
                        else
                        {
                            // RunForm starts the form and shows it once Load has run (#822).
                            task = VBLoader.RunForm(form, tokenSource.Token, out _, debugController);
                        }
                    }
                    catch (Exception e)
                    {
                        // The form could not be built, so the run never began, but Reset() above had already
                        // put the controller in Running and the project was claimed. Both used to stay that way,
                        // the error went only to the log, and the start was reported as having worked (#590).
                        // Torn down here, before RunHandle exists, so nothing ever reads as running.
                        //
                        // Building the form parses every module the run loads, so a syntax error in any of them
                        // arrives here, naming its module. It was a message box that nothing recorded, so run_project
                        // said the program had run and ended (#759). Raised before the first await, so StartFailed
                        // comes before the start call returns, as its contract says.
                        debugController.Stop();
                        RunningProject = null;
                        Report(StartFailed, DescribeFailedStart(e, form.Name), e);
                        return;
                    }

                    RunHandle = new ActionDisposable(() => tokenSource.Cancel());
                    await task;
                }
                finally
                {
                    // The run ended by ANY route — ran to completion, the user closed the running form, End was
                    // pressed, or a fault ended it. Tear the debug session down so a walk paused at a breakpoint is
                    // unwound and the controller (and the editor's current-statement bar) return to a clean state.
                    // Idempotent with EndProject's own Stop(); on the plain window-close path this is the ONLY
                    // teardown.
                    VBWindowContext.Faulted -= OnFaulted;
                    BasicInterpreter.EndReached -= OnEndReached;
                    debugController.Stop();
                    RunHandle = null;
                    RunningProject = null;
                }

                // Reported once the run is over, so the dialog never appears beside a run that still looks live.
                if (fault is not null)
                    Report(RunFailed, RunErrorText.ForFault(fault, localization), fault);
            }
            WindowTask().ListenErrors();
        }
        else
        {
            // A start that never became a run, reported as one, in VB6's words. It was a message box nothing
            // recorded (#759).
            StartFailed?.Invoke(new VBCompileErrorException(BasicInterpreter.NoStartupObject).Message);
        }
    }

    /// <summary>
    /// What a start that never became a run says, the same way on both paths (#759). A compile error is the program's
    /// and is given as it is. Anything else is described as a fault in a run would be, with the startup form named
    /// where there is one, since the person did not ask for that form and may not know it was being loaded.
    /// </summary>
    private string DescribeFailedStart(Exception e, string? formName)
    {
        if (e is VBCompileErrorException)
            return e.Message;
        Serilog.Log.Error(e, "The project failed to load, at {Form}", formName ?? "Sub Main");
        var described = Describe(e);
        return formName is null
            ? described
            : string.Format(localization.GetString("Str.ProjectRunner.StartupFormFailedToLoad"), formName, described);
    }

    private string Describe(Exception e) => e switch
    {
        VBRunTimeException runTime => RunErrorText.For(runTime),
        VBCompileErrorException compile => compile.Message,
        _ => RunErrorText.ForFault(e, localization),
    };

    /// <summary>Whether an idle Run To Cursor is still waiting for a start. For tests.</summary>
    internal bool HasPendingRunToCursor => _pendingRunTo is not null;

    /// <summary>
    /// Run a project whose startup object is <c>Sub Main</c>: a program of every standard module, with the project's forms
    /// as templates that Main can load and show (#822).
    /// </summary>
    ///
    /// <remarks>
    /// <para>
    /// Every standard module is loaded, because the startup lookup is project-wide: a <c>Sub Main</c> in
    /// any of them is the entry point, including a <c>Private</c> one in a module other than the first
    /// (measured — see <c>corpus/conformance/sub-main-startup.json</c>). The first module is nominated as
    /// primary only so the debug gate has a name to report; which module holds <c>Main</c> is
    /// <see cref="BasicInterpreter.RunStartupSubMain"/>'s business, not this method's.
    /// </para>
    /// <para>
    /// The run lasts until Main has returned and no form is loaded, so a Main that shows a form and returns leaves the
    /// form running (measured). End, a fault in any of its procedures, and the debugger's Stop end it as they end a form's
    /// run, closing every window. `Debug.Print` reaches the Immediate window through the same standard library the MDI
    /// runtime uses.
    /// </para>
    /// </remarks>
    private void RunSubMain(ProjectDefinition projectDefinition, bool stepInto,
        (DocumentIdentity Document, int Line)? runTo)
    {
        // Shared with the form paths, so the same project sees the same modules whichever startup object it
        // has. Sub Main previously answered this question here and the form paths not at all (#220).
        var (modules, _) = VBLoader.InterpreterModules(projectDefinition);

        if (modules.Count == 0)
        {
            // A start that never became a run (#759), as on the startup-form path.
            StartFailed?.Invoke(new VBCompileErrorException(BasicInterpreter.NoStartupObject).Message);
            return;
        }

        async Task MainTask()
        {

            // Claimed before anything can execute — see the same line on the startup-form path.
            RunningProject = projectDefinition;

            debugController.Reset();
            var session = debugController.Session;
            ApplyBreakpoints(projectDefinition);
            ApplyWatchBreaks();
            if (runTo is { } rt)
                debugController.RunToCursor(rt.Document.Name, rt.Line);
            if (stepInto)
                debugController.StepInto();

            // What ended the run, if anything did. Reported only once the run is torn down, so nothing reads
            // as running while the dialog is up. These used to be MessageBox calls of their own, which no
            // record kept, so get_last_runtime_error never saw an error from Sub Main (#754).
            string? failure = null;
            Exception? failedWith = null;
            var tokenSource = new CancellationTokenSource();

            // A fault in an event procedure of a form Main showed ends the run, as on the form path (#754).
            Exception? fault = null;
            void OnFaulted(VBWindowContext sender, Exception e)
            {
                if (!sender.IsInDebugSession(debugController, session) || fault is not null)
                    return;
                fault = e;
                debugController.Stop();
                Dispatcher.UIThread.Post(() => tokenSource.Cancel());
            }

            // End in any procedure, Main's or a form's, ends the run and reports nothing (#775).
            void OnEndReached(BasicInterpreter sender)
            {
                if (sender.IsInDebugSession(debugController, session))
                    Dispatcher.UIThread.Post(() => tokenSource.Cancel());
            }

            VBWindowContext.Faulted += OnFaulted;
            BasicInterpreter.EndReached += OnEndReached;
            try
            {
                VBProgram program;
                try
                {
                    // Building the program parses every module and runs the pre-pass over it, which throws for a
                    // syntax error, naming its module, and for a declaration it cannot take. It used to sit outside this try, after Reset() and the claim
                    // above, so a throw here left the debugger Running and the project claimed with nothing
                    // running, as #590 did on the form path. It is a start that never became a run, and is
                    // reported the way that path reports one.
                    var context = new VBWindowContext(new MDIStandaloneStandardLib(windowManager));
                    program = new VBProgram(projectDefinition, context, debugController,
                        Static.SingleView ? OpenInBrowser : VBLoader.OpenWindow);
                    // A missing or ambiguous Main is found here, before anything runs, so it too is a start that
                    // never became a run. It used to be raised as the run began, and arrived as a run that ended,
                    // which run_project answered as a success (#759).
                    program.Interpreter.FindStartupSubMain();
                }
                catch (Exception e)
                {
                    debugController.Stop();
                    RunningProject = null;
                    Report(StartFailed, DescribeFailedStart(e, formName: null), e);
                    return;
                }

                RunHandle = new ActionDisposable(() =>
                {
                    debugController.Stop();
                    tokenSource.Cancel();
                });
                // End in Sub Main unwinds it, and RunStartupSubMain takes that as a clean stop (#775).
                await VBLoader.RunSubMain(program, tokenSource.Token);
            }
            catch (Exception e) when (e is not (OperationCanceledException or ImmediateEvalException))
            {
                // A compile error the run meets as it goes arrives here; a missing or ambiguous Main no longer does,
                // since FindStartupSubMain asks before the run begins. A cancellation is left alone: it is how a statement still waiting on a dialog learns that the run was ended. So is the
                // Immediate window refusing an event procedure while paused, as at the form boundary
                // (VBWindowContext): that is its answer to what was typed, not a fault in the run.
                if (e is not (VBCompileErrorException or VBRunTimeException))
                    Serilog.Log.Error(e, "Sub Main stopped on a fault");
                failure = Describe(e);
                failedWith = e;
            }
            finally
            {
                VBWindowContext.Faulted -= OnFaulted;
                BasicInterpreter.EndReached -= OnEndReached;
                debugController.Stop();
                RunHandle = null;
                RunningProject = null;
            }

            if (fault is not null)
                Report(RunFailed, RunErrorText.ForFault(fault, localization), fault);
            else if (failure is not null)
                Report(RunFailed, failure, failedWith);
        }

        MainTask().ListenErrors();
    }

    public void RunStartupProject(bool stepInto = false)
    {
        if (archives?.IsActive == true) return;
        if (projectManager.StartupProject is {} startupProject)
        {
            RunProject(startupProject, stepInto);
        }
    }

    public void BreakCurrentProject()
    {
        if (externalDebugger?.IsActive == true) { _ = externalDebugger.CommandAsync("pause"); return; }
        // Request a break: the next statement gate suspends (a no-op if the program is idle-between-events).
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
        if (externalDebugger?.IsActive == true) return;
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
        // Resume from a break (F5 in break mode).
        debugController.Continue();
    }

    public void EndProject()
    {
        if (externalDebugger?.IsActive == true) { _ = externalDebugger.StopAsync(); return; }
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

    public bool CanStartDefaultProject => archives?.IsActive != true && !IsRunning && projectManager.StartupProject != null;
    public bool CanStartDefaultProjectWithFullCompile => CanStartDefaultProject;
    public bool CanBreakProject => externalDebugger?.IsActive == true
        ? !externalDebugger.IsBusy && externalDebugger.State == Dap.DapSessionState.Running && externalDebugger.ThreadId is not null
        : IsRunning && debugController.State == DebugState.Running;
    public bool CanContinueProject => externalDebugger?.IsActive == true
        ? !externalDebugger.IsBusy && externalDebugger.State == Dap.DapSessionState.Stopped && externalDebugger.ThreadId is not null
        : IsRunning && debugController.State == DebugState.Paused;
    // F8 works from idle (start-and-step, needs a startup project) or any running/paused state.
    public bool CanStepIntoProject => externalDebugger?.IsActive == true ? CanContinueProject : CanStartDefaultProject || IsRunning;
    public bool CanStepOverProject => CanStepIntoProject;
    public bool CanStepOutProject => CanStepIntoProject;
    public bool CanRunToCursor => externalDebugger?.IsActive != true && (CanStartDefaultProject || IsRunning);
    public bool CanEndProject => IsRunning;
    public bool CanRestartProject => IsRunning && externalDebugger?.IsActive != true;

    // As FormComponentClass.InstantiateWindow builds a desktop one, the menu bar included (#1127).
    private VBMDIFormRuntime InstantiateWindow(ComponentInstance instance, bool menuBar)
    {
        var form = new VBMDIFormRuntime(windowManager)
        {
            Title = instance.GetPropertyOrDefault(VBProperties.CaptionProperty) ?? "",
            Width = instance.GetPropertyOrDefault(VBProperties.WidthProperty),
            Height = instance.GetPropertyOrDefault(VBProperties.HeightProperty) + (menuBar ? VBLoader.MenuBarHeight : 0),
            [AttachedProperties.BackColorProperty] = instance.GetPropertyOrDefault(VBProperties.BackColorProperty),
            [MDIHostPanel.WindowLocationProperty] = new Point((int)instance.GetPropertyOrDefault(VBProperties.LeftProperty), (int)instance.GetPropertyOrDefault(VBProperties.TopProperty))
        };
        VBProps.SetName(form, instance.GetPropertyOrDefault(VBProperties.NameProperty));
        return form;
    }
    
    private Task RunFormInBrowser(FormDefinition element, CancellationToken token, out VBMDIFormRuntime window,
        IDebugController? debugController = null)
    {
        // The same build, start and show as VBLoader.RunForm, in an MDI window (#822). Parsed before anything is shown, so
        // a syntax error fails the start without leaving a window behind that nothing would close. The program's other
        // forms get MDI windows of their own.
        var shown = window = InstantiateWindow(VBLoader.FormComponentOf(element), VBLoader.HasMenuBar(element));
        VBLoader.PrepareForm(element, shown.Context, content => shown.Content = content, debugController, OpenInBrowser);
        return VBLoader.StartAndShow(shown.Context, token, close: shown.CloseForm,
            showUntilClosed: () => shown.ShowInHost(blockBehind: true));
    }

    // A window for another of the program's forms, in the single-view host (#822).
    private IFormView OpenInBrowser(FormDefinition definition) =>
        InstantiateWindow(VBLoader.FormComponentOf(definition), VBLoader.HasMenuBar(definition));

    public class VBMDIFormRuntime : MDIWindow, IModuleExecutionRoot, IFormView
    {
        protected override Type StyleKeyOverride => typeof(MDIWindow);

        private VBWindowContext windowContext;
        private readonly IWindowManager windowManager;

        public VBWindowContext Context => windowContext;

        // The window's time in the host, from its first show until it closes.
        private Task? inHost;

        public VBMDIFormRuntime(IWindowManager windowManager)
        {
            this.windowManager = windowManager;
            windowContext = new VBWindowContext(new MDIStandaloneStandardLib(windowManager)) { Window = this };
            // Held by a modal form, the window takes no input of its own (#822), and a press on it brings the modal form
            // forward instead.
            foreach (var input in new RoutedEvent[] { KeyDownEvent, KeyUpEvent, TextInputEvent, PointerReleasedEvent, PointerWheelChangedEvent })
                AddHandler(input, new EventHandler<RoutedEventArgs>((_, e) => e.Handled |= heldBy is not null), RoutingStrategies.Tunnel);
            AddHandler(PointerPressedEvent, (_, e) =>
            {
                if (heldBy is null)
                    return;
                e.Handled = true;
                heldBy.ActivateForm();
            }, RoutingStrategies.Tunnel);
            // Report layout and setters through the same extent comparison (#994, #1570). A size setter reports
            // immediately, even during Load; the later layout finds nothing new.
            this.GetObservable(BoundsProperty).Subscribe(new ActionObserver<Rect>(_ =>
            {
                sizePending = false;
                windowContext.WindowResized();
            }));
            this.GetObservable(WidthProperty).Subscribe(new ActionObserver<double>(_ => SizeSet()));
            this.GetObservable(HeightProperty).Subscribe(new ActionObserver<double>(_ => SizeSet()));
            this.GetObservable(MDIHostPanel.WindowStateProperty).Subscribe(new ActionObserver<WindowState>(_ => windowContext.WindowResized()));
            this.GetObservable(IsVisibleProperty).Subscribe(new ActionObserver<bool>(visible =>
            {
                if (visible)
                    windowContext.Shown();
            }));
            // The host's active window is its topmost, which stays so while the IDE itself is inactive, as a form stays the
            // program's active one while another application is. A held form is never active (#994).
            this.GetObservable(MDIHostPanel.IsActiveProperty).Subscribe(new ActionObserver<bool>(active =>
            {
                if (active && heldBy is null)
                    windowContext.Program?.Activated(windowContext);
            }));
        }

        private bool sizePending;

        private void SizeSet()
        {
            sizePending = true;
            windowContext.WindowResized(sizeSet: true);
        }

        /// <inheritdoc/>
        public (Size Size, WindowState State) Extent
        {
            get
            {
                var state = MDIHostPanel.GetWindowState(this);
                if (state == WindowState.Minimized)
                    return (default, state);
                // Until the host has arranged a size assignment, use the requested size.
                var size = sizePending || Bounds.Size == default ? new Size(Width, Height) : Bounds.Size;
                var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
                return (new Size(Math.Round(size.Width * scaling), Math.Round(size.Height * scaling)), state);
            }
        }

        // Form_Load is not raised here: the program raises Initialize and Load before the window is shown (#822, #994).

        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null)
        {
            windowContext.ExecuteSub(name, args);
        }

        public bool Handles(string name) => windowContext.Handles(name);

        public void ExecuteEvent(string name, Func<IReadOnlyList<Vb6Value>> args, EventCoalescing coalescing = EventCoalescing.None) =>
            windowContext.ExecuteEvent(name, args, coalescing);

        public void ExecuteTimer(string name, IReadOnlyList<Vb6Value>? args = null) => windowContext.ExecuteTimer(name, args);

        public Task<bool> UnloadAsync(int mode) => windowContext.UnloadAsync(mode);

        /// <summary>
        /// Put the window in the host, or show it again there: the startup form blocking the IDE behind it, another form
        /// blocking nothing, so the program's forms stay usable beside one another (#822). Completes when it closes.
        /// </summary>
        public Task ShowInHost(bool blockBehind)
        {
            IsVisible = true;
            if (inHost is not null)
                return inHost;
            inHost = blockBehind ? windowManager.ShowManagedWindow(this) : windowManager.ShowManagedWindow(this, blockBehind: false);
            // The host's own close takes the window away. The title bar's close asks the program first, as a desktop form's
            // close box does, QueryUnload with UnloadMode 0 and then Unload (#973), and is refused while a modal form holds
            // this one (#822). The window goes once the form has unloaded, through the host's close.
            hostClose = CloseCommand;
            CloseCommand = new DelegateCommand(() =>
            {
                if (!windowContext.UnloadFromCloseBox())
                    hostClose?.Execute(null);
            }, () => true);
            return inHost;
        }

        // The host's close, which takes the window away without asking the program.
        private System.Windows.Input.ICommand? hostClose;

        public void SetContent(Control content) => Content = content;

        public void ShowForm() => ShowInHost(blockBehind: false);

        public void HideForm() => IsVisible = false;

        public void CloseForm() => (hostClose ?? CloseCommand)?.Execute(null);

        public bool IsShown => inHost is { IsCompleted: false } && IsVisible;

        private IFormView? heldBy;

        public IFormView? HeldBy
        {
            get => heldBy;
            set
            {
                heldBy = value;
                // Its controls stop taking hits, which disabling them would do too, greying each one (#822).
                if (Content is Avalonia.Input.InputElement content)
                    content.IsHitTestVisible = value is null;
            }
        }

        public void ActivateForm() => this.ActivateMDIForm();
    }

    public class MDIStandaloneStandardLib : IBasicStandardLibrary
    {
        private readonly IWindowManager windowManager;

        public MDIStandaloneStandardLib(IWindowManager windowManager)
        {
            this.windowManager = windowManager;
        }

        public async Task<MessageBoxResult> MsgBox(string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return await windowManager.MessageBox(text, caption, buttons, icon, isProgramDialog: true);
        }

        public async Task<string?> InputBox(string prompt, string? title, string defaultText)
        {
            return await windowManager.InputBox(prompt, title, defaultText, isProgramDialog: true);
        }

        // The Immediate window shows the text, not the typed values (#1380).
        public void DebugPrint(Vb6Value value) { }

        public void DebugWrite(string text) => VBDebugConsole.Write(text);

        public int DebugColumn => VBDebugConsole.Column;

        public ITextClipboard Clipboard => new AvaloniaTextClipboard(() =>
            (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
                ?.MainWindow?.Clipboard);

        public IVbSettingsStore Settings => VbSettingsStores.ForThisUser();

        private HexIDE.Runtime.AvaloniaInterop.AvaloniaDisplay? display;
        public IVbDisplay Display => display ??= new HexIDE.Runtime.AvaloniaInterop.AvaloniaDisplay(() =>
            (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
                ?.MainWindow);

        /// <summary>
        /// A CommonDialog's Open or Save As (#1084), through the IDE's own pickers, so an automation client answers one
        /// with answer_next_file_dialog as it answers the IDE's.
        /// </summary>
        public Task<VbFileDialogAnswer?> ShowFileDialog(VbFileDialogRequest request) => ShowFileDialog(windowManager, request);

        public Task<bool> ShowPrintDialog() => windowManager.ShowPrintDialog();

        public Task<int?> ShowColorDialog(int initial) => windowManager.ShowColorDialog(initial);

        public Task<VbFontChoice?> ShowFontDialog(VbFontChoice initial) => ShowFontDialog(windowManager, initial);

        internal static async Task<VbFileDialogAnswer?> ShowFileDialog(IWindowManager windowManager, VbFileDialogRequest request)
        {
#if DEBUG
            // An armed answer stands for a name typed with the filter left as the program set it.
            int? scriptedIndex = HexIDE.IDE.ScriptedFileDialogs.Pending > 0 && request.FilterIndex >= 1 ? request.FilterIndex : null;
#else
            int? scriptedIndex = null;
#endif
            var start = Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime { MainWindow: { } main }
                        && TopLevel.GetTopLevel(main)?.StorageProvider is { } provider
                ? await VbFileDialogOptions.StartFolder(provider, request)
                : null;
            if (request.Save)
            {
                var path = await windowManager.SaveFilePickerAsync(VbFileDialogOptions.Save(request, start));
                return path is null ? null : new VbFileDialogAnswer(path, scriptedIndex);
            }
            var paths = await windowManager.OpenFilePickerAsync(VbFileDialogOptions.Open(request, start));
            return paths is { Count: > 0 }
                ? new VbFileDialogAnswer(paths[0], scriptedIndex, paths.Skip(1).ToArray())
                : null;
        }

        /// <summary>A CommonDialog's ShowFont (#1084), through the IDE's font dialog.</summary>
        internal static async Task<VbFontChoice?> ShowFontDialog(IWindowManager windowManager, VbFontChoice initial)
        {
            var start = initial.Name.Length == 0
                ? null
                : new FontDialogResult(new Avalonia.Media.FontFamily(initial.Name),
                    initial.Italic ? Avalonia.Media.FontStyle.Italic : Avalonia.Media.FontStyle.Normal,
                    initial.Bold ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal, initial.Size);
            var chosen = await windowManager.ShowFontDialog(start);
            return chosen is null
                ? null
                : new VbFontChoice(chosen.Family.Name, (float)chosen.Size, chosen.Weight >= Avalonia.Media.FontWeight.Bold,
                    chosen.Style != Avalonia.Media.FontStyle.Normal);
        }
    }
}
