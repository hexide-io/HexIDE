using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Utils;
using Serilog;

namespace HexIDE.Runtime;

/// <summary>
/// A running form's handle on its program: the interpreter built from the project's modules, and the instance of the
/// form module the window shows (#822). The form is an object: its declarations and controls are the instance's, and
/// <c>Me</c> in its code is that instance, as it is for a class.
/// </summary>
public class VBWindowContext : IModuleExecutionRoot
{
    private readonly IBasicStandardLibrary standardLibrary;
    private BasicInterpreter? interpreter;

    /// <summary>Closes every file the program has open: its end, however it ends (#1145).</summary>
    internal void CloseFiles() => interpreter?.BuiltIns.Files.CloseAll();
    private VbObject? instance;
    private bool formDeclarationsRun;
    // Set once StartAsync begins. A started form's declarations run there and nowhere else, so an event can never run
    // them late, after Load has used the variables they would re-create (#1010 review).
    private bool started;
    private bool controlsStarted;
    private bool controlsShown;

    public ModuleExecutionContext ExecutionContext { get; private set; } = new();

    /// <summary>
    /// Runs this program's handlers one at a time, as VB6's single thread does (#766). One per run: every form of a
    /// program shares its program's (#822).
    /// </summary>
    public EventPump Events { get; private set; } = new();

    /// <summary>The program this form is one of, or null for a context that built its own interpreter (<see cref="SetCode"/>).</summary>
    public VBProgram? Program { get; private set; }

    // The form module this context's instance is made from: the program's primary module unless attached to another.
    private ModuleInfo? template;

    /// <summary>The library the program's built-ins reach the host through, from the startup form's window.</summary>
    internal IBasicStandardLibrary StandardLibrary => standardLibrary;

    public string Code { get; private set; } = "";

    /// <summary>The form module's own env: the template its instance is cloned from.</summary>
    public ExecutionEnvironment RootEnv { get; } = new();

    /// <summary>
    /// The window the form runs in, whose built-in members (Caption, Width, Hide and the rest) the form instance answers
    /// for after its own. Null for a context with no window, as a test's is.
    /// </summary>
    public Avalonia.Controls.Control? Window { get; init; }

    public static event Action<VBWindowContext, VBRunTimeException>? RunTimeError;

    public static event Action<VBWindowContext, VBCompileErrorException>? CompileError;

    /// <summary>
    /// An event procedure stopped on an exception that is not a VB6 error: a construct HexIDE does not
    /// implement (<see cref="NotImplementedException"/>), or a fault in the engine itself. Neither is
    /// trappable, so <c>On Error</c> never sees it and the procedure stops at that statement.
    /// </summary>
    /// <remarks>
    /// This used to reach only the log. The handler stopped part way and the program carried on, so
    /// nothing told the user that a statement had not run (#754). Whoever owns the run decides what a fault
    /// means for it. With nobody listening, it is logged and nothing else.
    /// </remarks>
    public static event Action<VBWindowContext, Exception>? Faulted;

    public VBWindowContext(IBasicStandardLibrary standardLibrary)
    {
        this.standardLibrary = standardLibrary;
    }

    /// <summary>
    /// Compile the form's code, and the project's other modules, into a fresh interpreter. The form is the primary
    /// module, of kind <see cref="InterpreterModuleKind.Form"/>: a template, whose running instance this context makes
    /// on first use (#822). <paramref name="moduleName"/> is the form's name: the name the debug pause-gate reports and
    /// breakpoints are keyed by, and the name the instance is reached by from any module.
    /// <paramref name="debugController"/> is the per-session controller (null ⇒ no debugging, zero gate overhead).
    /// </summary>
    /// <param name="additionalModules">
    /// The project's other standard modules. Omitting these is not a missing nicety: without them a form
    /// cannot call <c>Module1.DoThing</c>, cannot read a <c>Public Const</c> declared in a <c>.bas</c>, and
    /// cannot reach a <c>Public</c> variable there — which is most non-trivial VB6, the moment F5 is
    /// pressed (hexide-io/HexIDE#220).
    /// </param>
    /// <param name="classModules">
    /// The project's class modules, so <c>New SomeClass</c> resolves from a form.
    /// </param>
    public void SetCode(string code, string moduleName = "Module1", Debugging.IDebugController? debugController = null,
        Interpreter.AppInfo? appInfo = null,
        IReadOnlyList<(string Name, string Code)>? additionalModules = null,
        IReadOnlyList<(string Name, string Code)>? classModules = null)
    {
        Code = code;
        interpreter = new BasicInterpreter(standardLibrary, ExecutionContext, RootEnv, code, moduleName,
            additionalModules, classModules, InterpreterModuleKind.Form)
        {
            DebugController = debugController,
            Events = Events,
        };
        instance = null;
        formDeclarationsRun = false;
        // What `App` reports. Null means no project behind this run (a bare context), and App then reports
        // empty rather than inventing an identity.
        if (appInfo is not null)
            interpreter.SetAppInfo(appInfo);
    }

