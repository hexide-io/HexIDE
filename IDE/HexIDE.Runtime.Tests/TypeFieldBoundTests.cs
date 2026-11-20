using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Measured native and p-code rows quoted in hexide-io/HexIDE#4595 (2026-11-18).
public class TypeFieldBoundTests : BaseVBTestFixture
{
    private const string Constants = "Public Const PC As Long = 40";
    private const string Grids = "Public Enum PG\nPlayerGrid1 = 1\nPlayerGrid2 = 2\nEnd Enum";

    [Theory]
    [InlineData("Const N = 3", "a(1 To N) As Long", "LBound(x.a)", 1L)]
    [InlineData("Const N = 3", "a(1 To N) As Long", "UBound(x.a)", 3L)]
    [InlineData("Enum S\nMAXS = 4\nEnd Enum", "a(0 To MAXS) As Integer", "LBound(x.a)", 0L)]
    [InlineData("Enum S\nMAXS = 4\nEnd Enum", "a(0 To MAXS) As Integer", "UBound(x.a)", 4L)]
    [InlineData("Const N = 3", "a(N + 1) As Long", "LBound(x.a)", 0L)]
    [InlineData("Const N = 3", "a(N + 1) As Long", "UBound(x.a)", 4L)]
    [InlineData("", "a(1 To Module2.PC) As Long", "LBound(x.a)", 1L)]
    [InlineData("", "a(1 To Module2.PC) As Long", "UBound(x.a)", 40L)]
    [InlineData("", "a(PC \\ 10, 1 To 2) As Integer", "LBound(x.a, 1)", 0L)]
    [InlineData("", "a(PC \\ 10, 1 To 2) As Integer", "UBound(x.a, 1)", 4L)]
    [InlineData("", "a(PC \\ 10, 1 To 2) As Integer", "LBound(x.a, 2)", 1L)]
    [InlineData("", "a(PC \\ 10, 1 To 2) As Integer", "UBound(x.a, 2)", 2L)]
    [InlineData("", "a(PlayerGrid1 To PlayerGrid2) As Long", "LBound(x.a)", 1L)]
    [InlineData("", "a(PlayerGrid1 To PlayerGrid2) As Long", "UBound(x.a)", 2L)]
    [InlineData("", "a(Module2.PlayerGrid1 To PG.PlayerGrid2) As Long", "LBound(x.a)", 1L)]
    [InlineData("", "a(Module2.PlayerGrid1 To PG.PlayerGrid2) As Long", "UBound(x.a)", 2L)]
    public async Task Standard_module_bounds(string before, string field, string read, long expected)
    {
        await RunModules($"{before}\nPrivate Type T\n{field}\nEnd Type\nDim x As T\nDebug.Print {read}",
            ("Module2", Constants + "\n" + Grids));
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Fact]
    public async Task Issue_repro_and_store()
    {
        await RunModules("Private Const N As Byte = 3\nPublic Enum Sizes\nsMin = 2\nsMax = 4\nEnd Enum\nPrivate Type T\na(1 To N) As Long\nb(sMin To sMax) As Integer\nEnd Type\nDim x As T\nx.a(3) = 7\nDebug.Print UBound(x.a)\nDebug.Print LBound(x.b)\nDebug.Print UBound(x.b)\nDebug.Print x.a(3)");
        AssertDebugLog([new Vb6Value(3L), new Vb6Value(2L), new Vb6Value(4L), new Vb6Value(7L)]);
    }

    [Fact]
    public async Task Public_class_enum()
    {
        await RunClasses("Private Type T\ng(PlayerGrid1 To PlayerGrid2) As Long\nEnd Type\nDim x As T\nDebug.Print LBound(x.g)\nDebug.Print UBound(x.g)", ("Class1", Grids));
        AssertDebugLog([new Vb6Value(1L), new Vb6Value(2L)]);
    }

    [Theory]
    [InlineData("LBound(this.a)", 1L)]
    [InlineData("UBound(this.a)", 3L)]
    [InlineData("UBound(this.b)", 5L)]
    [InlineData("UBound(this.c)", 40L)]
    [InlineData("LBound(y.a)", 1L)]
    [InlineData("UBound(y.a)", 3L)]
    [InlineData("this.a(3)", 7L)]
    public async Task Class_bounds(string read, long expected)
    {
        await RunModulesAndClasses("Dim o As New Class1\no.Test", [("Module2", Constants)],
            [("Class1", $"Private Const N As Byte = 3\nPrivate Enum CE\ncMax = 5\nEnd Enum\nPrivate Type T\na(1 To N) As Long\nb(0 To cMax) As Integer\nc(1 To PC) As Long\nEnd Type\nPrivate this As T\nPublic Sub Test()\nDim y As T\nthis.a(3) = 7\nDebug.Print {read}\nEnd Sub")]);
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("Private Type T\na(1 To N) As Long\nEnd Type\nConst N = 3", "")]
    [InlineData("Private Type T\na(1 To PV) As Long\nEnd Type", "Public PV As Long")]
    public async Task Nonconstant_module_bound(string declarations, string other)
    {
        Func<Task> run = () => RunModules(declarations, ("Module2", other));
        var error = await run.Should().ThrowAsync<VBCompileErrorException>();
        error.Which.Message.Should().EndWith("Constant expression required");
    }

