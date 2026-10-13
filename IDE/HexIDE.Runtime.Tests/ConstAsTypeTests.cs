using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A Const's <c>As</c> type converts its value as a store into a variable of that type does, at module and procedure
/// level, and a value the type cannot hold is VB6's compile error. Every row was measured with <c>vb6.exe /make</c>, and
/// run where it built; the probes are in docs/vb6-fidelity-oracle.md, "A Const's As type". HexIDE stored the value as the
/// initialiser gave it, whatever the As clause said, and a module array's bounds now trust what a Const holds.
/// </summary>
public class ConstAsTypeTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("Const C As Integer = 2.5", "2", "Integer")]
    [InlineData("Const C As Long = 2.5", "2", "Long")]
    [InlineData("Const C As Long = 3.5", "4", "Long")]
    [InlineData("Const C As Long = 7 / 2", "4", "Long")]
    [InlineData("Const C As Integer = \"5\"", "5", "Integer")]
    [InlineData("Const C As Integer = True", "-1", "Integer")]
    [InlineData("Const C As String = 5", "5", "String")]
    [InlineData("Const C As Boolean = \"True\"", "True", "Boolean")]
    [InlineData("Const C As Currency = 1.23456", "1.2346", "Currency")]
    [InlineData("Const C As Single = 1.23456789", "1.234568", "Single")]
    [InlineData("Const C As Variant = 2.5", "2.5", "Double")]               // a Variant keeps what it is given
    public async Task A_module_Const_holds_its_value_as_its_As_type(string declaration, string text, string typeName)
    {
        await Run($"{declaration}\nDebug.Print CStr(C)\nDebug.Print TypeName(C)\n");

        AssertDebugLog([new Vb6Value(text), new Vb6Value(typeName)]);
    }

    [Fact]
    public async Task A_Date_Const_of_a_number_is_that_day()
    {
        // Measured: `Const C As Date = 1` is 31/12/1899 on an en-GB machine, a Date. Read as a number, so the test does
        // not depend on the machine's date format.
        await Run("Const C As Date = 1\nDebug.Print TypeName(C)\nDebug.Print CDbl(C)\n");

        AssertDebugLog([new Vb6Value("Date"), new Vb6Value(1.0)]);
    }

    [Theory]
    [InlineData("Const C As Date = \"1/2/2003\"\nDim x\nx = C")]
    [InlineData("Dim x As Date\nx = \"1/2/2003\"")]                                 // a store, the same conversion
    public async Task A_Date_from_a_date_string_is_that_date(string code)
    {
        // Measured on an en-GB machine: 01/02/2003, a Date, for the Const and the store alike. The string reads in the
        // program's locale, so the test asks only for the parts every locale agrees on: 2003, and a day and month of
        // 1 and 2 in either order. The store raised 13.
        await Run($"{code}\nDebug.Print TypeName(x)\nDebug.Print Year(x)\nDebug.Print Month(x) + Day(x)\n");

        AssertDebugLog([new Vb6Value("Date"), new Vb6Value(2003), new Vb6Value(3)]);
    }

    [Fact]
    public async Task A_store_of_a_string_that_is_no_date_into_a_Date_is_13()
    {
        // Measured: 13.
        await Run("Dim x As Date\nOn Error Resume Next\nx = \"abc\"\nDebug.Print Err.Number\n");

        AssertDebugLog([new Vb6Value(13L)]);
    }

    [Theory]
    [InlineData("Const C As Byte = 300")]
    [InlineData("Const C As Byte = -1")]
    [InlineData("Const C As Integer = 32768")]
    [InlineData("Const C As Currency = 922337203685478#")]
    [InlineData("Const C As Date = 3000000")]
    public async Task A_Const_its_type_cannot_hold_is_VB6s_Overflow(string declaration)
    {
        var run = () => Run($"{declaration}\nDebug.Print C\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Overflow*");
    }

    [Theory]
    [InlineData("Const C As Integer = \"x\"")]
    [InlineData("Const C As Date = \"abc\"")]
    public async Task A_Const_its_type_cannot_read_is_VB6s_Type_mismatch(string declaration)
    {
        // Measured: neither compiles.
        var run = () => Run($"{declaration}\nDebug.Print C\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Type mismatch*");
    }

    [Fact]
    public async Task A_procedures_Const_holds_its_value_as_its_As_type()
    {
        // Measured: 2, an Integer.
        await Run("Go\nSub Go()\n    Const P As Integer = 2.5\n    Debug.Print P\n    Debug.Print TypeName(P)\nEnd Sub\n");

        AssertDebugLog([new Vb6Value((short)2), new Vb6Value("Integer")]);
    }

    [Fact]
    public async Task A_procedures_Const_its_type_cannot_hold_is_VB6s_Overflow()
    {
        var run = () => Run("Go\nSub Go()\n    Const P As Byte = 300\n    Debug.Print P\nEnd Sub\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Overflow*");
    }

    [Theory]
    [InlineData("Const C As Integer = 2.5\nDim d(C + C) As Long", 0L, 4L)]          // it sized 0 To 5
    [InlineData("Const CL As Long = 7 / 2\nDim d(CL * 2) As Long", 0L, 8L)]         // 0 To 7
    [InlineData("Const B As Boolean = 5\nDim d(B To 1) As Long", -1L, 1L)]          // a run-time 9
    public async Task A_module_array_is_sized_from_what_a_typed_Const_holds(string declarations, long lower, long upper)
    {
        // Measured, each with /make and run.
        await Run($"{declarations}\nDebug.Print LBound(d)\nDebug.Print UBound(d)\n");

        AssertDebugLog([new Vb6Value(lower), new Vb6Value(upper)]);
    }
}