    /// <summary>
    /// The interpreter this context built, so a test can assert what it was handed. Internal on purpose:
    /// which modules reach the interpreter is exactly what #220 got wrong, and it was observable from no
    /// public member — the failure showed up only as a form unable to call its own project's code.
    /// </summary>
    internal BasicInterpreter? Interpreter => interpreter;

    /// <summary>Whether this context belongs to the named run, for hosts listening to static runtime events.</summary>
    public bool IsInDebugSession(Debugging.IDebugController controller, int session)
        => interpreter?.IsInDebugSession(controller, session) == true;

    /// <summary>The running form, an instance of the form module (#822), or null before <see cref="SetCode"/>.</summary>
    public VbObject? FormInstance => interpreter is null ? null : EnsureInstance();

    /// <summary>Where the form's controls go: its instance's own env, as a class instance's fields are in its own.</summary>
    public ExecutionEnvironment InstanceEnv => EnsureInstance().InstanceEnv;

    private VbObject EnsureInstance()
    {
        if (interpreter is null)
            throw new InvalidOperationException("The form has no code yet: SetCode comes first.");
        if (instance is null)
        {
            instance = interpreter.NewFormInstance(template ?? interpreter.PrimaryModule);
            instance.FormWindow = Window;
            // The context's own hold, apart from the one the form's name has: in VB6 a loaded form is kept alive by
            // Forms, so `Set Form1 = Nothing` from a module clears the name and leaves the running form be (#1010
            // review). Without it, that Set terminated the instance under its own window. In a program, the program
            // holds a form while it's loaded instead (#822).
            if (Program is null)
                interpreter.AddRef(Vb6Value.NewObject(instance));
        }
        return instance;
    }

    /// <summary>
    /// Join <paramref name="program"/> as one of its forms (#822): its interpreter, its state and its pump, with an
    /// instance of <paramref name="form"/> made on first use, or <paramref name="existing"/>, an unloaded instance
    /// loading again in this window. <paramref name="code"/> is the text the program parsed for the form, its whole
    /// file (#273), which <see cref="Code"/> then reports as <see cref="SetCode"/>'s path does.
    /// </summary>
    internal void Attach(VBProgram program, ModuleInfo form, string code, VbObject? existing = null)
    {
        Program = program;
        Code = code;
        interpreter = program.Interpreter;
        ExecutionContext = program.ExecutionContext;
        Events = program.Events;
        template = form;
        instance = existing;
        // An instance loading again has had its declarations and its Initialize: VB6 raises Load alone (measured).
        formDeclarationsRun = existing is not null;
        started = existing is not null;
        if (existing is not null)
            existing.FormWindow = Window;
    }

    internal void AttachControl(VBProgram program, ModuleInfo module)
    {
        Attach(program, module, "", new VbObject(module, module.ModuleEnv.Clone()) { FormLoaded = true });
        formDeclarationsRun = false;
        started = false;
        program.Interpreter.AddRef(Vb6Value.NewObject(instance!));
    }

    /// <summary>
    /// Make the instance's start before its load (#822): the modules' declarations, the form's own, then
    /// <c>Form_Initialize</c>, inside the walk that first used the form's name, as VB6 runs it (measured). An error in
    /// Initialize is the walk's, for its <c>On Error</c> to trap (measured), so it isn't caught here.
    /// </summary>
    internal async Task InitializeAsync()
    {
        var form = EnsureInstance();
        started = true;
        await EnsureDeclarations();
        await interpreter!.RaiseFormEvent(form, "Form_Initialize");
    }

