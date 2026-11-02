using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class NumericComparisonTests : BaseVBTestFixture
{
    // #229: docs/vb6-fidelity-oracle/numeric-type-pairs--five-arithmetic-tables-and-comparison-is-a-sixth.md.
    [Theory]
    [InlineData("CSng(0.1)", "CDbl(0.1)", 0)]
    [InlineData("CSng(16777216)", "CLng(16777217)", -1)]
    [InlineData("CCur(0.0312)", "CDbl(0.03125)", 0)]
    [InlineData("CCur(0.0938)", "CDbl(0.09375)", 0)]
    [InlineData("CDec(\"0.10000000000000001\")", "CDbl(0.1)", 1)]
    [InlineData("CSng(0)", "CDbl(1E-300)", 0)]
    public async Task Comparisons_use_the_measured_pair_domain(string left, string right, int order)
    {
        await Run($"""
            Dim a, b
            a = {left}
            b = {right}
            Debug.Print {left} = {right}, {left} <> {right}, {left} < {right}, {left} > {right}
            Debug.Print {right} = {left}, {right} <> {left}, {right} < {left}, {right} > {left}
            Debug.Print a = b, a <> b, a < b, a > b
            Debug.Print b = a, b <> a, b < a, b > a
            """);
        AssertDebugLog(Enumerable.Range(0, 4).SelectMany(i => new[]
        {
            new Vb6Value(order == 0), new Vb6Value(order != 0),
            new Vb6Value(i % 2 == 0 ? order < 0 : order > 0),
            new Vb6Value(i % 2 == 0 ? order > 0 : order < 0)
        }).ToList());
    }

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

    [Fact]
    public async Task Narrowing_is_exact_and_out_of_range_comparisons_do_not_raise()
    {
        await Run("""
            Debug.Print CSng(0.1) = CDbl(0.1)
            Debug.Print CSng(0.1) = CDbl(0.100000006)
            Debug.Print CCur(0.0313) = CDbl(0.03125)
            Debug.Print CByte(0) < CInt(300)
            Debug.Print CCur(1) < CDbl(1E30)
            Debug.Print CDec(1) < CDbl(1E30)
            Debug.Print CSng(1) < CDbl(1E300)
            Debug.Print Err.Number = 0
            """);
        AssertDebugLog([new Vb6Value(true), new Vb6Value(false), new Vb6Value(false), new Vb6Value(true),
            new Vb6Value(true), new Vb6Value(true), new Vb6Value(true), new Vb6Value(true)]);
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
