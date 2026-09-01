using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Labs.Input;
using Avalonia.Platform;
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

    // A Run-To-Cursor target requested while IDLE — applied AFTER the run-start Reset (which clears the controller's
    // target), so the fresh run breaks at it. Consumed once in RunProject.
    private (string Module, int Line)? _pendingRunTo;

    [Notify]
    [AlsoNotify(nameof(IsRunning), nameof(CanStartDefaultProject), nameof(CanStartDefaultProjectWithFullCompile), nameof(CanBreakProject), nameof(CanContinueProject), nameof(CanStepIntoProject), nameof(CanStepOverProject), nameof(CanStepOutProject), nameof(CanRunToCursor), nameof(CanEndProject), nameof(CanRestartProject))]
    private System.IDisposable? runningProject;

    public bool IsRunning => runningProject != null;

    public ProjectRunnerService(IEventBus eventBus,
        IWindowManager windowManager,
        IProjectManager projectManager,
        ILocalizationService localization,
        IDebugController debugController,
        IBreakpointService breakpointService,
        WatchService watchService)
    {
        this.eventBus = eventBus;
        this.windowManager = windowManager;
        this.projectManager = projectManager;
        this.localization = localization;
        this.debugController = debugController;
        this.breakpointService = breakpointService;
        this.watchService = watchService;

        // Break/Continue toggle the debug state without touching `runningProject`, so re-query the run commands
        // when the controller pauses/resumes (otherwise the Break/Continue buttons stay stale).
        debugController.Stopped += _ => CommandManager.InvalidateRequerySuggested();
        debugController.Continued += CommandManager.InvalidateRequerySuggested;

        // A breakpoint toggled (in the gutter or via MCP) while a run is live takes effect immediately, matching
        // VB6 — push just that document's set to the controller. Launch-time application is ApplyBreakpoints().
        // (Persistence/load is UserSidecarService's job, on project open/save.)
        breakpointService.BreakpointsChanged += uri =>
        {
            if (IsRunning)
                debugController.SetBreakpoints(ModuleNameFromUri(uri), breakpointService.GetBreakpoints(uri));
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
            PushBreakpoints(form.Name, $"vb6://form/{form.Name}");
        foreach (var module in project.Modules)
            PushBreakpoints(module.Name, $"vb6://module/{module.Name}");

        void PushBreakpoints(string moduleName, string uri)
        {
            var lines = breakpointService.GetBreakpoints(uri);
            if (lines.Count > 0)
                debugController.SetBreakpoints(moduleName, lines);
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

    // vb6://form/Form1 → "Form1"; vb6://module/Module1 → "Module1" — the bare name the debug gate reports.
    private static string ModuleNameFromUri(string uri)
    {
        var slash = uri.LastIndexOf('/');
        return slash >= 0 ? uri[(slash + 1)..] : uri;
    }

    public void RunProject(ProjectDefinition projectDefinition, bool stepInto = false)
    {
        eventBus.Publish(new ApplyAllUnsavedChangesEvent());

        if (projectDefinition.StartupForm is { } form)
        {
            async Task WindowTask()
            {
                var tokenSource = new CancellationTokenSource();

                var syntaxChecker = new SyntaxChecker();
                try
                {
                    syntaxChecker.Run(form.Code);
                }
                catch (VBCompileErrorException error)
                {
                    await windowManager.MessageBox(error.Message, icon: MessageBoxIcon.Warning);
                    throw new OperationCanceledException();
                }

                // Fresh debug session: clear any prior break/abort state, then apply the current breakpoints so the
                // very first statement can already be a break.
                debugController.Reset();
                ApplyBreakpoints(projectDefinition);
                ApplyWatchBreaks();   // push Break-When-True/Changed watches so the run can break on them from line 1
                if (_pendingRunTo is { } rt)   // an idle Run-To-Cursor: arm the target now that Reset has cleared it
                {
                    debugController.RunToCursor(rt.Module, rt.Line);
                    _pendingRunTo = null;
                }
                // F8-from-idle: arm step BEFORE the first statement runs, so the run breaks at line 1 (VB6
                // start-and-step). Arming while Running (fresh Reset) simply sets the flag; the first gate breaks.
                if (stepInto)
                    debugController.StepInto();

                Task task;
                if (Static.SingleView)
                {
                    task = RunFormInBrowser(form, tokenSource.Token, out _, debugController);
                }
                else
                {
                    task = VBLoader.RunForm(form, tokenSource.Token, out var window, debugController);
                    window.Show();
                }

                RunningProject = new ActionDisposable(() => tokenSource.Cancel());
                try
                {
                    await task;
                }
                finally
                {
                    // The run ended by ANY route — ran to completion, the user closed the running form, or End was
                    // pressed. Tear the debug session down so a walk paused at a breakpoint is unwound and the
                    // controller (and the editor's current-statement bar) return to a clean state. Idempotent with
                    // EndProject's own Stop(); on the plain window-close path this is the ONLY teardown.
                    debugController.Stop();
                    RunningProject = null;
                }
            }
            WindowTask().ListenErrors();
        }
        else
        {
            windowManager.MessageBox(localization.GetString("Str.ProjectRunner.MustHaveStartupForm"), icon: MessageBoxIcon.Error);
        }
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

    public void RunToCursorProject(string module, int line)
    {
        // Run To Cursor (Ctrl+F8): a one-shot break at (module, line). Paused → arm + Continue; running → arm (breaks
        // when reached); idle → start the project and arm the target after the run-start sequence (a plain run, NOT
        // start-and-step — it runs freely to the cursor line).
        if (!IsRunning)
        {
            _pendingRunTo = (module, line);
            RunStartupProject();
        }
        else if (debugController.State == DebugState.Paused)
        {
            debugController.RunToCursor(module, line);
            debugController.Continue();
        }
        else
        {
            debugController.RunToCursor(module, line);
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
        RunningProject?.Dispose();
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
        var form = element.Components.FirstOrDefault(x => x.BaseClass == FormComponentClass.Instance);
        if (form == null)
            throw new Exception("No form found");

        window = InstantiateWindow(form);
        var formName = form.GetPropertyOrDefault(VBProperties.NameProperty)?.ToString();
        if (formName is not null)
        {
            window.Context.ExecutionContext.AllocVariable(window.Context.RootEnv, formName, new Vb6Value(window));
            window.Context.ExecutionContext.AllocVariable(window.Context.RootEnv, "Me", new Vb6Value(window));
        }

        var task = windowManager.ShowManagedWindow(window);
        window.Content = VBLoader.SpawnComponents(element, window.Context.ExecutionContext, window.Context.RootEnv);

        window.Context.SetCode(code: element.Code, moduleName: formName ?? "Module1", debugController: debugController);
        token.Register((state, _) =>
        {
            (state as MDIWindow)!.CloseCommand.Execute(null);
        }, window);

        return task;
    }

    public class VBMDIFormRuntime : MDIWindow, IModuleExecutionRoot
    {
        protected override Type StyleKeyOverride => typeof(MDIWindow);

        private VBWindowContext windowContext;

        public VBWindowContext Context => windowContext;

        private bool first;

        public VBMDIFormRuntime(IWindowManager windowManager)
        {
            windowContext = new VBWindowContext(new MDIStandaloneStandardLib(windowManager));
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

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
            windowContext.ExecuteSub("Form_Load");
        }

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