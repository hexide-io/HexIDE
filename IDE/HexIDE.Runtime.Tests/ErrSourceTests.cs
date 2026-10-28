using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Components.InBox;

namespace HexIDE.Runtime.Tests;

public class ErrSourceTests : BaseVBTestFixture
{
    // #1483: vb6.exe SP6, native and p-code, oracle commit 4552d7cf.
    [Theory]
    [InlineData("Dim c As New Collection\nc.Remove 1")]
    [InlineData("Err.Raise 1000")]
    [InlineData("Error 5")]
    public async Task Source_IsProjectName_RegardlessOfTitle(string statement)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Probe");
        project.UnknownPreSectionLines.Add((0, "Title=\"Titled App\""));
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv,
            $"On Error Resume Next\n{statement}\nDebug.Print Err.Source\n");
        vb.SetAppInfo(AppInfo.FromProject(project));
        await vb.Execute();
        AssertDebugLog(["Probe"]);
    }

    [Theory]
    [InlineData("Dim c As New Collection\nc.Remove 1", false)]
    [InlineData("Err.Raise 1000", false)]
    [InlineData("Error 5", false)]
    [InlineData("Dim c As New Collection\nc.Remove 1", true)]
    [InlineData("Err.Raise 1000", true)]
    [InlineData("Error 5", true)]
    public async Task ClassError_UsesProjectName_InCalleeOrCallerHandler(string statement, bool inside)
    {
        var body = inside
            ? $"On Error Resume Next\n{statement}\nDebug.Print Err.Source\n"
            : statement;
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv,
            "On Error Resume Next\nDim x As New Class1\nx.Fail\n" + (inside ? "" : "Debug.Print Err.Source\n"),
            classModules: [("Class1", $"Public Sub Fail()\n{body}\nEnd Sub\n")])
            { ProjectName = "Probe" };
        await vb.Execute();
        AssertDebugLog(["Probe"]);
    }

    [Theory]
    [InlineData("Dim c As New Collection\nc.Remove 1")]
    [InlineData("Err.Raise 1000")]
    [InlineData("Error 5")]
    public async Task FormGoToHandler_UsesProjectName(string statement)
    {
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv,
            $"On Error GoTo Handler\n{statement}\nExit Sub\nHandler:\nDebug.Print Err.Source\n",
            primaryName: "Form1", primaryKind: InterpreterModuleKind.Form) { ProjectName = "Probe" };
        await vb.Execute();
        AssertDebugLog(["Probe"]);
    }

    // Exercise the managed library boundary on Linux; this does not activate a native COM DLL.
    [Theory]
    [InlineData("Dim c As New Collection\nc.Remove 1", "ProbeDll", "Fail")]
    [InlineData("Err.Raise 1000", "ProbeDll", "Fail")]
    [InlineData("Err.Raise 1001, \"MySrc\"", "MySrc", "Fail")]
    [InlineData("", "Probe", "Absent")]
    public async Task LibraryError_KeepsRaisingProjectsSource(string statement, string expected, string member)
    {
        var library = new RuntimeLibrary("ProbeDll", "{11111111-1111-1111-1111-111111111111}", "1.0",
            [new("Class1", () => new LibraryCode(async () =>
            {
                var dll = new BasicInterpreter(new MockStdLib(debug), new ModuleExecutionContext(),
                    new ExecutionEnvironment(), "Dim x As New Class1\nx.Fail\n",
                    classModules: [("Class1", $"Public Sub Fail()\n{statement}\nEnd Sub\n")])
                    { ProjectName = "ProbeDll" };
                await dll.Execute();
            }), ["Fail"], false)], new Dictionary<string, IReadOnlyDictionary<string, long>>());
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv,
            $"On Error Resume Next\nDim x As Object\nSet x = New ProbeDll.Class1\nx.{member}\nDebug.Print Err.Source\n")
            { ProjectName = "Probe" };
        vb.SetAppInfo(new AppInfo(ControlLibraries: [library]));
        await vb.Execute();
        AssertDebugLog([expected]);
    }

    private sealed class LibraryCode(Func<Task> run) : ICSharpAsyncProxy
    {
        public void Call(string method, List<Vb6Value> args) => throw new InvalidOperationException();
        public async Task<Vb6Value?> CallAsync(string method, List<Vb6Value> args)
        {
            if (!method.Equals("Fail", StringComparison.OrdinalIgnoreCase))
                throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
            await run();
            return null;
        }
    }

    [Fact]
    public async Task SourcelessRaise_KeepsSetSource_AndClearEmptiesIt()
    {
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, """
            On Error Resume Next
            Err.Source = "Set"
            Err.Raise 7
            Debug.Print Err.Source
            Err.Raise 1001, "MySrc"
            Debug.Print Err.Source
            Err.Clear
            Debug.Print Err.Source
            Err.Raise 1000
            Debug.Print Err.Source
            """) { ProjectName = "Probe" };
        await vb.Execute();
        AssertDebugLog(["Set", "MySrc", "", "Probe"]);
    }
}
