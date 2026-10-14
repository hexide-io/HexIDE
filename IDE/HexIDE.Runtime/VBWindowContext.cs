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

    public ModuleExecutionContext ExecutionContext { get; } = new();

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
            DebugController = debugController
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
        }
        return instance;
    }

    /// <summary>
    /// Hold the form's controls until it loads. VB6 loads a form at the first use of one of its controls, even inside
    /// <c>Form_Initialize</c> (measured, #822); HexIDE can't yet, so a use before Load is refused (#1009) rather than read
    /// in the wrong order. Called once the controls are in the instance's env.
    /// </summary>
    public void HoldControlsUntilLoad()
    {
        var form = EnsureInstance();
        foreach (var location in form.InstanceEnv.variableToLocation.Values)
            if (ExecutionContext.State[location].Value is Avalonia.Controls.Control or ControlArrayGroup)
                ExecutionContext.State.MarkPendingControl(location, form.ClassName);
    }

    /// <summary>
    /// Start the form as VB6 starts its startup form, before its window is shown (measured, #822): the modules'
    /// declarations, then the form's own, then <c>Form_Initialize</c>, then <c>Form_Load</c>. Each event runs inside the
    /// boundary <see cref="ExecuteSub"/> gives any event, so an error in one is reported as one is there.
    /// </summary>
    /// <returns>
    /// Whether the form started, and so is to be shown. A run-time error in Initialize or Load leaves the run going, as
    /// one does in any event procedure (debugger-vb6-divergences D23), so the start goes on. A fault, End, or the
    /// debugger's Stop ends the run: the start stops there, and Load is not raised after a fault in Initialize. The
    /// owner's reaction to a fault arrives later, posted, so the start can't wait for it to say so.
    /// </returns>
    public async Task<bool> StartAsync()
    {
        if (interpreter is null)
            return false;
        var form = EnsureInstance();
        var initialized = await Dispatch(async () =>
        {
            await EnsureDeclarations();
            await interpreter.RaiseFormEvent(form, "Form_Initialize");
        });
        if (!initialized || RunIsOver())
            return false;
        form.FormLoaded = true;
        ExecutionContext.State.ClearPendingControls(form.ClassName);
        return await Dispatch(() => interpreter.RaiseFormEvent(form, "Form_Load")) && !RunIsOver();
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
        // An event raised before SetCode has no code to run, as when a form's controls fail to build and the
        // run is abandoned half way. It used to reach here anyway and log a NullReferenceException after the
        // real error, which read as a second fault (#590).
        if (interpreter is null)
            return;
        var form = EnsureInstance();
        var argList = args is null ? null : new List<Vb6Value>(args);
        // The event is the form's: its own procedure of that name, run on its instance (#822). A context that was never
        // started, as a test's is, runs the declarations first.
        Dispatch(async () =>
        {
            await EnsureDeclarations();
            await interpreter.RaiseFormEvent(form, name, argList);
        }).ListenErrors();
    }

    // The event-dispatch boundary: every way an event procedure can stop, reported as what it is. True when the run
    // carries on afterwards: the procedure finished, or stopped on a run-time or compile error, which leaves the run
    // going (D22, D23). False when the run is over: a fault, a cancellation, or the debugger's Stop.
    private async Task<bool> Dispatch(Func<Task> work)
    {
        try
        {
            await work();
            return true;
        }
        catch (VBRunTimeException e)
        {
            RunTimeError?.Invoke(this, e);
            return true;
        }
        catch (VBCompileErrorException e)
        {
            CompileError?.Invoke(this, e);
            return true;
        }
        catch (Debugging.StopExecutionSignal)
        {
            // Debugger Stop (End) unwinds via this signal. The interpreter entry points already swallow it;
            // this is the belt-and-suspenders at the run-window boundary so it never logs as an error.
            return false;
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
            return e is Debugging.ImmediateEvalException;
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
}
