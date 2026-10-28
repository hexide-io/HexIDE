using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// vb6.exe SP6, native and p-code: https://github.com/hexide-io/HexIDE/issues/2523.
public class FinancialIntrinsicTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("Pmt(0.08 / 12, 120, 10000)", "C05E54F74E50A768", 0)]
    [InlineData("Pmt(0.08 / 12, 120, 10000, 0, 1)", "C05E218ADC34FE76", 0)]
    [InlineData("Pmt(0.08 / 12, 120, 10000, 5000)", "C062950ED011D2E3", 0)]
    [InlineData("Pmt(0, 12, 1200)", "C059000000000000", 0)]
    [InlineData("Pmt(0, 12, 1200, 600, 1)", "C062C00000000000", 0)]
    [InlineData("Pmt(0.1, 0, 1000)", "", 5)]
    [InlineData("Pmt(-0.05, 10, 1000)", "C052A6D17C18AF17", 0)]
    [InlineData("Pmt(0.1, 10, 1000, 0, 2)", "C0627E69573E859B", 0)]
    [InlineData("Pmt(0.1, 10, 1000, 0, -1)", "C0627E69573E859B", 0)]
    [InlineData("Pmt(\"0.1\", 10, 1000)", "C06457DA465E5FC5", 0)]
    [InlineData("Pmt(Null, 10, 1000)", "", 94)]
    [InlineData("Pmt(0.1, 10.5, 1000)", "C063C418EDB5A8E3", 0)]
    [InlineData("FV(0.06 / 12, 120, -100)", "40D000FBD1CEC57B", 0)]
    [InlineData("FV(0.06 / 12, 120, -100, -1000, 1)", "40D1DC515981D2E6", 0)]
    [InlineData("FV(0, 10, -100, -500)", "4097700000000000", 0)]
    [InlineData("FV(0.1, 0, -100, -500)", "407F400000000000", 0)]
    [InlineData("PV(0.08 / 12, 240, -500)", "40ED3024AAD01C32", 0)]
    [InlineData("PV(0.08 / 12, 240, -500, 10000, 1)", "40EC643E47D8C3FB", 0)]
    [InlineData("PV(0, 10, -100)", "408F400000000000", 0)]
    public async Task MeasuredRow(string expression, string bits, int error)
    {
        using var locale = VbLocale.Use(System.Globalization.CultureInfo.GetCultureInfo("en-GB"));
        await Run($"""
            On Error Resume Next
            Dim answer As Variant
            answer = {expression}
            Debug.Print Err.Number
            If Err.Number = 0 Then Debug.Print answer
            """);
        debug[0].Value.Should().Be((long)error);
        if (error != 0) return;
        debug.Should().HaveCount(2);
        debug[1].Type.Should().Be(Vb6Value.ValueType.Double);
        BitConverter.DoubleToUInt64Bits((double)debug[1].Value!).ToString("X16").Should().Be(bits);
    }

    [Theory]
    [InlineData("Pmt(0.1, 10)")]
    [InlineData("Pmt(0.1, 10, 1000, 0, 0, 0)")]
    [InlineData("Pmt(0.1, 10, Null)")]
    [InlineData("FV(Null, 10, -100)")]
    [InlineData("PV(0.1, 10, \"-100\")")]
    [InlineData("Pmt(\"not numeric\", 10, 1000)")]
    [InlineData("PV(#1/1/2000#, 10, -100)")]
    [InlineData("FV(1, 2000, -1)")]
    [InlineData("FV(-1, 10, -100)")]
    [InlineData("PV(0.1, 10001, -100)")]
    public async Task Unmeasured_edges_refuse(string expression)
    {
        var run = () => Run($"Dim answer\nanswer = {expression}\n");
        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*#2523*");
    }
}
