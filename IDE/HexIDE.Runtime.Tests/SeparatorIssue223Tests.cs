using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class SeparatorIssue223Tests : BaseVBTestFixture
{
    [Theory]
    [InlineData(": Debug.Print \"A\"\n", "A")]
    [InlineData(":  :  : Debug.Print \"A\"\n", "A")]
    [InlineData(": Debug.Print \"A\"\nDebug.Print \"B\"\n", "A", "B")]
    [InlineData("If True Then\nDebug.Print \"T\"\nFin: End If\nDebug.Print \"AFTER\"\n", "T", "AFTER")]
    [InlineData("Dim x As Boolean\nx = True\nIf x Then\nDebug.Print \"A\"\nSkip: End If\n", "A")]
    [InlineData("If True Then:\nDebug.Print \"A\"\n", "A")]
    public async Task MeasuredIssueCases(string code, params string[] expected)
    {
        await Run(code);
        AssertDebugLog(expected.Select(s => new Vb6Value(s)).ToList());
    }

    [Fact]
    public async Task AnElseColonStillStartsTheElseBranch()
    {
        await Run("If False Then\nDebug.Print \"A\"\nElse : Debug.Print \"B\" : Debug.Print \"C\"\nEnd If\n");
        AssertDebugLog([new Vb6Value("B"), new Vb6Value("C")]);
    }
}
