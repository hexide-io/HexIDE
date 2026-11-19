using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Variant argument measurements, vb6.exe SP6 native and p-code (2026-11-19):
// https://github.com/hexide-io/HexIDE/issues/4883.
public class FinancialArgumentTests : BaseVBTestFixture
{
    public static IEnumerable<object[]> Arguments()
    {
        (string Call, int Required, int Optional)[] calls =
        [
            ("Pmt(0.1, 10, {0}, {1})", 2, 3),
            ("FV(0.1, 10, {0}, {1})", 2, 3),
            ("PV(0.1, 10, {0}, {1})", 2, 3),
            ("NPer(0.1, -100, {0}, {1})", 2, 3),
            ("IPmt(0.1, 2, 10, {0}, {1})", 3, 4),
            ("PPmt(0.1, 2, 10, {0}, {1})", 3, 4),
            ("Rate(10, -100, {0}, {1})", 2, 3),
            ("SLN({0}, 0, 10)", 0, -1),
            ("SYD({0}, 0, 10, 2)", 0, -1),
            ("DDB({0}, 0, 10, 2, {1})", 0, 4),
            ("NPV({0}, a)", 0, -1),
            ("IRR(a, {1})", -1, 1),
            ("MIRR(a, {0}, 0.1)", 1, -1)
        ];
        (string Setup, string Number, int Error)[] kinds =
        [
            ("x = CDate(0.1)", "0.1", 0),
            ("x = \"0.1\"", "0.1", 0),
            ("x = \" 0.1 \"", "0.1", 0),
            ("x = True", "-1", 0),
            ("", "0", 0),
            ("x = CCur(0.1)", "CDbl(CCur(0.1))", 0),
            ("x = CDec(0.1)", "CDbl(CDec(0.1))", 0),
            ("x = Null", "0", 94),
            ("x = \"abc\"", "0", 13),
            ("x = \"\"", "0", 13),
            ("x = Array(1)", "0", 13),
            ("x = CVErr(5)", "0", 13),
            ("Set x = Nothing", "0", 91),
            ("Set x = New Collection", "0", 450),
            ("missing", "0", 13)
        ];
        foreach (var (call, required, optional) in calls)
        foreach (var (setup, number, error) in kinds)
        foreach (bool isOptional in new[] { false, true })
        {
            if ((isOptional ? optional : required) < 0) continue;
            string actual = string.Format(call, isOptional ? "1000" : "x", isOptional ? "x" : "2");
            string expected = string.Format(call, isOptional ? "1000" : number, isOptional ? number : "2");
            if (setup == "missing" && isOptional)
            {
                // Forwarded Optional Variant is different from an empty argument slot at the call site.
                int comma = actual.LastIndexOf(',');
                expected = actual[..comma] + ")";
            }
            yield return [actual, setup, expected, setup == "missing" && isOptional ? 0 : error];
        }
    }

    [Theory]
    [MemberData(nameof(Arguments))]
    public async Task Variant_argument_matches_number_or_measured_error(string expression, string setup, string equivalent, int error)
    {
        using var locale = VbLocale.Use(System.Globalization.CultureInfo.GetCultureInfo("en-GB"));
        string declaration = setup == "missing" ? "" : "Dim x As Variant";
        string header = setup == "missing" ? "Sub Check(Optional x As Variant)" : "Sub Check()";
        // Empty converts to a zero IRR guess; that numerical domain remains unmeasured.
        bool zeroGuess = expression == "IRR(a, x)" && setup == "";
        var run = () => Run($"""
            {header}
            {declaration}
            Dim a(2) As Double
            a(0) = -100: a(1) = 50: a(2) = 60
            { (setup == "missing" ? "" : setup) }
            On Error Resume Next
            Dim result As Variant
            result = {expression}
            Debug.Print Err.Number
            Debug.Print result
            Err.Clear
            result = Empty
            result = {(error == 0 ? equivalent : "0")}
            Debug.Print Err.Number
            Debug.Print result
            End Sub
            Check
            """);
        if (zeroGuess)
        {
            await run.Should().ThrowAsync<NotImplementedException>().WithMessage("*#4883*");
            return;
        }
        await run();
        debug.Should().HaveCount(4);
        if (error != 0)
        {
            debug[0].Value.Should().Be((long)error);
            return;
        }
        debug[0].Should().Be(debug[2]);
        if ((long)debug[0].Value! != 0) return;
        debug[1].Type.Should().Be(Vb6Value.ValueType.Double);
        debug[3].Type.Should().Be(Vb6Value.ValueType.Double);
        BitConverter.DoubleToUInt64Bits((double)debug[1].Value!).Should()
            .Be(BitConverter.DoubleToUInt64Bits((double)debug[3].Value!));
    }
    public static IEnumerable<object[]> NonFiniteArguments()
    {
        string[] calls =
        [
            "Pmt(bad, 10, 1000)", "FV(bad, 10, -100)", "PV(bad, 10, -100)",
            "NPer(bad, -100, 1000)", "IPmt(bad, 2, 10, 1000)", "PPmt(bad, 2, 10, 1000)",
            "Rate(10, -100, 700, 0, 0, bad)", "SLN(bad, 100, 10)",
            "SYD(bad, 100, 10, 2)", "DDB(bad, 100, 10, 2)",
            "NPV(bad, a)", "IRR(a, bad)", "MIRR(a, bad, 0.1)",
            "Pmt(0.1, 10, 1000, 0, bad)", "FV(0.1, 10, -100, 0, bad)",
            "PV(0.1, 10, -100, 0, bad)"
        ];
        foreach (string call in calls)
        foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            yield return [call, value];
    }

    [Theory]
    [MemberData(nameof(NonFiniteArguments))]
    public async Task Non_finite_inputs_remain_explicitly_refused(string expression, double value)
    {
        // HexIDE's refusal contract, not a VB6 result: #4883 explicitly leaves these inputs out.
        context.AllocVariable(rootEnv, "bad", new Vb6Value(value));
        var run = () => Run($"""
            Dim a(2) As Double
            a(0) = -100: a(1) = 50: a(2) = 60
            x = {expression}
            """);
        await run.Should().ThrowAsync<NotImplementedException>().WithMessage("*#4883*");
    }

}
