using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Phase 4.1 — While…Wend (a pre-tested loop with no Exit While; Exit Sub/Function propagates out).
/// </summary>
public class WhileWendTests : BaseVBTestFixture
{
    [Fact]
    public async Task Loop_Accumulates()
    {
        await Run(
            "Dim i\n" +
            "Dim total\n" +
            "i = 1\n" +
            "total = 0\n" +
            "While i <= 5\n" +
            "total = total + i\n" +
            "i = i + 1\n" +
            "Wend\n" +
            "Debug.Print total\n");
        AssertDebugLog([15]);
    }

    [Fact]
    public async Task ZeroIterations_WhenConditionFalseAtEntry()
    {
        await Run(
            "Dim n\n" +
            "n = 10\n" +
            "While n < 5\n" +
            "n = n + 1\n" +
            "Wend\n" +
            "Debug.Print n\n");
        AssertDebugLog([10]);
    }

    [Fact]
    public async Task ExitSub_FromInsideWhile_PropagatesOut()
    {
        await Run(
            "Sub Go()\n" +
            "Dim i\n" +
            "i = 0\n" +
            "While i < 100\n" +
            "i = i + 1\n" +
            "If i = 3 Then\n" +
            "Debug.Print i\n" +
            "Exit Sub\n" +
            "End If\n" +
            "Wend\n" +
            "Debug.Print 999\n" +
            "End Sub\n" +
            "Go\n");
        AssertDebugLog([3]);   // 999 never printed
    }
}
