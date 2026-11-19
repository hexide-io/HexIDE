using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// vb6.exe SP6, matching native and p-code: https://github.com/hexide-io/HexIDE/issues/2525.
public class CashFlowIntrinsicTests : BaseVBTestFixture
{
    [Theory]
    // Named call equals its measured positional twin (#4145).
    [InlineData("NPV(ValueArray := a, Rate := 0.1)", "409C45757D31FA2E", 0)]
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
    // Model bits authorized by Jen, #2525 issuecomment-6954533411 (2026-11-17).
    // Measured bits: 3FC875EFF170437B, 3FC875EFF1704153, 3FB62D72209FBD49,
    // 3FB94260ED237997, 3FC477233C8903B7; oracle: npv-irr-and-mirr.md.
    [InlineData("IRR(a)", "3FC875EFF17028E0", 0)]
    [InlineData("IRR(a, 0.2)", "3FC875EFF170444A", 0)]
    [InlineData("IRR(b)", "3FB62D7220A36D0D", 0)]
    [InlineData("IRR(cs)", "", 5)]
    [InlineData("MIRR(b, 0.1, 0.12)", "3FB94260ED23798E", 0)]
    [InlineData("MIRR(a, 0.1, 0.12)", "3FC477233C8903B7", 0)]
    [InlineData("MIRR(cs, 0.1, 0.12)", "", 11)]
    [InlineData("MIRR(b, -1, 0.12)", "", 5)]
    public async Task Authorized_cash_flow_row(string expression, string bits, int error)
    {
        await Run($"""
            Dim a(4) As Double, b(5) As Double, cs(2) As Double
            a(0) = -10000: a(1) = 3000: a(2) = 4200: a(3) = 6800: a(4) = 1000
            b(0) = -70000: b(1) = 12000: b(2) = 15000: b(3) = 18000: b(4) = 21000: b(5) = 26000
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
    [InlineData("IRR(a, 0)")] // Equal secant residuals: unmeasured nonconvergence.
    [InlineData("IRR(neg)")]
    [InlineData("IRR(zeros)")]
    [InlineData("IRR(mixed)")]
    [InlineData("IRR(z)")]
    [InlineData("IRR(matrix)")]
    [InlineData("IRR(Array(-1, 2))")]
    [InlineData("IRR(42)")]
    [InlineData("IRR(a, Null)")]
    [InlineData("IRR(a, \"0.1\")")]
    [InlineData("MIRR(a, 0.1, -1)")]
    [InlineData("MIRR(a, 0.1, -2)")]
    [InlineData("MIRR(neg, 0.1, 0.12)")]
    [InlineData("MIRR(zeros, 0.1, 0.12)")]
    [InlineData("MIRR(mixed, 0.1, 0.12)")]
    [InlineData("MIRR(z, 0.1, 0.12)")]
    [InlineData("MIRR(matrix, 0.1, 0.12)")]
    [InlineData("MIRR(Array(-1, 2), 0.1, 0.12)")]
    [InlineData("MIRR(42, 0.1, 0.12)")]
    [InlineData("MIRR(a, Null, 0.12)")]
    [InlineData("MIRR(a, 0.1, \"0.12\")")]
    [InlineData("NPV(0.1)")]
    [InlineData("NPV(Null, a)")]
    [InlineData("NPV(\"0.1\", a)")]
    [InlineData("NPV(0.1, Array(1, 2))")]
    [InlineData("NPV(0.1, 42)")]
    [InlineData("NPV(0.1, matrix)")]
    public async Task Unsupported_cash_flow_calls_refuse(string expression)
    {
        var run = () => Run($"""
            Dim a(4) As Double, b(5) As Double, cs(2) As Double, matrix(1, 1) As Double
            a(0) = -10000: a(1) = 3000: a(2) = 4200: a(3) = 6800: a(4) = 1000
            b(0) = -70000: b(1) = 12000: b(2) = 15000: b(3) = 18000: b(4) = 21000: b(5) = 26000
            cs(0) = 100: cs(1) = 200: cs(2) = 300
            Dim neg(2) As Double, zeros(2) As Double, mixed(2) As Double, z() As Double
            neg(0) = -100: neg(1) = -200: neg(2) = -300
            mixed(0) = 100: mixed(1) = 200
            Dim answer
            answer = {expression}
            """);
        await run.Should().ThrowAsync<NotImplementedException>().WithMessage("*#4883*");
    }
}
