using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Measured native and p-code: docs/vb6-fidelity-oracle/an-undeclared-name-before-a-dot.md (#3780).
public class UndeclaredMemberTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("v = Nope.F()")]
    [InlineData("v = Nope.F(1)")]
    [InlineData("v = Nope.P")]
    [InlineData("Set o = Nope.P")]
    [InlineData("v = Nope.P.Q")]
    [InlineData("v = Nope!Key")]
    [InlineData("Nope.P = 5")]
    [InlineData("Nope.F")]
    [InlineData("Call Nope.F")]
    [InlineData("With Nope\nv = .P\nEnd With")]
    public async Task UndeclaredReceiverRaisesTrappableObjectRequired(string statement)
    {
        await Run("On Error Resume Next\nv = \"unset\"\n" + statement + "\n" +
            "Debug.Print CStr(Err.Number)\nDebug.Print Err.Description\nDebug.Print v\n" +
            "Err.Clear\nDebug.Print TypeName(Nope)\nDebug.Print \"next\"");
        AssertDebugLog(["424", "Object required", "unset", "Empty", "next"]);
    }

    [Theory]
    [InlineData("v = Nope.F()")]
    [InlineData("Nope.F")]
    public async Task OptionExplicitStillRefusesUndeclaredReceiver(string statement)
    {
        var run = () => Run("Option Explicit\nDim v\nOn Error Resume Next\n" + statement);
        await run.Should().ThrowAsync<VBVariableNotDefinedException>().WithMessage("*Nope*");
    }

    [Fact]
    public async Task RealModuleStillWins()
    {
        await RunModules("Debug.Print CStr(Real.F())", ("Real", "Public Function F() As Long\nF = 7\nEnd Function"));
        AssertDebugLog(["7"]);
    }

    [Theory]
    [InlineData("Printer.P", "Printer")]
    [InlineData("LoadResData.P", "LoadResData")]
    public async Task MissingIntrinsicStillRefuses(string expression, string name)
    {
        var run = () => Run("On Error Resume Next\nv = " + expression);
        await run.Should().ThrowAsync<NotImplementedException>().WithMessage("*" + name + "*implement*");
    }
}