    /// <summary>
    /// Load the form (#822): its controls usable, the program holding it, then <c>Form_Load</c>, inside the walk that asked,
    /// as <c>Load</c>, <c>Show</c> or the first use of a control does in VB6 (measured). The window stays hidden.
    /// </summary>
    internal async Task LoadAsync()
    {
        var form = EnsureInstance();
        if (form.FormLoaded)
            return;
        form.FormLoaded = true;
        form.FormUnloaded = false;
        ExecutionContext.State.ClearPendingControls(form);
        Program?.Loaded(this);
        await StartControls();
        await Report(() => interpreter!.RaiseFormEvent(form, "Form_Load"));
    }

    internal async Task ControlEvent(string name, IReadOnlyList<Vb6Value>? args = null)
    {
        await EnsureDeclarations();
        await interpreter!.RaiseFormEvent(EnsureInstance(), name, args);
    }

    // The event is measured; PropertyBag operations are not part of #1631 and remain explicitly refused.
    private sealed class UnimplementedPropertyBag : ICSharpProxy, ICSharpPropertyBag
    {
        public void Call(string method, List<Vb6Value> args) => throw new NotImplementedException($"PropertyBag.{method}");
        public bool TryGetProperty(string name, out Vb6Value value) => throw new NotImplementedException($"PropertyBag.{name}");
        public bool TrySetProperty(string name, Vb6Value value) => throw new NotImplementedException($"PropertyBag.{name}");
    }

    private IEnumerable<VBUserControl> UserControls => EnsureInstance().Controls?.All.OfType<VBUserControl>()
        .OrderBy(AvaloniaInterop.UserControlMembers.Windowless) ?? Enumerable.Empty<VBUserControl>();

    private async Task StartControls()
    {
        if (controlsStarted)
            return;
        controlsStarted = true;
        foreach (var control in UserControls)
        {
            if (control.Context is not { } context)
                continue;
            await context.ControlEvent("UserControl_Initialize");
            await context.ControlEvent("UserControl_ReadProperties", [new Vb6Value(new UnimplementedPropertyBag())]);
            if (!AvaloniaInterop.UserControlMembers.Invisible(control))
                await context.ControlEvent("UserControl_Resize");
        }
    }

    internal void ShowControls()
    {
        if (controlsShown)
            return;
        controlsShown = true;
        foreach (var control in UserControls)
            if (!AvaloniaInterop.UserControlMembers.Invisible(control))
                control.ExecuteSub("UserControl_Show");
    }

    private async Task StopControls()
    {
        foreach (var control in UserControls)
        {
            if (control.Context is not { } context)
                continue;
            if (!AvaloniaInterop.UserControlMembers.Invisible(control))
                await context.ControlEvent("UserControl_Hide");
            await context.ControlEvent("UserControl_Terminate");
        }
        controlsStarted = false;
        controlsShown = false;
    }

    // Whether the window has been shown since the form loaded, and its extent when Resize was last raised (#994).
    private bool shownSinceLoad;
    private (Avalonia.Size Size, Avalonia.Controls.WindowState State) resizedAt;

    /// <summary>
    /// The window has become visible (#994). The first time since the form loaded, that is Resize, raised inside the
    /// statement that showed it, as VB6 raises it before Show returns. Measured on vb6.exe in the console, native and p-code:
    /// a startup form raises Initialize, Load, one Resize, then Activate; <c>Load</c> alone raises no Resize; and a form shown
    /// again after <c>Hide</c> raises none, while one loaded again after <c>Unload</c> does.
    /// </summary>
    public void Shown()
    {
        if (shownSinceLoad || instance is not { FormLoaded: true } || Window is not IFormView view)
            return;
        ShowControls();
        shownSinceLoad = true;
        resizedAt = view.Extent;
        ExecuteSub("Form_Resize");
    }

    /// <summary>
    /// The window's size or state may have changed (#994): Resize, once the window has been shown since the form loaded,
    /// when its client size or state is not what it was when Resize was last raised. The window system reports a size more
    /// often than it changes one: showing a window reported it two or three times, each a Resize, where VB6 raises one.
    /// A size setter also reports while hidden (#1570), inside the assignment; Show still raises its own Resize.
    /// And a Move to the size the form has raises none in VB6 (measured).
    /// </summary>
    public void WindowResized(bool sizeSet = false, bool paintGrowth = false)
    {
        if ((!shownSinceLoad && !sizeSet) || instance is not { FormLoaded: true } || Window is not IFormView view)
            return;
        var now = view.Extent;
        if (now == resizedAt)
            return;
        // Mixed growth and shrinkage was not measured; keep its existing lack of Paint (#2389).
        var grew = now.Size.Width >= resizedAt.Size.Width && now.Size.Height >= resizedAt.Size.Height
            && (now.Size.Width > resizedAt.Size.Width || now.Size.Height > resizedAt.Size.Height);
        resizedAt = now;
        ExecuteSub("Form_Resize");
        if (paintGrowth && grew)
            ExecuteSub("Form_Paint");
    }

