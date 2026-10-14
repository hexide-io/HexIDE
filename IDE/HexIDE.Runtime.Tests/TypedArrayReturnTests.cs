using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A Function or Property Get declared to return an array, <c>As Long()</c>, returns that array (#975). Measured on vb6.exe,
/// native and p-code alike, with the probe in docs/vb6-fidelity-oracle.md, "A Function that returns an array". HexIDE read the
/// declaration as its element type, so the array the body stored was narrowed into a Long on the way out, and came back 0
/// with no error. Whether the caller gets a copy is #970's, so these read the result and do not write through it.
/// </summary>
public class TypedArrayReturnTests : BaseVBTestFixture
{
    private const string Procedures = """
        Function RetFixed() As Long()
            Dim a(3) As Long
            a(1) = 7
            RetFixed = a
        End Function
        Function RetEmpty() As Long()
        End Function
        Function RetStr() As String()
            RetStr = Split("a,b,c", ",")
        End Function
        Function RetVar() As Variant()
            Dim v(1) As Variant
            v(0) = "x"
            v(1) = 2
            RetVar = v
        End Function
        """;

    [Theory]
    [InlineData("Dim x\nx = RetFixed()\nDebug.Print TypeName(x) & \" \" & UBound(x) & \" \" & x(1)", "Long() 3 7")]
    [InlineData("Debug.Print TypeName(RetFixed())", "Long()")]
    [InlineData("Dim d() As Long\nd = RetFixed()\nDebug.Print UBound(d) & \" \" & d(1)", "3 7")]
    [InlineData("Debug.Print TypeName(RetEmpty())", "Long()")]
    [InlineData("Dim x\nx = RetStr()\nDebug.Print TypeName(x) & \" \" & UBound(x) & \" \" & x(2)", "String() 2 c")]
    [InlineData("Dim x\nx = RetVar()\nDebug.Print TypeName(x) & \" \" & x(0) & \" \" & TypeName(x(1))", "Variant() x Integer")]
    public async Task A_Function_declared_to_return_an_array_returns_it(string use, string expected)
    {
        await Run(use + "\n" + Procedures);

        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Fact]
    public async Task A_class_Property_Get_declared_to_return_an_array_returns_it()
    {
        // Measured, native and p-code. A standard module's Property Get is not reached by name at all, which is #977.
        await RunClasses("Dim o As Foo, x\nSet o = New Foo\nx = o.Items\nDebug.Print TypeName(x) & \" \" & UBound(x) & \" \" & x(1)\n",
            ("Foo", "Public Property Get Items() As Long()\n    Dim a(3) As Long\n    a(1) = 7\n    Items = a\nEnd Property\n"));

        AssertDebugLog([new Vb6Value("Long() 3 7")]);
    }

    [Fact]
    public async Task A_Function_that_never_assigns_its_array_returns_an_empty_one()
    {
        // Measured: UBound of it raises 9, as of any array that was never dimensioned.
        var run = () => Run("Dim x\nx = RetEmpty()\nDebug.Print UBound(x)\n" + Procedures);

        (await run.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(9);
    }

    // #986. Every row was measured, native and p-code agreeing, and is generated from those probes and their logs (the
    // oracle's "A Function that returns an array" has the tables). `Shape` writes a value's TypeName and bounds, "-"
    // where a bound raises.
    private const string Shape = """
        Function Shape(x) As String
            On Error Resume Next
            Dim lo, hi
            lo = "-": hi = "-"
            lo = LBound(x): hi = UBound(x)
            Shape = TypeName(x) & " " & lo & " To " & hi
        End Function
        """;

    [Theory]
    [InlineData("Function FA() As Integer()\n    FA = Array(1, 2)\nEnd Function\n", "FA()", "err 13, x Empty - To -")]
    [InlineData("Function FB() As Integer()\n    Dim ia(1 To 3) As Integer\n    ia(2) = 7\n    FB = ia\nEnd Function\n", "FB()", "err 0, x Integer() 1 To 3")]
    [InlineData("Function FC() As Integer()\n    Dim v, ia(1 To 3) As Integer\n    ia(2) = 7\n    v = ia\n    FC = v\nEnd Function\n", "FC()", "err 0, x Integer() 1 To 3")]
    [InlineData("Function FD() As Integer()\n    Dim v, la(1) As Long\n    v = la\n    FD = v\nEnd Function\n", "FD()", "err 13, x Empty - To -")]
    [InlineData("Function FE() As Integer()\n    Dim v\n    FE = v\nEnd Function\n", "FE()", "err 13, x Empty - To -")]
    [InlineData("Function FF() As Integer()\n    Dim v\n    v = 5\n    FF = v\nEnd Function\n", "FF()", "err 13, x Empty - To -")]
    [InlineData("Function FG() As Variant()\n    FG = Array(1, 2)\nEnd Function\n", "FG()", "err 0, x Variant() 0 To 1")]
    [InlineData("Function FH() As Variant()\n    Dim v, ia(1) As Integer\n    v = ia\n    FH = v\nEnd Function\n", "FH()", "err 13, x Empty - To -")]
    [InlineData("Function FI() As String()\n    FI = Split(\"a,b\", \",\")\nEnd Function\n", "FI()", "err 0, x String() 0 To 1")]
    [InlineData("Function FJ() As Integer()\n    Dim d() As Integer\n    FJ = d\nEnd Function\n", "FJ()", "err 0, x Integer() - To -")]
    [InlineData("Function FK() As Integer()\n    Dim v\n    v = Array(1, 2)\n    FK = v\nEnd Function\n", "FK()", "err 13, x Empty - To -")]
    public async Task A_store_into_a_typed_array_return_takes_only_an_array_of_its_type(string procedure, string call, string expected)
    {
        await Run($"On Error Resume Next\nDim x\nx = {call}\nDebug.Print \"err \" & Err.Number & \", x \" & Shape(x)\n" + procedure + Shape);

        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("Function F() As Integer()\n    F = Split(\"1,2\", \",\")\nEnd Function\n", "err 13, x Empty - To -")]
    [InlineData("Function F() As Long()\n    F = Array(1, 2)\nEnd Function\n", "err 13, x Empty - To -")]
    public async Task A_call_that_returns_a_Variant_stored_into_a_typed_array_return_is_13(string function, string expected)
    {
        await Run("On Error Resume Next\nDim x\nx = F()\nDebug.Print \"err \" & Err.Number & \", x \" & Shape(x)\n" + function + Shape);

        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("Function F() As Long()\n    F = 5\nEnd Function\n")]
    [InlineData("Function F() As Integer()\n    Dim la(1) As Long\n    F = la\nEnd Function\n")]
    [InlineData("Function F() As Integer()\n    Dim va(1) As Variant\n    F = va\nEnd Function\n")]
    [InlineData("Function F() As Variant()\n    Dim ia(1) As Integer\n    F = ia\nEnd Function\n")]
    public async Task A_store_VB6_refuses_while_compiling_is_its_compile_error_as_the_statement_runs(string function)
    {
        // A scalar, or an array variable declared with another element type: VB6's "Can't assign to array" at the store.
        var run = () => Run("On Error Resume Next\nDim x\nx = F()\n" + function + Shape);

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Can't assign to array*");
    }

    [Fact]
    public async Task A_typed_array_return_keeps_its_bounds_through_a_typed_array_variable()
    {
        await Run("Dim d() As Integer\nd = FB()\nDebug.Print Shape(d)\n" + "Function FB() As Integer()\n    Dim ia(1 To 3) As Integer\n    ia(2) = 7\n    FB = ia\nEnd Function\n" + Shape);

        AssertDebugLog([new Vb6Value("Integer() 1 To 3")]);
    }

    [Fact]
    public async Task A_class_Property_Get_declared_to_return_a_typed_array_is_checked_too()
    {
        // Measured through a class, because HexIDE does not reach a standard module's Property Get by name (#977).
        await RunClasses("On Error Resume Next\nDim o As Foo, x\nSet o = New Foo\nx = o.PG\nDebug.Print \"err \" & Err.Number & \", TypeName(x) \" & TypeName(x)\n"
            + "Err.Clear\nx = Empty\nx = o.PB\nDebug.Print \"err \" & Err.Number & \", TypeName(x) \" & TypeName(x) & \" \" & LBound(x) & \" To \" & UBound(x)\n",
            ("Foo", "Public Property Get PG() As Integer()\n    PG = Array(1, 2)\nEnd Property\n"
                + "Public Property Get PB() As Integer()\n    Dim ia(1 To 3) As Integer\n    PB = ia\nEnd Property\n"));

        AssertDebugLog([new Vb6Value("err 13, TypeName(x) Empty"), new Vb6Value("err 0, TypeName(x) Integer() 1 To 3")]);
    }
}
