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

    // A Run-To-Cursor target requested while IDLE — applied AFTER the run-start Reset (which clears the controller's
    // target), so the fresh run breaks at it. Consumed once in RunProject.
    private (DocumentIdentity Document, int Line)? _pendingRunTo;

    [Notify]
    [AlsoNotify(nameof(IsRunning), nameof(CanStartDefaultProject), nameof(CanStartDefaultProjectWithFullCompile), nameof(CanBreakProject), nameof(CanContinueProject), nameof(CanStepIntoProject), nameof(CanStepOverProject), nameof(CanStepOutProject), nameof(CanRunToCursor), nameof(CanEndProject), nameof(CanRestartProject))]
    private System.IDisposable? runHandle;

    public bool IsRunning => runHandle != null;

    /// <inheritdoc />
    public event Action<string>? StartFailed;

    /// <inheritdoc />
    public event Action<string>? RunFailed;

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

    public ProjectRunnerService(IEventBus eventBus,
        IWindowManager windowManager,
        IProjectManager projectManager,
        ILocalizationService localization,
        IDebugController debugController,
        IBreakpointService breakpointService,
        WatchService watchService,
        RunScope runScope)
    {
        this.eventBus = eventBus;
        this.windowManager = windowManager;
        this.projectManager = projectManager;
        this.localization = localization;
        this.debugController = debugController;
        this.breakpointService = breakpointService;
        this.watchService = watchService;
        this.runScope = runScope;

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
                void OnFaulted(VBWindowContext _, Exception e)
                {
                    if (fault is not null)
                        return;   // the first fault ends the run; any after it come from the same collapse
                    fault = e;
                    // Abort first, as EndProject does. A fault is caught where its own event procedure was
                    // dispatched, so a procedure that raised that event (a Click that set Text1.Text, firing
                    // Text1_Change) would otherwise carry on past the statement, and so would any other walk
                    // still running. Stopping the controller ends each of them at its next statement. That covers
                    // the raiser only while it is still waiting on the event: if Text1_Change yielded its time
                    // slice before faulting, the setter had already returned and the click had run on (#766).
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
                void OnEndReached(BasicInterpreter _) => Dispatcher.UIThread.Post(() => tokenSource.Cancel());

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
                        StartFailed?.Invoke(DescribeFailedStart(e, form.Name));
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
                    RunFailed?.Invoke(RunErrorText.ForFault(fault, localization));
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
    /// Run a project whose startup object is <c>Sub Main</c> — a code-only Standard EXE, which has no
    /// window to open and no form whose code could serve as the entry point.
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
    /// The run has no window, so unlike the form path there is nothing whose closing ends it: it finishes
    /// when <c>Main</c> returns. `Debug.Print` reaches the Immediate window through the same standard
    /// library the MDI runtime uses.
    /// </para>
    /// </remarks>
    private void RunSubMain(ProjectDefinition projectDefinition, bool stepInto,
        (DocumentIdentity Document, int Line)? runTo)
    {
        // Shared with the form paths, so the same project sees the same modules whichever startup object it
        // has. Sub Main previously answered this question here and the form paths not at all (#220).
        var (modules, classModules) = VBLoader.InterpreterModules(projectDefinition);

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
            try
            {
                BasicInterpreter interpreter;
                try
                {
                    // Building the interpreter parses every module and runs the pre-pass over it, which throws for a
                    // syntax error, naming its module, and for a declaration it cannot take. It used to sit outside this try, after Reset() and the claim
                    // above, so a throw here left the debugger Running and the project claimed with nothing
                    // running, as #590 did on the form path. It is a start that never became a run, and is
                    // reported the way that path reports one.
                    var context = new VBWindowContext(new MDIStandaloneStandardLib(windowManager));
                    interpreter = new BasicInterpreter(
                        new MDIStandaloneStandardLib(windowManager), context.ExecutionContext, context.RootEnv,
                        modules[0].Code, modules[0].Name,
                        modules.Count > 1 ? modules.Skip(1).ToList() : null,
                        classModules)
                    {
                        DebugController = debugController,
                    };
                    interpreter.SetAppInfo(AppInfo.FromProject(projectDefinition));
                    // A missing or ambiguous Main is found here, before anything runs, so it too is a start that
                    // never became a run. It used to be raised as the run began, and arrived as a run that ended,
                    // which run_project answered as a success (#759).
                    interpreter.FindStartupSubMain();
                }
                catch (Exception e)
                {
                    debugController.Stop();
                    RunningProject = null;
                    StartFailed?.Invoke(DescribeFailedStart(e, formName: null));
                    return;
                }

                RunHandle = new ActionDisposable(() => debugController.Stop());
                // End in Sub Main unwinds it, and RunStartupSubMain takes that as a clean stop (#775). Sub Main cannot
                // show a form yet, so no other interpreter can reach End during this run; when it can, this run will
                // need to listen for EndReached as the form run does.
                await interpreter.RunStartupSubMain();
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
            }
            finally
            {
                debugController.Stop();
                RunHandle = null;
                RunningProject = null;
            }

            if (failure is not null)
                RunFailed?.Invoke(failure);
        }

        MainTask().ListenErrors();
    }

    public void RunStartupProject(bool stepInto = false)
    {
        if (projectManager.StartupProject is {} startupProject)
        {
            RunProject(startupProject, stepInto);
        }
    }

    public void BreakCurrentProject()
    {
        // Request a break: the next statement gate suspends (a no-op if the program is idle-between-events).
        debugController.Pause();
    }

    public void StepIntoProject()
    {
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
        // Resume from a break (F5 in break mode).
        debugController.Continue();
    }

    public void EndProject()
    {
        // Abort first so a paused interpreter unwinds via StopExecutionSignal, then cancel the token (closes the
        // window). Order matters: Stop() releases the break gate that Dispose()'s cancel alone cannot.
        debugController.Stop();
        RunHandle?.Dispose();
        RunHandle = null;
        RunningProject = null;
    }

    public void RestartProject()
    {
        EndProject();
        RunStartupProject();
    }

    public bool CanStartDefaultProject => !IsRunning && projectManager.StartupProject != null;
    public bool CanStartDefaultProjectWithFullCompile => CanStartDefaultProject;
    public bool CanBreakProject => IsRunning && debugController.State == DebugState.Running;
    public bool CanContinueProject => IsRunning && debugController.State == DebugState.Paused;
    // F8 works from idle (start-and-step, needs a startup project) or any running/paused state.
    public bool CanStepIntoProject => CanStartDefaultProject || IsRunning;
    public bool CanStepOverProject => CanStartDefaultProject || IsRunning;
    public bool CanStepOutProject => CanStartDefaultProject || IsRunning;
    public bool CanRunToCursor => CanStartDefaultProject || IsRunning;
    public bool CanEndProject => IsRunning;
    public bool CanRestartProject => IsRunning;

    private VBMDIFormRuntime InstantiateWindow(ComponentInstance instance)
    {
        var form = new VBMDIFormRuntime(windowManager)
        {
            Title = instance.GetPropertyOrDefault(VBProperties.CaptionProperty) ?? "",
            Width = instance.GetPropertyOrDefault(VBProperties.WidthProperty),
            Height = instance.GetPropertyOrDefault(VBProperties.HeightProperty),
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
        // a syntax error fails the start without leaving a window behind that nothing would close.
        var shown = window = InstantiateWindow(VBLoader.FormComponentOf(element));
        VBLoader.PrepareForm(element, shown.Context, content => shown.Content = content, debugController);
        return VBLoader.StartAndShow(shown.Context, token, close: () => shown.CloseCommand.Execute(null),
            showUntilClosed: () => windowManager.ShowManagedWindow(shown));
    }

    public class VBMDIFormRuntime : MDIWindow, IModuleExecutionRoot
    {
        protected override Type StyleKeyOverride => typeof(MDIWindow);

        private VBWindowContext windowContext;

        public VBWindowContext Context => windowContext;

        private bool first;

        public VBMDIFormRuntime(IWindowManager windowManager)
        {
            windowContext = new VBWindowContext(new MDIStandaloneStandardLib(windowManager)) { Window = this };
            this.GetObservable(BoundsProperty)
                .Subscribe(new ActionObserver<Rect>(_ =>
                {
                    if (first)
                    {
                        first = false;
                        return;
                    }
                    windowContext.ExecuteSub("Form_Resize");
                }));
        }

        // Form_Load is not raised here: the program raises Initialize and Load before the window is shown (#822, #994).

        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null)
        {
            windowContext.ExecuteSub(name, args);
        }
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
            return await windowManager.MessageBox(text, caption, buttons, icon);
        }

        public async Task<string?> InputBox(string prompt, string? title, string defaultText)
        {
            return await windowManager.InputBox(prompt, title, defaultText);
        }

        public void DebugPrint(Vb6Value value) => VBDebugConsole.Emit(value);
    }
}