    [Fact]
    public async Task Class_const_below_type()
    {
        Func<Task> run = () => RunClasses("Dim o As New Class1\no.Test",
            ("Class1", "Private Type T\na(1 To N) As Long\nEnd Type\nPrivate Const N As Byte = 3\nPublic Sub Test()\nEnd Sub"));
        var error = await run.Should().ThrowAsync<VBCompileErrorException>();
        error.Which.Message.Should().EndWith("Constant expression required");
    }
    // Native and p-code measurements: hexide-io/HexIDE#4734, oracle PR #4800.
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Allocation_before_type(bool inClass, bool constantAfterVariable)
    {
        const string constant = "Private Const N As Byte = 3\n";
        const string variable = "Private x As T\n";
        var declarations = (constantAfterVariable ? variable + constant : constant + variable)
            + "Private Type T\na(1 To N) As Long\nEnd Type\n";
        const string body = "Debug.Print LBound(x.a)\nDebug.Print UBound(x.a)\nx.a(N) = 7\nDebug.Print x.a(N)";
        if (inClass)
            await RunClasses("Dim o As New Class1\no.Test", ("Class1", declarations + "Public Sub Test()\n" + body + "\nEnd Sub"));
        else
            await RunModules(declarations + body);
        AssertDebugLog([new Vb6Value(1L), new Vb6Value(3L), new Vb6Value(7L)]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Allocation_before_type_with_constant_chain(bool inClass)
    {
        const string declarations = "Private x As T\nPrivate Const M As Byte = 2\nPrivate Const N As Byte = M + 1\nPrivate Type T\na(1 To N) As Long\nEnd Type\n";
        const string body = "Debug.Print LBound(x.a)\nDebug.Print UBound(x.a)\nx.a(N) = 7\nDebug.Print x.a(N)";
        if (inClass)
            await RunClasses("Dim o As New Class1\no.Test", ("Class1", declarations + "Public Sub Test()\n" + body + "\nEnd Sub"));
        else
            await RunModules(declarations + body);
        AssertDebugLog([new Vb6Value(1L), new Vb6Value(3L), new Vb6Value(7L)]);
    }

    // A second instance finds the Type's bounds already resolved and runs its Consts in its own walk, not ahead of it.
    [Fact]
    public async Task Allocation_before_type_in_a_second_instance()
    {
        const string declarations = "Private x As T\nPrivate Const N As Byte = 3\nPrivate Type T\na(1 To N) As Long\nEnd Type\n";
        await RunClasses("Dim o As New Class1\no.Test\nDim p As New Class1\np.Test",
            ("Class1", declarations + "Public Sub Test()\nDebug.Print UBound(x.a)\nDebug.Print CLng(N)\nEnd Sub"));
        AssertDebugLog([new Vb6Value(3L), new Vb6Value(3L), new Vb6Value(3L), new Vb6Value(3L)]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Allocation_before_type_const_below_type(bool inClass)
    {
        const string declarations = "Private x As T\nPrivate Type T\na(1 To N) As Long\nEnd Type\nPrivate Const N As Byte = 3\n";
        Func<Task> run = inClass
            ? () => RunClasses("Dim o As New Class1\no.Test", ("Class1", declarations + "Public Sub Test()\nEnd Sub"))
            : () => RunModules(declarations);
        (await run.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should()
            .EndWith("Constant expression required");
    }

    // vb6.exe SP6 native and p-code measurements: hexide-io/HexIDE#4925, case 4.
    [Theory]
    [InlineData("", "a(5 To 1) As Integer")]
    [InlineData("Const L = 5\nConst H = 1\n", "a(L To H) As Integer")]
    public async Task Inverted_field_bound_is_a_compile_error(string constants, string field)
    {
        Func<Task> run = () => RunModules($"{constants}Private Type T\n{field}\nEnd Type");
        (await run.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should()
            .EndWith("Range has no values");
    }

    [Fact]
    public async Task Equal_field_bounds_allocate_one_element()
    {
        await RunModules("Private Type T\na(3 To 3) As Integer\nEnd Type\nDim u As T\nDebug.Print LBound(u.a) & \" \" & UBound(u.a)");
        AssertDebugLog(["3 3"]);
    }

    [Fact]
    public async Task Allocation_runs_an_unstarted_standard_module()
    {
        // Execute runs the other modules' declarations in order, Module2 before Module3, so Module2's `Public r As T`
        // allocates T before Module3 has resolved its bounds: the allocation runs Module3's declarations first
        // (awaited, never blocked on).
        await RunModulesAndClasses("Debug.Print LBound(r.a) & \" \" & UBound(r.a)\n",
            [("Module2", "Public r As T\nPublic s() As T\nFunction F() As T\nEnd Function"),
             ("Module3", "Public Const N = 3\nPublic Type T\na(1 To N) As Long\nEnd Type")], []);
        AssertDebugLog(["1 3"]);
    }

}
