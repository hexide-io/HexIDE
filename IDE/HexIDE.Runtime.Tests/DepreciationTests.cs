using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// vb6.exe SP6, native and p-code: https://github.com/hexide-io/HexIDE/issues/2524.
public class DepreciationTests : BaseVBTestFixture
{
    // Oracle: docs/vb6-fidelity-oracle/ddb-sln-and-syd.md.
    [Theory]
    [InlineData("DDB(2400, 300, 10 * 365, 1)", "3FF50A8542A150A8")]
    [InlineData("DDB(2400, 300, 10 * 12, 1)", "4044000000000000")]
    [InlineData("DDB(2400, 300, 10, 1)", "407E000000000000")]
    [InlineData("DDB(2400, 300, 10, 2, 1.5)", "4073200000000000")]
    // Measured: 40361F5F40D83797. Jen approved the closest model, #2524 issuecomment-6954532512.
    [InlineData("DDB(2400, 300, 10, 10)", "40361F5F40D83791")]
    [InlineData("DDB(2400, 300, 10, 2.5)", "4075775C544FF263")]
    [InlineData("DDB(2400, 3000, 10, 1)", "C082C00000000000")]
    public async Task Ddb_approved_model_returns_Double_bits(string expression, string bits)
    {
        await Run($"Debug.Print {expression}\n");
        debug.Should().ContainSingle();
        debug[0].Type.Should().Be(Vb6Value.ValueType.Double);
        BitConverter.DoubleToUInt64Bits((double)debug[0].Value!).ToString("X16").Should().Be(bits);
        if (expression == "DDB(2400, 300, 10, 10)")
            written.ToString().Trim().Should().Be("22.1225472000002");
    }

    [Theory]
    [InlineData("SLN(30000, 7500, 10)", "40A1940000000000")]
    [InlineData("SLN(30000, 7500, -2)", "C0C5F90000000000")]
    [InlineData("SYD(30000, 7500, 10, 1)", "40AFF5D1745D1746")]
    [InlineData("SYD(30000, 7500, 10, 10)", "407991745D1745D1")]
    [InlineData("SYD(30000, 7500, 10, 2.5)", "40AB2A8BA2E8BA2F")]
    public async Task Measured_value_is_bit_exact_Double(string expression, string bits)
    {
        await Run($"Debug.Print {expression}\n");
        debug.Should().ContainSingle();
        debug[0].Type.Should().Be(Vb6Value.ValueType.Double);
        BitConverter.DoubleToUInt64Bits((double)debug[0].Value!).ToString("X16").Should().Be(bits);
    }

    [Theory]
    [InlineData("DDB(2400, 300, 10, 11)")]
    [InlineData("DDB(2400, 300, 10, 0)")]
    [InlineData("DDB(2400, 300, 0, 1)")]
    [InlineData("SLN(30000, 7500, 0)")]
    [InlineData("SYD(30000, 7500, 10, 11)")]
    [InlineData("SYD(30000, 7500, 10, 0)")]
    public async Task Measured_invalid_argument_is_error_5(string expression)
    {
        await Run($"On Error Resume Next\nx = {expression}\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(5L)]);
    }
    // Argument diagnostics measured in #4883 (SP6 native and p-code).
    [Theory]
    [InlineData("DDB(2400, 300, 10)", 449)]
    [InlineData("DDB(2400, 300, 10, 1, 2, 3)", 450)]
    [InlineData("SLN(30000, 7500)", 449)]
    [InlineData("SLN(30000, 7500, 10, 1)", 450)]
    [InlineData("SYD(30000, 7500, 10)", 449)]
    [InlineData("SYD(30000, 7500, 10, 1, 2)", 450)]
    public async Task Measured_argument_count_reports_VB6_error(string expression, int error)
    {
        await Run($"On Error Resume Next\nx = {expression}\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value((long)error)]);
    }

    // HexIDE refuses FinancialNumber.Pow's unmeasured domains, without guessing a VB6 value.
    [Theory]
    [InlineData("DDB(2400, 300, 10, 1, 10)")]
    [InlineData("DDB(2400, 300, 10, 1, 11)")]
    [InlineData("DDB(2400, 300, 10002, 10002)")]
    public async Task Ddb_unmeasured_power_domain_is_refused(string expression)
    {
        Func<Task> run = () => Run($"Debug.Print {expression}\n");
        await run.Should().ThrowAsync<NotImplementedException>();
    }
}
