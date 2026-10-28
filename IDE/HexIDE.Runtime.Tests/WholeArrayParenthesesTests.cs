using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class WholeArrayParenthesesTests : BaseVBTestFixture
{
    private string[] Log => debug.Select(v => v.Value?.ToString() ?? "").ToArray();

    [Theory]
    [InlineData("S arr()")]
    [InlineData("Call S(arr())")]
    public async Task A_declared_array_with_empty_parentheses_shares_with_an_array_parameter(string call)
    {
        await Run("Dim arr(2) As Long\n" + call + "\nDebug.Print arr(0)\n"
            + "Sub S(x() As Long)\nDebug.Print UBound(x)\nx(0) = x(0) + 1\nEnd Sub\n");

        Log.Should().Equal("2", "1");
    }

    [Theory]
    [InlineData("", "10")]
    [InlineData("ByVal ", "0")]
    public async Task A_Variant_parameter_shares_ByRef_and_copies_ByVal(string modifier, string expected)
    {
        await Run("Dim arr(2) As Long\nV arr()\nDebug.Print arr(0)\n"
            + "Sub V(" + modifier + "x As Variant)\nDebug.Print TypeName(x)\nx(0) = x(0) + 10\nEnd Sub\n");

        Log.Should().Equal("Long()", expected);
    }

    [Fact]
    public async Task A_function_argument_can_be_the_whole_array()
    {
        await Run("Dim arr(4) As Long\nDebug.Print F(arr())\n"
            + "Function F(x() As Long) As Long\nF = UBound(x)\nEnd Function\n");

        Log.Should().Equal("4");
    }

    [Fact]
    public async Task An_intrinsic_argument_can_be_the_whole_array()
    {
        await Run("Dim arr(3) As Long\nDebug.Print UBound(arr())\n");

        Log.Should().Equal("3");
    }

    [Fact]
    public async Task Let_into_a_Variant_copies_the_whole_array()
    {
        await Run("Dim arr(2) As Long, w\nw = arr()\n"
            + "Debug.Print TypeName(w)\nDebug.Print UBound(w)\nw(0) = 10\nDebug.Print arr(0)\n");

        Log.Should().Equal("Long()", "2", "0");
    }

    [Fact]
    public async Task Empty_parentheses_on_a_Variant_array_are_a_compile_error_at_the_statement()
    {
        var run = () => Run("Dim v As Variant\nv = Array(1, 2)\nDebug.Print \"before\"\nVee v()\n"
            + "Sub Vee(x As Variant)\nEnd Sub\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>())
            .WithMessage("*Expected Sub, Function, or Property*");
        Log.Should().Equal("before");
    }
}
