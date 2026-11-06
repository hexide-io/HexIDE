namespace HexIDE.Runtime.Tests;

public class ErrHelpTests : BaseVBTestFixture
{
    // #2527: vb6.exe SP6, native and p-code, en-GB oracle machine.
    [Theory]
    [InlineData("", "[]|0|Long|0|Long")]
    [InlineData("On Error Resume Next\nErr.Raise 5, \"src\", \"desc\", \"help.hlp\", 77", "[help.hlp]|77|Long|0|Long")]
    [InlineData("On Error Resume Next\nErr.Raise 5, , , \"help.hlp\", 77\nErr.Clear", "[]|0|Long|0|Long")]
    [InlineData("On Error Resume Next\nErr.Raise 5", "[]|1000005|Long|0|Long")]
    [InlineData("On Error Resume Next\nErr.Raise 1000", "[]|1000095|Long|0|Long")]
    [InlineData("On Error Resume Next\nDim x\nx = 1 / 0", "[]|1000011|Long|0|Long")]
    [InlineData("On Error Resume Next\nDim x As Integer\nx = 32767\nx = x + 1", "[]|1000006|Long|0|Long")]
    [InlineData("Err.HelpFile = \"mine.chm\"\nErr.HelpContext = 12", "[mine.chm]|12|Long|0|Long")]
    [InlineData("Err.HelpContext = \"13\"", "[]|13|Long|0|Long")]
    [InlineData("Err.HelpContext = 70000", "[]|70000|Long|0|Long")]
    [InlineData("On Error Resume Next\nErr.Raise 5, , , \"h.hlp\"", "[h.hlp]|1000005|Long|0|Long")]
    [InlineData("On Error Resume Next\nErr.Raise 5, , , , 9", "[]|9|Long|0|Long")]
    public async Task HelpProperties_MatchMeasuredRows(string setup, string expected)
    {
        await Run(setup + "\nDebug.Print \"[\" & Err.HelpFile & \"]|\" & Err.HelpContext & \"|\" & TypeName(Err.HelpContext) & \"|\" & Err.LastDllError & \"|\" & TypeName(Err.LastDllError)\n");
        AssertDebugLog([expected]);
    }

    [Fact]
    public async Task LastDllError_IsReadOnly()
    {
        var action = async () => await Run("Err.LastDllError = 3\n");
        await action.Should().ThrowAsync<HexIDE.Runtime.Interpreter.VBCompileErrorException>()
            .WithMessage("*Can't assign to read-only property*");
    }
    [Theory]
    [InlineData("Err.HelpContext = 1.5")]
    [InlineData("Err.HelpContext = Null")]
    [InlineData("Err.HelpContext = \"abc\"")]
    [InlineData("Err.HelpFile = 12")]
    [InlineData("Err.Raise 5, , , 12")]
    [InlineData("Err.Raise 5, , , , 1.5")]
    public async Task UnmeasuredHelpConversions_AreExplicitlyRefused(string statement)
    {
        var action = async () => await Run(statement + "\n");
        await action.Should().ThrowAsync<NotImplementedException>().WithMessage("*not measured*#2527*");
    }
}