    /// <summary>Run the form's Terminate, as the program does at its end for a form whose instance it still holds.</summary>
    internal async Task TerminateAsync() => await Report(() => interpreter!.FireTerminate(EnsureInstance()));

    // Set once the window has been closed, so the program's end and an unload never close it twice.
    private bool windowClosed;

    /// <summary>Close the window once, however many times the program asks.</summary>
    internal void CloseOnce()
    {
        if (windowClosed)
            return;
        windowClosed = true;
        CloseWindow?.Invoke();
    }

    /// <summary>Whether the form unloaded itself during its start, as <c>Unload Me</c> in Form_Load does.</summary>
    internal bool UnloadedDuringStart => instance is { FormUnloaded: true };

    /// <summary>
    /// Hold the form's controls until it loads. VB6 loads a form at the first use of one of its controls, even inside
    /// <c>Form_Initialize</c> (measured, #822), and so does HexIDE: a read of a held control asks for the load
    /// (<see cref="FormLoadNeeded"/>). Called once the controls are in the instance's env.
    /// </summary>
    public void HoldControlsUntilLoad() => HoldControls(EnsureInstance());

    private void HoldControls(VbObject form)
    {
        foreach (var location in form.InstanceEnv.variableToLocation.Values)
            if (ExecutionContext.State[location].Value is Avalonia.Controls.Control or ControlArrayGroup)
                ExecutionContext.State.MarkPendingControl(location, form);
    }

    /// <summary>How the host closes the form's window. Set as the form is started.</summary>
    public Action? CloseWindow { get; set; }

    // Set while QueryUnload and Unload run. An Unload from inside them does nothing, as in VB6 (measured).
    private bool unloading;

    // Set once the form has unloaded and until its Terminate has run, which waits for the handler that unloaded it.
    private bool terminatePending;

    /// <summary>
    /// Unload the form, as VB6 does (#973): <c>Form_QueryUnload</c>, then <c>Form_Unload</c>, each with a ByRef Cancel
    /// that keeps the form loaded. They run inside the statement that unloads, as a call does, and the statements after
    /// <c>Unload Me</c> run once they have. The window goes at once. Terminate, and the run's end with its last form, come
    /// once the handler that unloaded it has returned (measured, native and p-code).
    /// </summary>
    /// <remarks>
    /// An Unload of a form already unloaded, or from inside its own QueryUnload or Unload, does nothing: no event, no
    /// error, and no reload (measured). Inside the handlers the outer unload goes on.
    /// </remarks>
    /// <param name="mode">VB6's UnloadMode: 1 (vbFormCode) from <c>Unload</c>, 0 (vbFormControlMenu) from the close box.</param>
    /// <returns>Whether the form is now unloaded: false when a handler cancelled, or while the unload is still deciding.</returns>
    public async Task<bool> UnloadAsync(int mode)
    {
        if (interpreter is null)
            return true;
        var form = EnsureInstance();
        if (unloading || form.FormUnloaded)
            return form.FormUnloaded;
        // A form that isn't loaded unloads as nothing: one whose name was only read, or one still in its own Initialize.
        // No event, no error, and its instance stays (measured, #822).
        if (!form.FormLoaded)
            return true;
        unloading = true;
        try
        {
            if (await Cancelled(form, "Form_QueryUnload", mode) || await Cancelled(form, "Form_Unload", null))
                return false;
        }
        finally
        {
            unloading = false;
        }
        form.FormLoaded = false;
        await StopControls();
        form.FormUnloaded = true;
        HoldControls(form);
        if (Window is not null)
            Window.IsVisible = false;
        terminatePending = true;
        if (Program is { } program)
            await program.Unloaded(this);
        Events.WhenIdle(() => FinishUnloadAsync(CloseWindow));
        return true;
    }

