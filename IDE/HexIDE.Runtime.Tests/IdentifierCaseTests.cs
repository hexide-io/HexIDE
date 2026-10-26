using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// VB6 identifiers are case-insensitive: <c>abc</c> after <c>Dim Abc</c> is the same variable (#811). Every expected
/// value and its type below was measured on vb6.exe; the probes and their output are in docs/vb6-fidelity-oracle/identifier-case.md,
/// "Identifier case".
/// </summary>
public class IdentifierCaseTests : BaseVBTestFixture
{
    [Fact]
    public async Task A_variable_written_in_another_case_is_the_same_variable()
    {
        // Without the fold this wrote a new implicit variable, and Abc kept its old value with no error.
        await Run("Dim Abc As Integer\nAbc = 1\nabc = 5\nDebug.Print Abc\n");
        AssertDebugLog([new Vb6Value(5)]);
    }

    [Fact]
    public async Task Option_Explicit_accepts_a_declared_name_in_another_case()
    {
        await Run("Option Explicit\nDim Abc As Integer\nabc = 5\nDebug.Print ABC\n");
        AssertDebugLog([new Vb6Value(5)]);
    }

    [Fact]
    public async Task A_Functions_return_value_assigned_in_another_case_is_returned()
    {
        // The worst case: foo = 7 inside Function Foo wrote a new local, so the caller got 0.
        await Run("Function Foo() As Integer\n    foo = 7\nEnd Function\nDebug.Print Foo()\n");
        AssertDebugLog([new Vb6Value(7)]);
    }

    [Fact]
    public async Task A_Functions_return_value_read_back_in_another_case_is_the_same_slot()
    {
        await Run("Function Bar() As Integer\n    BAR = 8\n    bar = bar + 1\nEnd Function\nDebug.Print bar()\n");
        AssertDebugLog([new Vb6Value(9)]);
    }

    [Fact]
    public async Task A_constant_is_read_in_another_case()
    {
        // Measured from a form against a .bas `Public Const`. On HexIDE's form path that reads Empty in every case
        // (#256), so this reads it within one module.
        await Run("Const KPI As Integer = 3\nDebug.Print kpi * 2\n");
        AssertDebugLog([new Vb6Value(6)]);
    }

    [Fact]
    public async Task Parameters_are_read_and_written_in_another_case()
    {
        await Run("""
            Sub Bump(ByVal Pa As Integer, ByRef Outv As Integer)
                pa = pa + 1
                OUTV = PA
            End Sub
            Dim o As Integer
            bump 10, o
            Debug.Print o

            """.Replace("\r\n", "\n"));
        AssertDebugLog([new Vb6Value(11)]);
    }

    [Fact]
    public async Task A_local_that_differs_only_in_case_shadows_the_module_variable()
    {
        // Measured: inside Shadow, xyz and XYZ are both the local; the module's Xyz keeps its value.
        await Run("""
            Dim Xyz As Integer
            Sub Shadow()
                Dim xyz As Integer
                xyz = 3
                Debug.Print XYZ
            End Sub
            Xyz = 1
            Shadow
            Debug.Print Xyz

            """.Replace("\r\n", "\n"));
        AssertDebugLog([new Vb6Value(3), new Vb6Value(1)]);
    }

    [Fact]
    public async Task A_module_its_procedure_and_its_variable_are_reached_in_another_case()
    {
        // Measured with the variable written from the form, which HexIDE cannot do yet in any case: reaching
        // another module's Public variable from a form is #256. So the module writes its own variable here, and the
        // call and the qualified read cross the modules in other cases.
        await RunModules("module2.setit\nDebug.Print MODULE2.abc\n",
            ("Module2", "Public Abc As Integer\nPublic Sub SetIt()\n    abc = 5\nEnd Sub\n"));
        AssertDebugLog([new Vb6Value(5)]);
    }

    [Fact]
    public async Task A_procedures_Const_shadows_a_module_name_in_any_case_and_leaves_it_alone()
    {
        // Measured: inside each Sub its Const is its own, and after it the module's Limit is still 1 and its Lim2
        // still 5. A Const used to update whatever slot its name found, which through a case-insensitive scope was
        // the module's, so the value outlived the call (#819). The same-case form was already wrong on main.
        await Run("""
            Dim Limit As Integer
            Const Lim2 As Integer = 5
            Sub OtherCase()
                Const LIMIT As Integer = 10
                Debug.Print LIMIT
                Debug.Print limit
            End Sub
            Sub SameCase()
                Const Limit As Integer = 20
                Debug.Print Limit
            End Sub
            Sub OverConstOtherCase()
                Const lim2 As Integer = 7
                Debug.Print lim2
            End Sub
            Sub OverConstSameCase()
                Const Lim2 As Integer = 8
                Debug.Print Lim2
            End Sub
            Limit = 1
            OtherCase
            Debug.Print Limit
            SameCase
            Debug.Print Limit
            OverConstOtherCase
            Debug.Print Lim2
            OverConstSameCase
            Debug.Print Lim2
            OtherCase
            Debug.Print Limit

            """.Replace("\r\n", "\n"));
        AssertDebugLog([
            new Vb6Value(10), new Vb6Value(10), new Vb6Value(1),
            new Vb6Value(20), new Vb6Value(1),
            new Vb6Value(7), new Vb6Value(5),
            new Vb6Value(8), new Vb6Value(5),
            new Vb6Value(10), new Vb6Value(10), new Vb6Value(1),
        ]);
    }

    [Theory]
    [InlineData("debug.print 1\n")]
    [InlineData("DEBUG.PRINT 1\n")]
    [InlineData("Debug.print 1\n")]
    public async Task Debug_Print_is_spelled_in_any_case(string code)
    {
        await Run(code);
        AssertDebugLog([new Vb6Value(1)]);
    }

    [Fact]
    public async Task Err_is_raised_and_read_in_another_case()
    {
        await Run("On Error Resume Next\nerr.raise 5\nDebug.Print ERR.NUMBER\nerr.clear\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(5L), new Vb6Value(0L)]);
    }
}
