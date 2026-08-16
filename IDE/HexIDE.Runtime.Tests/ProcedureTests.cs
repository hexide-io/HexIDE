using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// User procedures (Sub/Function) — Phase 1 of the interpreter-core build-out: declaration, parameter
/// binding (ByRef-by-default via shared slots, ByVal copy, Optional + defaults), return values, recursion,
/// and dispatch from statements and expressions.
/// </summary>
public class ProcedureTests : BaseVBTestFixture
{
    [Fact]
    public async Task ModuleWithAFunction_Declares_WithoutThrowing()
    {
        await Run("Function Sq(n As Integer) As Integer\n    Sq = n * n\nEnd Function\n\nDebug.Print 1\n");
        AssertDebugLog([new Vb6Value(1)]);
    }

    [Fact]
    public async Task BareSubCall_RunsTheBody()
    {
        await Run("Sub Greet()\n    Debug.Print \"hi\"\nEnd Sub\n\nGreet\n");
        AssertDebugLog([new Vb6Value("hi")]);
    }

    [Fact]
    public async Task CallKeyword_PassesAByValArgument()
    {
        await Run("Sub PrintIt(ByVal n As Integer)\n    Debug.Print n\nEnd Sub\n\nCall PrintIt(42)\n");
        AssertDebugLog([new Vb6Value(42)]);
    }

    [Fact]
    public async Task Function_ReturnsViaItsName_UsedInAnExpression()
    {
        await Run("Function Sq(ByVal n As Integer) As Integer\n    Sq = n * n\nEnd Function\n\nDebug.Print Sq(5)\n");
        AssertDebugLog([new Vb6Value(25)]);
    }

    [Fact]
    public async Task Recursion_FreshScopePerCall()
    {
        await Run(
            "Function Fact(ByVal n As Integer) As Integer\n" +
            "    If n <= 1 Then\n        Fact = 1\n    Else\n        Fact = n * Fact(n - 1)\n    End If\n" +
            "End Function\n\nDebug.Print Fact(5)\n");
        AssertDebugLog([new Vb6Value(120)]);
    }

    [Fact]
    public async Task ByRef_IsTheDefault_MutationVisibleToCaller()
    {
        await Run(
            "Sub Inc(x As Integer)\n    x = x + 1\nEnd Sub\n\n" +
            "Dim a As Integer\na = 10\nInc a\nDebug.Print a\n");
        AssertDebugLog([new Vb6Value(11)]);
    }

    [Fact]
    public async Task ByVal_DoesNotMutateTheCaller()
    {
        await Run(
            "Sub Bump(ByVal x As Integer)\n    x = x + 1\nEnd Sub\n\n" +
            "Dim b As Integer\nb = 10\nBump b\nDebug.Print b\n");
        AssertDebugLog([new Vb6Value(10)]);
    }

    [Fact]
    public async Task ExtraParens_ForceByVal()
    {
        await Run(
            "Sub Bump2(x As Integer)\n    x = x + 1\nEnd Sub\n\n" +
            "Dim c As Integer\nc = 10\nBump2 (c)\nDebug.Print c\n");
        AssertDebugLog([new Vb6Value(10)]);
    }

    [Fact]
    public async Task Optional_WithDefault_OmittedThenSupplied()
    {
        await Run(
            "Function AddN(ByVal a As Integer, Optional ByVal b As Integer = 100) As Integer\n" +
            "    AddN = a + b\nEnd Function\n\nDebug.Print AddN(1)\nDebug.Print AddN(1, 2)\n");
        AssertDebugLog([new Vb6Value(101), new Vb6Value(3)]);
    }

    [Fact]
    public async Task ZeroArgFunction_CalledBareInAnExpression()
    {
        await Run("Function Answer() As Integer\n    Answer = 42\nEnd Function\n\nDebug.Print Answer\n");
        AssertDebugLog([new Vb6Value(42)]);
    }

    [Fact]
    public async Task Function_DefaultReturn_WhenNeverAssigned()
    {
        await Run("Function Zero() As Integer\nEnd Function\n\nDebug.Print Zero()\n");
        AssertDebugLog([new Vb6Value(0)]);
    }

    [Fact]
    public async Task ExitFunction_ShortCircuits()
    {
        await Run(
            "Function Pick(ByVal n As Integer) As Integer\n" +
            "    If n > 0 Then\n        Pick = 1\n        Exit Function\n    End If\n    Pick = -1\n" +
            "End Function\n\nDebug.Print Pick(5)\n");
        AssertDebugLog([new Vb6Value(1)]);
    }

    [Fact]
    public async Task Sub_ReadsAndWritesAModuleGlobal()
    {
        await Run("Dim g As Integer\nSub SetG()\n    g = 7\nEnd Sub\n\nSetG\nDebug.Print g\n");
        AssertDebugLog([new Vb6Value(7)]);
    }

    [Fact]
    public async Task CallResolution_IsCaseInsensitive()
    {
        await Run("Sub SayHi()\n    Debug.Print \"yo\"\nEnd Sub\n\nsayhi\n");
        AssertDebugLog([new Vb6Value("yo")]);
    }

    [Fact]
    public async Task Builtins_StillResolve_NoDispatchRegression()
    {
        await Run("Debug.Print UCase(\"hi\")\n");
        AssertDebugLog([new Vb6Value("HI")]);
    }
}
