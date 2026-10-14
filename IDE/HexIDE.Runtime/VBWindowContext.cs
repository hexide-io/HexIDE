using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
    private VbObject? instance;
    private bool formDeclarationsRun;
    // Set once StartAsync begins. A started form's declarations run there and nowhere else, so an event can never run
    // them late, after Load has used the variables they would re-create (#1010 review).
    private bool started;

    public ModuleExecutionContext ExecutionContext { get; } = new();

    /// <summary>
    /// Runs this program's handlers one at a time, as VB6's single thread does (#766). One per run: a program of several
    /// forms will share one, since VB6 serialises them all.
    /// </summary>
    public EventPump Events { get; } = new();

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
            instance = interpreter.NewFormInstance(interpreter.PrimaryModule);
            instance.FormWindow = Window;
            // The context's own hold, apart from the one the form's name has: in VB6 a loaded form is kept alive by
            // Forms, so `Set Form1 = Nothing` from a module clears the name and leaves the running form be (#1010
            // review). Without it, that Set terminated the instance under its own window.
            interpreter.AddRef(Vb6Value.NewObject(instance));
        }
        return instance;
    }

    /// <summary>
    /// Hold the form's controls until it loads. VB6 loads a form at the first use of one of its controls, even inside
    /// <c>Form_Initialize</c> (measured, #822); HexIDE can't yet, so a use before Load is refused (#1009) rather than read
    /// in the wrong order. Called once the controls are in the instance's env.
    /// </summary>
    public void HoldControlsUntilLoad() => HoldControls(EnsureInstance());

    private void HoldControls(VbObject form)
    {
        foreach (var location in form.InstanceEnv.variableToLocation.Values)
            if (ExecutionContext.State[location].Value is Avalonia.Controls.Control or ControlArrayGroup)
                ExecutionContext.State.MarkPendingControl(location, form.ClassName);
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
        if (!form.FormLoaded)
            throw new NotImplementedException($"Unload of {form.ClassName} before it has loaded is not implemented yet (#822).");
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
        form.FormUnloaded = true;
        ExecutionContext.State.MarkUnloaded(form.ClassName);
        HoldControls(form);
        if (Window is not null)
            Window.IsVisible = false;
        terminatePending = true;
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
    /// Unload, raised as any event is, so the window must stay open until it has. False when there is no program to ask.
    /// </summary>
    public bool UnloadFromCloseBox()
    {
        if (interpreter is null || RunIsOver() || instance is null)
            return false;
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
                form.FormLoaded = true;
                ExecutionContext.State.ClearPendingControls(form.ClassName);
                // A form that unloaded itself in Load, as `Unload Me` there does, is not shown (measured, #1046 review).
                return await Report(() => interpreter.RaiseFormEvent(form, "Form_Load")) != Outcome.Over && !RunIsOver()
                    && !form.FormUnloaded;
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

    public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null)
    {
        var argList = args is null ? null : new List<Vb6Value>(args);
        Raise(name, () => argList);
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

    private void Raise(string name, Func<IReadOnlyList<Vb6Value>?> args,
        EventCoalescing coalescing = EventCoalescing.None, string? key = null)
    {
        // An event raised before SetCode has no code to run, as when a form's controls fail to build and the
        // run is abandoned half way. It used to reach here anyway and log a NullReferenceException after the
        // real error, which read as a second fault (#590).
        if (interpreter is null)
            return;
        var form = EnsureInstance();
        // The event is the form's: its own procedure of that name, run on its instance (#822). A context that was never
        // started, as a test's is, runs the declarations first; a started one ran them in StartAsync. The arguments are
        // worked out inside the dispatch, so one HexIDE can't work out is refused as the run's error (#974).
        async Task Work()
        {
            // An event that waited while the run ended runs nothing, and neither does one for a form that has unloaded,
            // whose window and controls VB6 has destroyed.
            if (RunIsOver() || form.FormUnloaded)
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
