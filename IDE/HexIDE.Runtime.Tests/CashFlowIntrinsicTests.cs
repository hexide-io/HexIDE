using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// vb6.exe SP6, matching native and p-code: https://github.com/hexide-io/HexIDE/issues/2525.
public class CashFlowIntrinsicTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("NPV(0.1, a)", "409C45757D31FA2E", 0)]
    [InlineData("NPV(0, a)", "40B3880000000000", 0)]
    [InlineData("NPV(-1, a)", "", 5)]
    [InlineData("NPV(0.1, cs)", "407E197C0E9E19C5", 0)]
    [InlineData("NPV(0.1, z)", "", 5)]
    public async Task Measured_npv_row(string expression, string bits, int error)
    {
        await Run($"""
            Dim a(4) As Double, cs(2) As Double, z() As Double
            a(0) = -10000: a(1) = 3000: a(2) = 4200: a(3) = 6800: a(4) = 1000
            cs(0) = 100: cs(1) = 200: cs(2) = 300
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
    [InlineData("IRR(a)")]
    [InlineData("IRR(a, 0.2)")]
    [InlineData("IRR(b)")]
    [InlineData("IRR(cs)")]
    [InlineData("MIRR(b, 0.1, 0.12)")]
    [InlineData("MIRR(a, 0.1, 0.12)")]
    [InlineData("MIRR(cs, 0.1, 0.12)")]
    [InlineData("MIRR(b, -1, 0.12)")]
    [InlineData("NPV(0.1)")]
    [InlineData("NPV(Null, a)")]
    [InlineData("NPV(\"0.1\", a)")]
    [InlineData("NPV(0.1, Array(1, 2))")]
    [InlineData("NPV(0.1, 42)")]
    [InlineData("NPV(0.1, matrix)")]
    [InlineData("NPV(Rate := 0.1, ValueArray := a)")]
    public async Task Unsupported_cash_flow_calls_refuse(string expression)
    {
        var run = () => Run($"""
            Dim a(4) As Double, b(5) As Double, cs(2) As Double, matrix(1, 1) As Double
            a(0) = -10000: a(1) = 3000: a(2) = 4200: a(3) = 6800: a(4) = 1000
            b(0) = -70000: b(1) = 12000: b(2) = 15000: b(3) = 18000: b(4) = 21000: b(5) = 26000
            cs(0) = 100: cs(1) = 200: cs(2) = 300
            Dim answer
            answer = {expression}
            """);
        await run.Should().ThrowAsync<NotImplementedException>();
    }
}
