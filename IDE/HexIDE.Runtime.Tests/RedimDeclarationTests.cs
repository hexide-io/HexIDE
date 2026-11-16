using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Measured in VB6 SP6, native and p-code: https://github.com/hexide-io/HexIDE/issues/1495.
public class RedimDeclarationTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("", "q(2)", "q", "Variant()", 0, 2)]
    [InlineData("Option Explicit", "q(2)", "q", "Variant()", 0, 2)]
    [InlineData("Option Explicit", "q(2) As Long", "q", "Long()", 0, 2)]
    [InlineData("Option Explicit", "q(1 To 3) As String", "q", "String()", 1, 3)]
    // Also measured: vb6-fidelity-oracle/a-declaration-written-with-a-type-character.md.
    [InlineData("Option Explicit", "q%(2)", "q", "Integer()", 0, 2)]
    [InlineData("DefInt I", "i4(2)", "i4", "Integer()", 0, 2)]
    public async Task New_name_declares_a_local_array(string options, string declaration, string name,
        string type, int lower, int upper)
    {
        await Run($"""
            {options}
            Sub Main()
                ReDim {declaration}
                Debug.Print TypeName({name})
                Debug.Print "" & LBound({name})
                Debug.Print "" & UBound({name})
            End Sub
            Main
            """);
        AssertDebugLog([new(type), new(lower.ToString()), new(upper.ToString())]);
    }

    [Fact]
    public async Task Later_redim_clears_and_preserve_keeps_elements()
    {
        await Run("""
            Option Explicit
            Sub Main()
                ReDim q(2)
                q(2) = 7
                ReDim q(5)
                Debug.Print "" & UBound(q)
                Debug.Print IsEmpty(q(2))
                ReDim p(3)
                p(2) = 7
                ReDim Preserve p(4)
                Debug.Print p(2)
            End Sub
            Main
            """);
        AssertDebugLog([new("5"), new(true), new(7)]);
    }

    [Theory]
    [InlineData("", "ReDim Preserve q(2)", "*Variable not defined*")]
    [InlineData("Option Explicit", "ReDim Preserve q(2)", "*Variable not defined*")]
    [InlineData("Option Explicit", "ReDim q(2)\nReDim q(3) As Long", "*Can't change data types of array elements*")]
    [InlineData("Option Explicit", "Debug.Print q\nReDim q(2)", "*Variable not defined*")]
    public async Task Measured_compile_errors_surface_during_execution(string options, string body, string message)
    {
        var run = () => Run($"{options}\nSub Main()\n{body}\nEnd Sub\nMain\n");
        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage(message);
    }

    [Fact]
    public async Task Existing_module_array_is_resized_in_its_module()
    {
        await Run("""
            Option Explicit
            Dim mArr() As Long
            Sub Resize()
                ReDim mArr(2)
            End Sub
            Sub Main()
                Resize
                Debug.Print TypeName(mArr)
                Debug.Print "" & UBound(mArr)
            End Sub
            Main
            """);
        AssertDebugLog([new("Long()"), new("2")]);
    }
}
