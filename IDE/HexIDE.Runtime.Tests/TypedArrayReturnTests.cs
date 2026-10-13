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
}
