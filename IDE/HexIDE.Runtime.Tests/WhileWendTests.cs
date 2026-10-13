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

    private const string Counter =
        "Public N As Long\nPublic Sub Bump()\n    N = N + 1\nEnd Sub\nPublic Sub Add(ByVal x As Long)\n    N = N + x\nEnd Sub\n";

    [Theory]
    [InlineData("While i < 3\nk.Bump\ni = i + 1\nWend", 3L)]                              // obj.Method
    [InlineData("While i < 2\nk.Add 10\ni = i + 1\nWend", 20L)]                           // obj.Method arg
    [InlineData("While i < 2\nCall k.Add(10)\ni = i + 1\nWend", 20L)]                     // Call obj.Method(arg)
    [InlineData("While i < 2\nCall Helper(k)\ni = i + 1\nWend", 2L)]                      // Call Sub(obj)
    [InlineData("While i < 2\nWith k\n.Bump\n.Add 10\nEnd With\ni = i + 1\nWend", 22L)]   // a With block
    [InlineData("While i < 2\ni = i + 1\nk.Bump\nWend", 2L)]                              // after an assignment
    [InlineData("While i < 2: k.Bump: i = i + 1: Wend", 2L)]                              // one line, with colons
    [InlineData("While i < 2\nj = 0\nWhile j < 2\nk.Bump\nj = j + 1\nWend\ni = i + 1\nWend", 4L)]   // nested
    public async Task A_member_call_inside_While_is_one_statement(string loop, long expected)
    {
        // Measured, native and in the IDE: each gives the count shown. A bare `k.Bump` or `k.Add 10` was parsed as a call
        // to a procedure named k, then a With member call, so those five rows stopped with "Sub or Function not defined
        // (k)" (#882). The Call and With rows start with a keyword and ran before too; they are here as coverage.
        await RunClasses(
            $"Dim k As Counter, i As Integer, j As Integer\nSet k = New Counter\n{loop}\nDebug.Print k.N\n" +
            "Sub Helper(o As Counter)\n    o.Bump\nEnd Sub\n",
            ("Counter", Counter));
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Fact]
    public async Task Debug_Print_inside_While_prints()
    {
        await Run("Dim i As Integer\nWhile i < 2\nDebug.Print i\ni = i + 1\nWend\n");
        AssertDebugLog([new Vb6Value(0), new Vb6Value(1)]);
    }

    [Fact]
    public async Task Err_Clear_inside_While_clears_the_error_that_let_it_in()
    {
        // Measured, native and in the IDE: one pass.
        await Run("Dim c As Integer\nOn Error Resume Next\nErr.Raise 5\nWhile Err.Number <> 0\nErr.Clear\nc = c + 1\nWend\n" +
                  "Debug.Print c\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(1), new Vb6Value(0L)]);
    }

    [Fact]
    public async Task An_empty_While_runs()
    {
        await Run("While False\nWend\nDebug.Print \"ran\"\n");
        AssertDebugLog(["ran"]);
    }
}
