using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class LenTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("Integer", "5", 2)]
    [InlineData("Long", "5", 4)]
    [InlineData("Single", "5", 4)]
    [InlineData("Double", "5", 8)]
    [InlineData("Currency", "5", 8)]
    [InlineData("Date", "#1/1/2000#", 8)]
    [InlineData("Boolean", "True", 2)]
    public async Task A_typed_variable_returns_its_byte_size(string type, string value, long size)
    {
        await Run($"Dim x As {type}\nx = {value}\nDebug.Print Len(x)\n");
        AssertDebugLog([new Vb6Value(size)]);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("i + 0")]
    [InlineData("1 / 3")]
    [InlineData("CSng(1) / 3")]
    public async Task A_numeric_expression_requires_a_variable(string expression)
    {
        var act = async () => await Run($"Dim i As Integer\nDebug.Print Len({expression})\n");
        (await act.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should()
            .Contain("Variable required - can't assign to this expression");
    }

    [Fact]
    public async Task Strings_and_Variants_keep_their_text_lengths()
    {
        await Run("Dim b As Byte, s As String, v As Variant\nb = 5\ns = \"12345\"\nDebug.Print Len(b)\nDebug.Print Len(s)\nDebug.Print Len(v)\nv = 5\nDebug.Print Len(v)\nv = 12345\nDebug.Print Len(v)\n");
        AssertDebugLog([new Vb6Value(1L), new Vb6Value(5L), new Vb6Value(0L), new Vb6Value(1L), new Vb6Value(5L)]);
    }
}
