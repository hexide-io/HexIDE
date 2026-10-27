using System.Threading.Tasks;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class BareControlTypeTests : BaseVBTestFixture
{
    [Fact]
    public async Task Bare_ProgressBar_declares_Nothing_and_accepts_its_control()
    {
        await Execute("""
            Dim pb As ProgressBar
            Debug.Print TypeName(pb)
            Set pb = first
            Debug.Print TypeName(pb), TypeOf pb Is ProgressBar
            """, false);
        AssertDebugLog(["Nothing", "ProgressBar", true]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bare_TypeOf_follows_the_first_project_library(bool reverse)
    {
        await Execute("""
            Debug.Print TypeOf first Is ProgressBar, TypeOf second Is ProgressBar
            Debug.Print TypeOf first Is MSComctlLib.ProgressBar, TypeOf second Is ComctlLib.ProgressBar
            """, reverse);
        AssertDebugLog([true, false, !reverse, !reverse]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bare_typed_Set_refuses_the_other_library(bool reverse)
    {
        await Execute("""
            On Error Resume Next
            Dim pb As ProgressBar
            Set pb = first
            Debug.Print Err.Number
            Set pb = second
            Debug.Print Err.Number
            """, reverse);
        AssertDebugLog([new Vb6Value(0L), new Vb6Value(13L)]);
    }

    private async Task Execute(string code, bool reverse)
    {
        var first = reverse ? ControlLibraries.ComctlLib : ControlLibraries.MSComctlLib;
        var second = reverse ? ControlLibraries.MSComctlLib : ControlLibraries.ComctlLib;
        context.AllocVariable(rootEnv, "first", new Vb6Value(new VBProgressBar { Library = first }));
        context.AllocVariable(rootEnv, "second", new Vb6Value(new VBProgressBar { Library = second }));
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, code);
        vb.SetAppInfo(new AppInfo(ControlLibraries: [first, second]));
        await vb.Execute();
    }
}
