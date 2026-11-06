using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Operator precedence and associativity (#1074), every row measured on vb6.exe: docs/vb6-fidelity-oracle/operator-precedence.md, "Operator
/// precedence". VB6's ladder is <c>^</c>, negation, <c>*</c> and <c>/</c>, <c>\</c>, <c>Mod</c>, <c>+</c> and <c>-</c>,
/// <c>&amp;</c>, every comparison (<c>Like</c> and <c>Is</c> included), <c>Not</c>, <c>And</c>, <c>Or</c>, <c>Xor</c>,
/// <c>Eqv</c>, <c>Imp</c>, and operators of one level are taken left to right. The interpreter's grammar had given each
/// operator a level of its own, so <c>10 - 2 + 3</c> was 5.
/// </summary>
public class OperatorPrecedenceTests : BaseVBTestFixture
{
    [Theory]
    // Within a level, left to right.
    [InlineData("10 - 2 + 3", "11|Integer")]
    [InlineData("1 - -1 + 1", "3|Integer")]
    [InlineData("8 - 3 - 2", "3|Integer")]
    [InlineData("6 / 2 * 3", "9|Double")]
    [InlineData("2 * 3 / 4", "1.5|Double")]
    [InlineData("100 / 10 / 2", "5|Double")]
    [InlineData("12 / 2 / 3", "2|Double")]
    [InlineData("20 \\ 3 \\ 2", "3|Integer")]
    [InlineData("9 Mod 5 Mod 3", "1|Integer")]
    [InlineData("2 ^ 3 ^ 2", "64|Double")]
    [InlineData("1 < 2 = True", "True|Boolean")]
    [InlineData("3 = 3 < 4", "True|Boolean")]
    [InlineData("3 > 2 > 1", "False|Boolean")]
    [InlineData("Nothing Is Nothing = True", "True|Boolean")]
    [InlineData("False Imp False Imp False", "False|Boolean")]
    // Each pair of adjacent levels, the higher first.
    [InlineData("-(2) ^ 2", "-4|Double")]
    [InlineData("2 * 3 ^ 2", "18|Double")]
    [InlineData("2 ^ 3 * 2", "16|Double")]
    [InlineData("5 * 5 \\ 2", "12|Integer")]
    [InlineData("10 \\ 2 * 3", "1|Integer")]
    [InlineData("2 * 6 \\ 4 * 2", "1|Integer")]
    [InlineData("7 / 2 \\ 1", "4|Long")]
    [InlineData("6 \\ 4 / 2", "3|Long")]
    [InlineData("7 Mod 4 \\ 2", "1|Integer")]
    [InlineData("8 \\ 3 Mod 2", "0|Integer")]
    [InlineData("7 Mod 3 * 2", "1|Integer")]
    [InlineData("2 * 3 Mod 4", "2|Integer")]
    [InlineData("2 + 3 Mod 2", "3|Integer")]
    [InlineData("5 - 7 Mod 4", "2|Integer")]
    [InlineData("9 Mod 4 + 1", "2|Integer")]
    [InlineData("1 + 2 & 3", "33|String")]
    [InlineData("5 - 3 & 1", "21|String")]
    [InlineData("\"a\" & 1 + 2", "a3|String")]
    [InlineData("\"a\" & \"b\" = \"ab\"", "True|Boolean")]
    [InlineData("1 + 2 = 3 And 4", "4|Integer")]
    [InlineData("Not 1 = 2", "True|Boolean")]
    [InlineData("Not 0 = 1", "True|Boolean")]
    [InlineData("Not False And False", "False|Boolean")]
    [InlineData("1 = 1 And 2 = 2", "True|Boolean")]
    [InlineData("True Or True And False", "True|Boolean")]
    [InlineData("True Xor True Or True", "False|Boolean")]
    [InlineData("False Imp False Eqv False", "True|Boolean")]
    public async Task An_expression_groups_as_vb6_exe_groups_it(string expression, string expected)
    {
        await Run($"Dim v\nv = {expression}\nDebug.Print CStr(v) & \"|\" & TypeName(v)\n");

        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("True = -1", true)]
    [InlineData("True < 0", true)]
    [InlineData("False = 0", true)]
    [InlineData("True = 1", false)]
    [InlineData("True > -2", true)]
    [InlineData("True = -1#", true)]
    [InlineData("False < CLng(1)", true)]
    [InlineData("True <> 0", true)]
    [InlineData("CDbl(-1) >= True", true)]
    // A Byte reads True as 255, its bits.
    [InlineData("CByte(1) > True", false)]
    public async Task A_Boolean_beside_a_number_compares_as_that_number(string expression, bool expected)
    {
        await Run($"Debug.Print {expression}\n");

        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Fact]
    public async Task An_Enum_member_folds_with_the_same_grouping()
    {
        // The pre-pass folds an Enum member's value itself, apart from the expression executor.
        await Run("Public Enum EX\n    a = 10 - 2 + 3\n    b = 5 * 5 \\ 2\nEnd Enum\nDebug.Print CStr(EX.a) & \"/\" & CStr(EX.b)\n");

        AssertDebugLog([new Vb6Value("11/12")]);
    }

    [Fact]
    public async Task A_module_level_bound_sizes_with_the_same_grouping()
    {
        // A module-level array's bound is sized apart from the expression executor too.
        await Run("Const N = 5\nDim d(N * N \\ 2 - 2 + 1) As Long\nDebug.Print UBound(d)\n");

        AssertDebugLog([new Vb6Value(11L)]);
    }

    [Fact]
    public async Task The_Immediate_window_groups_as_the_program_does()
    {
        var (vb, dbg) = NewDebuggable("Go\nSub Go()\nDim n As Integer\nn = 10\nDebug.Print n\nEnd Sub\n", "Module1");
        dbg.SetBreakpoints("Module1", new[] { 5 });
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnStopped(StoppedInfo _) => stopped.TrySetResult();
        dbg.Stopped += OnStopped;
        var run = vb.Execute();
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        dbg.Stopped -= OnStopped;

        (await dbg.EvaluateAsync("?n - 2 + 3")).Should().Be(" 11 ");
        (await dbg.EvaluateAsync("?n * n \\ 8")).Should().Be(" 12 ");

        dbg.Stop();
        await run.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
    }
}
