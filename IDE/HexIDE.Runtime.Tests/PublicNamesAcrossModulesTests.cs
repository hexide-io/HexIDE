using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A standard module's Public variables and Consts, named bare from another module (#880). Every expectation is
/// measured with <c>vb6.exe /make</c>, native and p-code agreeing (the oracle's "Another module's Public names, bare").
/// The primary module's top-level statements run after the other modules' declarations, as Main would.
/// </summary>
public class PublicNamesAcrossModulesTests : BaseVBTestFixture
{
    private const string Module2 = "Public G As Long\nPublic Const M = 3\nGlobal G2 As Integer\n" +
        "Public Sub Init2()\n    G = 5\n    G2 = 6\nEnd Sub\n" +
        "Public Function Show2() As String\n    Show2 = CStr(G)\nEnd Function\n";

    private void Printed(params string[] lines) => AssertDebugLog([.. lines.Select(l => new Vb6Value(l))]);

    private async Task RunProject(string primary, (string Name, string Code)[] modules, (string Name, string Code)[] classes)
    {
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, primary, "Module1", modules, classes);
        await vb.Execute();
    }

    [Fact]
    public async Task Another_modules_Public_variable_Const_and_Global_are_read_written_and_passed_ByRef_bare()
    {
        // Measured: `Long 5 Integer 3 6`, then 7 and 8. HexIDE read each as Empty: G, M and G2 became new variables here.
        await RunModules("Init2\n" +
            "Debug.Print TypeName(G) & \" \" & G & \" \" & TypeName(M) & \" \" & M & \" \" & G2\n" +
            "G = 7\nDebug.Print \"\" & Module2.G\nBump G\nDebug.Print Show2()\n" +
            "Sub Bump(n As Long)\n    n = n + 1\nEnd Sub\n",
            ("Module2", Module2));

        Printed("Long 5 Integer 3 6", "7", "8");
    }

    [Fact]
    public async Task Another_modules_Public_names_are_read_under_Option_Explicit()
    {
        // Measured: `3 5 6`. HexIDE refused M as "Variable not defined".
        await RunModules("Option Explicit\nInit2\nDebug.Print M & \" \" & G & \" \" & G2\n", ("Module2", Module2));

        Printed("3 5 6");
    }

    [Fact]
    public async Task Another_modules_Private_names_are_not_visible()
    {
        // Measured: `Empty Empty Empty`. A Private variable, a module-level Dim and a Private Const are each a new
        // variable here.
        await RunModules("Init2\nDebug.Print TypeName(P) & \" \" & TypeName(D) & \" \" & TypeName(PC)\n",
            ("Module2", "Private P As Long\nDim D As Long\nPrivate Const PC = 9\nPublic Sub Init2()\n    P = 5\n    D = 6\nEnd Sub\n"));

        Printed("Empty Empty Empty");
    }

    [Fact]
    public async Task Another_modules_Private_variable_is_not_defined_under_Option_Explicit()
    {
        var run = () => RunModules("Option Explicit\nDebug.Print P\n", ("Module2", "Private P As Long\n"));

        (await run.Should().ThrowAsync<VBVariableNotDefinedException>()).WithMessage("*Variable not defined (P)*");
    }

    [Theory]
    [InlineData("Public X As Long\n", "Public X As Long\n")]
    [InlineData("Public X As Long\n", "Public Const X = 1\n")]
    [InlineData("Public X As Long\n", "Public Sub X()\nEnd Sub\n")]
    [InlineData("Public X As Long\n", "Public Function X() As Long\n    X = 9\nEnd Function\n")]
    public async Task A_name_two_modules_export_is_ambiguous_used_bare(string module2, string module3)
    {
        // Measured for two variables, and for a variable beside a Sub and beside a Function: "Ambiguous name detected".
        var run = () => RunModules("Debug.Print X\n", ("Module2", module2), ("Module3", module3));

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Ambiguous name detected: X*");
    }

    [Fact]
    public async Task A_name_two_modules_export_is_read_qualified_and_by_its_own_module()
    {
        // Measured: `1 2`, then `own 1`. The clash is only in a bare use from a third module. The probe stored each through
        // its qualifier, which HexIDE does not do yet (#1006), so each module stores its own here.
        await RunModules("Set2\nSet3\nDebug.Print Module2.X & \" \" & Module3.X\nShow2\n",
            ("Module2", "Public X As Long\nPublic Sub Set2()\n    X = 1\nEnd Sub\nPublic Sub Show2()\n    Debug.Print \"own \" & X\nEnd Sub\n"),
            ("Module3", "Public X As Long\nPublic Sub Set3()\n    X = 2\nEnd Sub\n"));

        Printed("1 2", "own 1");
    }

    [Fact]
    public async Task The_modules_own_variable_wins()
    {
        // Measured: `1 2`. The probe stored Module2's through its qualifier (#1006 here), so Module2 stores its own.
        await RunModules("Dim X As Long\nX = 1\nSet2\nDebug.Print X & \" \" & Module2.X\n",
            ("Module2", "Public X As Long\nPublic Sub Set2()\n    X = 2\nEnd Sub\n"));

        Printed("1 2");
    }

    [Fact]
    public async Task The_modules_own_Function_wins()
    {
        // Measured: `own fn`.
        await RunModules("Debug.Print V\nFunction V() As String\n    V = \"own fn\"\nEnd Function\n",
            ("Module2", "Public V As String\n"));

        Printed("own fn");
    }

    [Fact]
    public async Task A_local_shadows_another_modules_Public()
    {
        // Measured: `local 0`.
        await RunModules("Run1\nSub Run1()\n    Dim G As String\n    G = \"local\"\n    Debug.Print G & \" \" & Module2.G\nEnd Sub\n",
            ("Module2", "Public G As Long\n"));

        Printed("local 0");
    }

    [Fact]
    public async Task Another_modules_Public_Const_is_found_before_a_built_in_constant()
    {
        // Measured: `42 Integer`. The project comes before the library.
        await RunModules("Debug.Print vbCancel & \" \" & TypeName(vbCancel)\n", ("Module2", "Public Const vbCancel = 42\n"));

        Printed("42 Integer");
    }

    [Fact]
    public async Task Another_modules_Public_dynamic_array_is_ReDimmed_and_written_bare()
    {
        // Measured: `4 9 9`. The probe read the element as Module2.A(2), which reads the whole array here (#965), so
        // Module2 reads its own.
        await RunModules("ReDim A(4)\nA(2) = 9\nDebug.Print UBound(Module2.A) & \" \" & A2() & \" \" & A(2)\n",
            ("Module2", "Public A() As Long\nPublic Function A2() As Long\n    A2 = A(2)\nEnd Function\n"));

        Printed("4 9 9");
    }

    [Fact]
    public async Task Another_modules_Public_object_is_Set_and_called_bare()
    {
        // Measured with a Collection: `Set C = New Collection` and `C.Add 1` bare, then `1 1`. HexIDE can't create a
        // Collection with New yet, so the object here is a class's, set and used the same way.
        await RunProject("Set C = New Class1\nC.Bump\nDebug.Print CN() & \" \" & C.N\n",
            [("Module2", "Public C As Class1\nPublic Function CN() As Long\n    CN = C.N\nEnd Function\n")],
            [("Class1", "Public N As Long\nPublic Sub Bump()\n    N = N + 1\nEnd Sub\n")]);

        Printed("1 1");
    }

    [Fact]
    public async Task Another_modules_Public_UDT_is_written_bare()
    {
        // Measured: `3 3`.
        await RunModules("P.X = 3\nDebug.Print Module2.P.X & \" \" & P.X\n",
            ("Module2", "Public Type PT\n    X As Long\nEnd Type\nPublic P As PT\n"));

        Printed("3 3");
    }

    [Fact]
    public async Task A_class_reads_and_writes_a_modules_Public_variable_bare()
    {
        // Measured: 5, then 11.
        await RunProject("Init2\nDim c As Class1\nSet c = New Class1\nDebug.Print \"\" & c.GetG\nc.SetG\nDebug.Print \"\" & Module2.G\n",
            [("Module2", Module2)],
            [("Class1", "Public Function GetG() As Long\n    GetG = G\nEnd Function\nPublic Sub SetG()\n    G = 11\nEnd Sub\n")]);

        Printed("5", "11");
    }
}
