using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// VB6 SP6, native and p-code agreeing: https://github.com/hexide-io/HexIDE/issues/4622.
public class UnsetFunctionReturnTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("Form")]
    [InlineData("VB.Form")]
    [InlineData("Control")]
    [InlineData("TextBox")]
    [InlineData("VB.TextBox")]
    [InlineData("PictureBox")]
    [InlineData("StdFont")]
    [InlineData("Object")]
    [InlineData("Collection")]
    [InlineData("Class1")]
    public async Task Unset_object_function_returns_Nothing(string type)
    {
        await RunClasses($"""
            Public Function Blank() As {type}
            End Function
            Debug.Print TypeName(Blank())
            Debug.Print VarType(Blank())
            Debug.Print (Blank() Is Nothing)
            """, ("Class1", "Public Value As Long\n"));
        AssertDebugLog(["Nothing", new Vb6Value(9L), true]);
    }

    [Theory]
    [InlineData("OLE_COLOR")]
    [InlineData("stdole.OLE_COLOR")]
    public async Task Unset_alias_function_returns_scalar_zero(string type)
    {
        await Run($"""
            Public Function Blank() As {type}
            End Function
            Debug.Print TypeName(Blank())
            Debug.Print VarType(Blank())
            Debug.Print Blank()
            """);
        AssertDebugLog(["Long", new Vb6Value(3L), new Vb6Value(0L)]);
    }

    [Fact]
    public async Task Unset_variant_function_returns_Empty()
    {
        await Run("Public Function Blank() As Variant\nEnd Function\nDebug.Print TypeName(Blank())\nDebug.Print VarType(Blank())\n");
        AssertDebugLog(["Empty", new Vb6Value(0L)]);
    }
}
