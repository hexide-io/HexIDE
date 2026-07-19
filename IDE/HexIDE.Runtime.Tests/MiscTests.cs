using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class MiscTests : BaseVBTestFixture
{
    [Fact]
    public async Task DebugPrint_WithStringLiteral_ShouldOutputCorrectString()
    {
        await Run(@"Debug.Print ""Hello""");
        AssertDebugLog(["Hello"]);
    }

    [Fact]
    public async Task VariableAssignment_ShouldOutputVariableValue()
    {
        await Run(@"
        Dim x
        Let x = 42
        Debug.Print x
    ");
        AssertDebugLog([new Vb6Value(42)]);
    }

    [Fact]
    public async Task IfStatement_ShouldOutputBasedOnCondition()
    {
        await Run(@"
        Dim x
        Let x = 10
        If x = 10 Then
            Debug.Print ""Ten""
        Else
            Debug.Print ""Not Ten""
        End If
    ");
        AssertDebugLog(["Ten"]);
    }

    [Fact]
    public async Task IfStatement_ShouldOutputBasedOnConditionNotEqual()
    {
        await Run(@"
        Dim x
        Let x = 10
        If x <> 10 Then
            Debug.Print ""Not Ten""
        Else
            Debug.Print ""Ten""
        End If
    ");
        AssertDebugLog(["Ten"]);
    }
}
