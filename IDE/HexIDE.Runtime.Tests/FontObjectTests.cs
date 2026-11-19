namespace HexIDE.Runtime.Tests;

public class FontObjectTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("Dim f As Object\nSet f = New StdFont")]
    [InlineData("Dim f As Object\nSet f = New stdole.StdFont")]
    public async Task Standalone_StdFont_starts_with_the_measured_name(string code)
    {
        // VB6 SP6 native/p-code: https://github.com/hexide-io/HexIDE/issues/4781.
        await Run(code + "\nDebug.Print f.Name");
        AssertDebugLog(["MS Sans Serif"]);
    }

    [Theory]
    [InlineData("Charset = 3", "StdFont.Charset")]
    [InlineData("Weight = 500", "StdFont.Weight")]
    public async Task Standalone_StdFont_keeps_explicit_refusals_for_unsupported_assignments(string assignment, string reason)
    {
        // Values outside the measured #4807 rows remain refused.
        Func<Task> run = () => Run("Dim f As Object\nSet f = New StdFont\nf." + assignment);
        var error = (await run.Should().ThrowAsync<NotImplementedException>()).Which;
        error.Message.Should().Contain(reason).And.Contain("#4807");
    }
}
