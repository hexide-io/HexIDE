using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Utils;
using Serilog;

namespace HexIDE.Runtime;

public class VBWindowContext : IModuleExecutionRoot
{
    private readonly IBasicStandardLibrary standardLibrary;
    private BasicInterpreter? interpreter;

    public ModuleExecutionContext ExecutionContext { get; } = new();

    public string Code { get; private set; } = "";

    public ExecutionEnvironment RootEnv { get; } = new();

    public static event Action<VBWindowContext, VBRunTimeException>? RunTimeError;

    public static event Action<VBWindowContext, VBCompileErrorException>? CompileError;

    public VBWindowContext(IBasicStandardLibrary standardLibrary)
    {
        this.standardLibrary = standardLibrary;
    }

    /// <summary>
    /// Compile the form/module code into a fresh interpreter. <paramref name="moduleName"/> becomes the
    /// interpreter's primary module name — the name the debug pause-gate reports (and that breakpoints are keyed
    /// by), so it must be the form/module's real name, not the "Module1" default. <paramref name="debugController"/>
    /// is the per-session controller (null ⇒ no debugging, zero gate overhead).
    /// </summary>
    public void SetCode(string code, string moduleName = "Module1", Debugging.IDebugController? debugController = null,
        Interpreter.AppInfo? appInfo = null)
    {
        Code = code;
        interpreter = new BasicInterpreter(standardLibrary, ExecutionContext, RootEnv, code, moduleName)
        {
            DebugController = debugController
        };
        // What `App` reports. Null means no project behind this run (a bare context), and App then reports
        // empty rather than inventing an identity.
        if (appInfo is not null)
            interpreter.SetAppInfo(appInfo);
    }

    public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null)
    {
        var argList = args is null ? null : new List<Vb6Value>(args);
        async Task Execute()
        {
            try
            {
                await interpreter!.ExecuteSub(name, argList, true);
            }
            catch (VBRunTimeException e)
            {
                RunTimeError?.Invoke(this, e);
            }
            catch (VBCompileErrorException e)
            {
                CompileError?.Invoke(this, e);
            }
            catch (Debugging.StopExecutionSignal)
            {
                // Debugger Stop (End) unwinds via this signal. The interpreter entry points already swallow it;
                // this is the belt-and-suspenders at the run-window boundary so it never logs as an error.
            }
            catch (Exception e)
            {
                Log.Error(e, "Unhandled exception during form execution");
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

        Execute().ListenErrors();
    }
}