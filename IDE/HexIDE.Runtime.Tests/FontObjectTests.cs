namespace HexIDE.Runtime.Tests;

public class FontObjectTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("Dim f As Object\nSet f = New StdFont")]
    [InlineData("Dim f As Object\nSet f = New stdole.StdFont")]
    public async Task Standalone_StdFont_refuses_without_inventing_initial_values(string code)
    {
        Func<Task> run = () => Run(code);
        (await run.Should().ThrowAsync<NotImplementedException>()).Which.Message.Should().Contain("StdFont");
    }
}