    /// <summary>
    /// The last of an unload, once the handler that unloaded the form has returned: <c>Form_Terminate</c>, the form's
    /// fields released, then <paramref name="close"/> for its window. Runs once, and does nothing when no unload waits.
    /// </summary>
    public async Task FinishUnloadAsync(Action? close)
    {
        if (!terminatePending)
            return;
        terminatePending = false;
        // In a program the form's Terminate waits for its instance's release, or for the program's end, not its unload
        // (measured, #822). The program ends first if this was its last form, running the Terminates and closing every
        // window: a host whose run ends with the window would otherwise end it before them. Else the window goes now.
        if (Program is { } program)
        {
            await program.Gone();
            if (close is not null && !windowClosed)
            {
                windowClosed = true;
                close();
            }
            return;
        }
        await Report(() => interpreter!.FireTerminate(EnsureInstance()));
        close?.Invoke();
    }

    // Raise QueryUnload or Unload with Cancel passed by reference, and say whether the handler set it.
    private async Task<bool> Cancelled(VbObject form, string name, int? mode)
    {
        var state = ExecutionContext.State;
        var cancel = state.Alloc(new Vb6Value(0));
        var args = new List<CallArg> { new(state[cancel], cancel) };
        if (mode is { } unloadMode)
            args.Add(new CallArg(new Vb6Value(unloadMode), null));
        await interpreter!.RaiseFormEvent(form, name, args);
        return state[cancel].Value is { } set && Convert.ToDouble(set, System.Globalization.CultureInfo.InvariantCulture) != 0;
    }

    /// <summary>
    /// The window's close box, pressed: true when the program now decides, through QueryUnload with UnloadMode 0 and then
    /// Unload, raised as any event is, so the window must stay open until it has, or when a modal form holds this one and the
    /// press is refused. False when there is no program to ask.
    /// </summary>
    public bool UnloadFromCloseBox()
    {
        if (interpreter is null || RunIsOver() || instance is null)
            return false;
        // Held by a modal form, the close box is refused, as VB6's disabled window ignores a click on it: nothing is
        // raised, the window stays, and the modal form comes forward (#822).
        if (Window is IFormView { HeldBy: { } modal })
        {
            modal.ActivateForm();
            return true;
        }
        // Unloading, or unloaded with Terminate still to come: the window stays until that has run, and goes then. Closed
        // now, the run would end before Terminate (#1046 review).
        if (unloading || instance.FormUnloaded)
            return true;
        if (!instance.FormLoaded)
            return false;
        var form = instance;
        Events.Raise("Form_QueryUnload", async () =>
        {
            // A second press, queued behind the first, finds the form gone or going, and asks nothing.
            if (form.FormUnloaded || unloading)
                return;
            await Dispatch(async () => await UnloadAsync(0));
        });
        return true;
    }

    /// <summary>
    /// Start the form as VB6 starts its startup form, before its window is shown (measured, #822): the modules'
    /// declarations, then the form's own, then <c>Form_Initialize</c>, then <c>Form_Load</c>. An error in each is
    /// reported as one in any event is. The whole start is one dispatch, as one event's handler chain is, so a step armed
    /// before it (F8 from design mode) breaks on the first statement it reaches, Initialize's or Load's, and a step off
    /// the end of Initialize goes on into Load (#1010 review).
    /// </summary>
    /// <returns>
    /// Whether the form started, and so is to be shown.
    /// <list type="bullet">
    /// <item>A failure in the declarations ends the start. Every event after it would run on variables that were never
    /// declared, and declarations never run later than this.</item>
    /// <item>A run-time error in Initialize or Load leaves the run going, as one does in any event procedure
    /// (debugger-vb6-divergences D23), so the start goes on.</item>
    /// <item>A fault, End, or the debugger's Stop ends the run, and the start stops there: Load is not raised after a
    /// fault in Initialize. The owner's reaction to a fault arrives later, posted, so the start can't wait for it.</item>
    /// <item>A form that unloaded itself during Load is not shown. Its Terminate is still to run
    /// (<see cref="FinishUnloadAsync"/>), and the run ends silently, as VB6's does (measured).</item>
    /// </list>
    /// </returns>
    public async Task<bool> StartAsync()
    {
        if (interpreter is null)
            return false;
        var form = EnsureInstance();
        started = true;
        // The start holds the gate as any handler does, so an event that comes during Load waits for it (#766).
        return await Events.Run("Form_Load", async () =>
        {
            try
            {
                if (await Report(EnsureDeclarations) != Outcome.Completed)
                    return false;
                if (await Report(() => interpreter.RaiseFormEvent(form, "Form_Initialize")) == Outcome.Over || RunIsOver())
                    return false;
                // A control or built-in member used in Initialize loaded the form there, and Load isn't raised again
                // (measured, #822).
                if (!form.FormLoaded && !form.FormUnloaded)
                {
                    form.FormLoaded = true;
                    ExecutionContext.State.ClearPendingControls(form);
                    Program?.Loaded(this);
                    await StartControls();
                    if (await Report(() => interpreter.RaiseFormEvent(form, "Form_Load")) == Outcome.Over)
                        return false;
                }
                // A form that unloaded itself in Load, as `Unload Me` there does, is not shown (measured, #1046 review).
                return !RunIsOver() && !form.FormUnloaded;
            }
            finally
            {
                interpreter.DebugController?.NotifyDispatchIdle();
            }
        });
    }

