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
    [InlineData("Charset = 2", "font substitution")]
    [InlineData("Size = \"10\"", "StdFont.Size")]
    [InlineData("Weight = 500", "StdFont.Weight")]
    [InlineData("Bold = False", "StdFont.Bold")]
    public async Task Standalone_StdFont_keeps_explicit_refusals_for_unsupported_assignments(string assignment, string reason)
    {
        // #4781 permits a refusal for Charset even at the measured value; other conversions are unmeasured.
        Func<Task> run = () => Run("Dim f As Object\nSet f = New StdFont\nf." + assignment);
        var error = (await run.Should().ThrowAsync<NotImplementedException>()).Which;
        error.Message.Should().Contain(reason).And.Contain("#4781");
    }
}
