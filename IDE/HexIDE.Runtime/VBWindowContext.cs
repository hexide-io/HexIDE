using System;
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

    public void SetCode(string code)
    {
        Code = code;
        interpreter = new BasicInterpreter(standardLibrary, ExecutionContext,RootEnv, code);
    }

    public void ExecuteSub(string name)
    {
        async Task Execute()
        {
            try
            {
                await interpreter!.ExecuteSub(name, null, true);
            }
            catch (VBRunTimeException e)
            {
                RunTimeError?.Invoke(this, e);
            }
            catch (VBCompileErrorException e)
            {
                CompileError?.Invoke(this, e);
            }
            catch (Exception e)
            {
                Log.Error(e, "Unhandled exception during form execution");
            }
        }

        Execute().ListenErrors();
    }
}