    private bool RunIsOver() => interpreter!.Ending || interpreter.DebugController?.IsAborting == true;

    private async Task EnsureDeclarations()
    {
        await interpreter!.RunModuleDeclarations();
        if (formDeclarationsRun)
            return;
        formDeclarationsRun = true;
        await interpreter.RunFormDeclarations(EnsureInstance());
    }

    public void ExecuteKeyboardEvent(string name, IReadOnlyList<Vb6Value> args, int writable, Action<int> completed)
    {
        Events.Raise(name, async () =>
        {
            if (interpreter is null || RunIsOver() || EnsureInstance().FormUnloaded)
                return;
            await Dispatch(async () =>
            {
                if (!started)
                    await EnsureDeclarations();
                var state = ExecutionContext.State;
                var slot = state.Alloc(args[writable]);
                var callArgs = new List<CallArg>();
                for (var i = 0; i < args.Count; i++)
                    callArgs.Add(new CallArg(args[i], i == writable ? slot : null));
                await interpreter.RaiseFormEvent(EnsureInstance(), name, callArgs);
                completed(Convert.ToInt32(state[slot].Value));
            });
        });
    }

    public async Task ExecuteEditEventAsync(string name, IList<Vb6Value> args)
    {
        if (interpreter is null) return;
        await Events.Run(name, async () =>
        {
            var form = EnsureInstance();
            if (RunIsOver() || form.FormUnloaded) return false;
            return await Dispatch(async () =>
            {
                if (!started) await EnsureDeclarations();
                var callArgs = new List<CallArg>();
                for (var i = 0; i < args.Count; i++)
                    callArgs.Add(new CallArg(args[i], null, new EditArgument(args, i)));
                await interpreter.RaiseFormEvent(form, name, callArgs);
            });
        });
    }

    private sealed record EditArgument(IList<Vb6Value> Arguments, int Index) : IByRefTarget
    {
        public Vb6Value.ValueType? DeclaredType => Arguments[Index].Type;
        public bool IsVariant => false;
        public Vb6Value Read() => Arguments[Index];
        public void Write(Vb6Value value) => Arguments[Index] = value;
    }

