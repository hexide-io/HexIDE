using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class BoundaryConstructTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("5", false)]
    [InlineData("", false)]
    [InlineData("5", true)]
    [InlineData("", true)]
    public async Task UdtArray_IsRefusedAtDeclaration(string bounds, bool local)
    {
        var declaration = $"Dim a({bounds}) As Employee\n";
        var code = "Type Employee\nName As String\nEnd Type\n"
            + (local ? "Sub Test()\n" + declaration + "Debug.Print 1\nEnd Sub\nTest\n"
                     : declaration + "Debug.Print 1\n");

        var act = async () => await Run(code);
        (await act.Should().ThrowAsync<NotImplementedException>()).Which.Message
            .Should().Be("An array of the user-defined type Employee is not supported (#178)");
        debug.Should().BeEmpty();
    }

    [Theory]
    [InlineData("a(0)!key")]
    public async Task BangAssignment_NamesTheUnsupportedConstruct(string target)
    {
        var act = async () => await Run($"Dim o As Object\nDim a(0)\n{target} = 1\n");
        (await act.Should().ThrowAsync<NotImplementedException>()).Which.Message
            .Should().Be("Bang access (!) in an assignment target is not supported: " + target);
    }
}
