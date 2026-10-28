using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class NumericComparisonTests : BaseVBTestFixture
{
    // #229 supervisor measurements: vb6.exe SP6, native and p-code agree.
    [Theory]
    [InlineData("CSng(0.1)", "CDbl(0.1)", true, true)]
    [InlineData("CSng(16777216)", "CLng(16777217)", true, false)]
    [InlineData("CCur(0.0312)", "CDbl(0.03125)", true, true)]
    [InlineData("CDec(\"0.10000000000000001\")", "CDbl(0.1)", false, true)]
    public async Task Inclusive_comparisons_use_the_measured_pair_domain(string left, string right, bool le, bool ge)
    {
        await Run($"""
            Dim a, b
            a = {left}
            b = {right}
            Debug.Print {left} <= {right}
            Debug.Print {left} >= {right}
            Debug.Print {right} <= {left}
            Debug.Print {right} >= {left}
            Debug.Print a <= b
            Debug.Print a >= b
            Debug.Print b <= a
            Debug.Print b >= a
            """);
        AssertDebugLog([new Vb6Value(le), new Vb6Value(ge), new Vb6Value(ge), new Vb6Value(le),
            new Vb6Value(le), new Vb6Value(ge), new Vb6Value(ge), new Vb6Value(le)]);
    }

    [Theory]
    [InlineData("True", "=", "-1", true)]
    [InlineData("True", "=", "CDbl(-1)", true)]
    [InlineData("True", "<", "0", true)]
    [InlineData("True", "=", "1", false)]
    [InlineData("False", "=", "CByte(0)", true)]
    [InlineData("False", "=", "CCur(0)", true)]
    public async Task Boolean_numeric_cases_already_follow_the_measurements(string left, string op, string right, bool expected)
    {
        await Run($"Dim a, b\na = {left}\nb = {right}\nDebug.Print {left} {op} {right}\nDebug.Print a {op} b");
        AssertDebugLog([new Vb6Value(expected), new Vb6Value(expected)]);
    }

    [Fact]
    public async Task False_is_not_less_than_true()
    {
        await Run("Dim a, b\na = False\nb = True\nDebug.Print False < True\nDebug.Print a < b");
        AssertDebugLog([new Vb6Value(false), new Vb6Value(false)]);
    }
}