    public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null)
    {
        var argList = args is null ? null : new List<Vb6Value>(args);
        Raise(name, () => argList);
    }

    /// <inheritdoc/>
    public void ExecuteSubIf(string name, IReadOnlyList<Vb6Value>? args, Func<bool> when)
    {
        var argList = args is null ? null : new List<Vb6Value>(args);
        Raise(name, () => argList, when: when);
    }

    /// <inheritdoc/>
    public void ExecuteEvent(string name, Func<IReadOnlyList<Vb6Value>> args, EventCoalescing coalescing = EventCoalescing.None) =>
        Raise(name, args, coalescing);

    /// <inheritdoc/>
    public bool Handles(string name) => interpreter is not null && EnsureInstance().ClassDef.PrePass.Procedures.ContainsKey(name);

    /// <inheritdoc/>
    public void ExecuteTimer(string name, IReadOnlyList<Vb6Value>? args = null)
    {
        var argList = args is null ? null : new List<Vb6Value>(args);
        // Each element of a Timer array is a timer of its own, so its ticks are kept apart by Index.
        var key = argList is { Count: > 0 } ? $"{name}({argList[0].Value})" : name;
        Raise(name, () => argList, EventCoalescing.Timer, key);
    }

    internal void ExecuteControlInput(string name, Func<Task> work)
    {
        if (interpreter is null)
            return;
        Events.Raise(name, async () =>
        {
            var form = EnsureInstance();
            if (RunIsOver() || form.FormUnloaded || Program is not null && !form.FormLoaded)
                return;
            await Dispatch(async () =>
            {
                if (!started)
                    await EnsureDeclarations();
                await work();
            });
        });
    }

    private void Raise(string name, Func<IReadOnlyList<Vb6Value>?> args,
        EventCoalescing coalescing = EventCoalescing.None, string? key = null, Func<bool>? when = null)
    {
        // An event raised before SetCode has no code to run, as when a form's controls fail to build and the
        // run is abandoned half way. It used to reach here anyway and log a NullReferenceException after the
        // real error, which read as a second fault (#590).
        if (interpreter is null)
            return;
        var form = EnsureInstance();
        if (!form.ClassDef.PrePass.Procedures.ContainsKey(name))
            return;
        // The event is the form's: its own procedure of that name, run on its instance (#822). A context that was never
        // started, as a test's is, runs the declarations first; a started one ran them in StartAsync. The arguments are
        // worked out inside the dispatch, so one HexIDE can't work out is refused as the run's error (#974).
        async Task Work()
        {
            // An event that waited while the run ended runs nothing, and neither does one for a form that has unloaded,
            // whose window and controls VB6 has destroyed, or, in a program, one not loaded yet, which has none (#822).
            if (RunIsOver() || form.FormUnloaded || Program is not null && !form.FormLoaded)
                return;
            // Asked at its turn, not when it was raised (#1344).
            if (when is not null && !when())
                return;
            await Dispatch(async () =>
            {
                if (!started)
                    await EnsureDeclarations();
                await interpreter.RaiseFormEvent(form, name, args());
            });
        }

        // Typed in the Immediate window while paused, `Text1.Text = "x"` raises Text1_Change, which is refused there (see
        // Report). The refusal belongs to the statement typed, so it runs now rather than waiting for the paused handler.
        if (interpreter.SuppressUserProcedureCalls)
        {
            Work().ListenErrors();
            return;
        }
        // One handler at a time: at once if nothing runs or the running handler's own code raised it, else in turn (#766).
        Events.Raise(key ?? name, Work, coalescing);
    }

    // How a piece of a dispatch ended: it finished; it stopped on a run-time or compile error, which was reported and
    // leaves the run going (D22, D23); or the run is over, after a fault, a cancellation, or the debugger's Stop.
    private enum Outcome { Completed, Reported, Over }

    // The event-dispatch boundary: one handler chain, its outcome reported, then the dispatch is idle. True when the
    // run carries on afterwards.
    private async Task<bool> Dispatch(Func<Task> work)
    {
        try
        {
            return await Report(work) != Outcome.Over;
        }
        finally
        {
            // Event-dispatch boundary: this handler chain has returned (or aborted). Disarm any step the
            // front-end armed but never consumed, so a leftover Step Into/Over/Out can't spuriously break the
            // NEXT event handler. A no-op unless a step is armed and the controller is Running. (Module top-level
            // execution — the headless test path — never routes through here, so its stepping is unaffected.)
            interpreter?.DebugController?.NotifyDispatchIdle();
        }
    }

    // Every way a piece of event code can stop, reported as what it is.
    private async Task<Outcome> Report(Func<Task> work)
    {
        try
        {
            await work();
            return Outcome.Completed;
        }
        catch (VBRunTimeException e)
        {
            RunTimeError?.Invoke(this, e);
            return Outcome.Reported;
        }
        catch (VBCompileErrorException e)
        {
            CompileError?.Invoke(this, e);
            return Outcome.Reported;
        }
        catch (Debugging.StopExecutionSignal)
        {
            // Debugger Stop (End) unwinds via this signal. The interpreter entry points already swallow it;
            // this is the belt-and-suspenders at the run-window boundary so it never logs as an error.
            return Outcome.Over;
        }
        catch (Exception e)
        {
            Log.Error(e, "Unhandled exception during form execution");
            // Neither of these is a fault in the run. A cancellation is how a run being torn down reaches a
            // statement still waiting on it, such as a MsgBox. An ImmediateEvalException is the Immediate
            // window refusing to run an event procedure while paused: `Text1.Text = "x"` typed there raises
            // Text1_Change, which cannot run in break mode. The refusal is the Immediate window's answer, and
            // the paused run carries on.
            if (e is not (OperationCanceledException or Debugging.ImmediateEvalException))
                Faulted?.Invoke(this, e);
            return e is Debugging.ImmediateEvalException ? Outcome.Reported : Outcome.Over;
        }
    }
}
