using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// vb6.exe SP6, native and p-code: https://github.com/hexide-io/HexIDE/issues/2524.
public class DepreciationTests : BaseVBTestFixture
{
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
    [InlineData("SLN(30000, 7500, 0)")]
    [InlineData("SYD(30000, 7500, 10, 11)")]
    [InlineData("SYD(30000, 7500, 10, 0)")]
    public async Task Measured_invalid_argument_is_error_5(string expression)
    {
        await Run($"On Error Resume Next\nx = {expression}\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(5L)]);
    }
    // This is HexIDE's refusal contract, not a claim about VB6's unmeasured argument diagnostics.
    [Theory]
    [InlineData("SLN(30000, 7500)")]
    [InlineData("SLN(30000, 7500, 10, 1)")]
    [InlineData("SYD(30000, 7500, 10)")]
    [InlineData("SYD(30000, 7500, 10, 1, 2)")]
    public async Task Unmeasured_argument_count_is_explicitly_refused(string expression)
    {
        Func<Task> run = () => Run($"Debug.Print {expression}\n");
        await run.Should().ThrowAsync<NotImplementedException>();
    }

}
