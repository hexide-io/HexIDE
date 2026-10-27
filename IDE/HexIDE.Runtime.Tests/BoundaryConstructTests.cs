using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class BoundaryConstructTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("a(0)!key")]
    public async Task BangAssignment_NamesTheUnsupportedConstruct(string target)
    {
        var act = async () => await Run($"Dim o As Object\nDim a(0)\n{target} = 1\n");
        (await act.Should().ThrowAsync<NotImplementedException>()).Which.Message
            .Should().Be("Bang access (!) in an assignment target is not supported: " + target);
    }
}
