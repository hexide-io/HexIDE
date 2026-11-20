using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// vb6.exe SP6 native/p-code measurements, 2026-11-19; p-code wins on errors:
// https://github.com/hexide-io/HexIDE/issues/4883 (latest supervisor comment).
// Marked IRR/MIRR rows pin the closest model under the standing #2525 precision exception.
public class FinancialEdgesTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("Pmt(-1, 10, 1000)", "8000000000000000", 0, "")]
    [InlineData("Pmt(-1.5, 2, 1000)", "C07F400000000000", 0, "")]
    [InlineData("Pmt(-1.5, 3, 1000)", "4064D55555555555", 0, "")]
    [InlineData("Pmt(0.1, -10, 1000)", "404F5F6919797F13", 0, "")]
    [InlineData("Pmt(0.001, 20000, 1000)", "BFF00000008F1060", 0, "")]
    [InlineData("Pmt(0.1, 10, 1E+308)", "FFB72CFB8FACA15C", 0, "")]
    [InlineData("Pmt(-0.1, 10, 1E+308)", "FF9E7E75219704CD", 0, "")]
    [InlineData("Pmt(0, 10, 1.7E+308, 1.7E+308)", "FFC8357426C5C92B", 0, "")]
    [InlineData("FV(-2, 2, -100)", "8000000000000000", 0, "")]
    [InlineData("FV(-1, 10, -100)", "4059000000000000", 0, "")]
    [InlineData("PV(-2, 2, -100)", "8000000000000000", 0, "")]
    [InlineData("NPer(0.1, -100, 1000, -1000)", "0000000000000000", 0, "")]
    [InlineData("NPer(0.1, -100, 0, 0)", "0000000000000000", 0, "")]
    [InlineData("NPer(0.1, 1E-300, 1E+300)", "C0CC4393F47F02E3", 0, "")]
    [InlineData("IPmt(-1, 2, 10, 1000)", "8000000000000000", 0, "")]
    [InlineData("IPmt(0.1, 1.5, 10, 1000)", "C0583BFF804633E8", 0, "")]
    [InlineData("IPmt(0.1, 2, 10, 1E+308)", "FFAAB1A33F9B2C58", -1, "")] // Fitted model: FFAAB1A33F9B2C59; keep the measured bits above.
    [InlineData("PPmt(-1, 2, 10, 1000)", "0000000000000000", 0, "")]
    [InlineData("PPmt(0.1, 2, 10, 1E+308)", "FFA3A853DFBE1660", -1, "")] // Fitted model: FFA3A853DFBE165F; keep the measured bits above.
    [InlineData("Rate(10.5, -100, 700)", "3FB3EBE09F6159E2", 0, "")]
    [InlineData("SLN(1000, 100, -5)", "C066800000000000", 0, "")]
    [InlineData("Pmt(-2, 2, 1000)", "", 11, "")]
    [InlineData("PV(-1, 10, -100)", "", 11, "")]
    [InlineData("PV(0.1, -20000, -100)", "", 11, "")]
    [InlineData("IPmt(-2, 2, 2, 1000)", "", 11, "")]
    [InlineData("PPmt(-2, 2, 2, 1000)", "", 11, "")]
    [InlineData("Pmt(-1.5, 2.5, 1000)", "", 6, "")]
    [InlineData("Pmt(0.1, 20000, 1000)", "", 6, "")]
    [InlineData("Pmt(10, 400, 1000)", "", 6, "")]
    [InlineData("Pmt(0, 1E-300, 1E+300)", "", 6, "")]
    [InlineData("FV(-1.5, 2.5, -100)", "", 6, "")]
    [InlineData("FV(0.1, 10, -1E+308)", "", 6, "")]
    [InlineData("FV(0, 10, -1E+308)", "", 6, "")]
    [InlineData("FV(0.1, 20000, -100)", "", 6, "")]
    [InlineData("PV(-1.5, 2.5, -100)", "", 6, "")]
    [InlineData("PV(0.1, 10, -1E+308)", "", 6, "")]
    [InlineData("NPer(1E-300, -100, 1000)", "", 6, "")]
    [InlineData("IPmt(-1.5, 2, 2.5, 1000)", "", 6, "")]
    [InlineData("PPmt(-1.5, 2, 2.5, 1000)", "", 6, "")]
    [InlineData("IPmt(0.1, 2, 20000, 1000)", "", 6, "")]
    [InlineData("PPmt(0.1, 2, 20000, 1000)", "", 6, "")]
    [InlineData("Rate(10.5, -100, 700, 0, 0, -1.5)", "", 6, "")]
    [InlineData("Rate(20000, -1, 100)", "", 6, "")]
    [InlineData("Rate(10, -1E+308, 1E+308)", "", 6, "")]
    [InlineData("SLN(1E+308, -1E+308, 1)", "", 6, "")]
    [InlineData("NPer(0.1, -50, 1000)", "", 5, "")]
    [InlineData("NPer(0.1, 100, -1000)", "", 5, "")]
    [InlineData("NPer(-1, -100, 1000)", "", 5, "")]
    [InlineData("NPer(-2, -100, 1000)", "", 5, "")]
    [InlineData("Rate(10, -100, 700, 0, 0, -1)", "", 5, "")]
    [InlineData("Rate(10, -100, 700, 0, 0, -2)", "", 5, "")]
    [InlineData("SLN(1000, 100, 0)", "", 5, "")]
    [InlineData("SYD(1000, 100, 0, 1)", "", 5, "")]
    [InlineData("SYD(1E+308, -1E+308, 5, 1)", "", 5, "")]
    [InlineData("SYD(1000, 100, 5, 6)", "", 5, "")]
    [InlineData("DDB(1000, 100, 0, 1)", "", 5, "")]
    [InlineData("DDB(1E+308, -1E+308, 5, 1)", "", 5, "")]
    [InlineData("DDB(1000, 100, 5, 6)", "", 5, "")]
    [InlineData("DDB(1000, 100, 5, 1, 0)", "", 5, "")]
    [InlineData("Pmt(0.1, 10)", "", 449, "")]
    [InlineData("Pmt(0.1, , 1000)", "", 449, "")]
    [InlineData("IPmt(0.1, 2, 10)", "", 449, "")]
    [InlineData("NPV(0.1)", "", 449, "")]
    [InlineData("NPV(, a)", "", 449, "")]
    [InlineData("IRR()", "", 449, "")]
    [InlineData("MIRR(a, 0.1)", "", 449, "")]
    [InlineData("MIRR(a, , 0.1)", "", 449, "")]
    [InlineData("SLN(1, 2)", "", 449, "")]
    [InlineData("SLN(1, , 3)", "", 449, "")]
    [InlineData("SYD(1, 2, 3)", "", 449, "")]
    [InlineData("DDB(1, 2, 3)", "", 449, "")]
    [InlineData("Pmt(0.1, 10, 1000, 0, 0, 0)", "", 450, "")]
    [InlineData("Rate(10, -100, 700, 0, 0, 0.1, 1)", "", 450, "")]
    [InlineData("NPV(0.1, a, 1)", "", 450, "")]
    [InlineData("IRR(a, 0.1, 1)", "", 450, "")]
    [InlineData("SLN(1, 2, 3, 4)", "", 450, "")]
    [InlineData("DDB(1, 2, 3, 4, 5, 6)", "", 450, "")]
    [InlineData("Pmt(\"abc\", 10, 1000)", "", 13, "")]
    [InlineData("Pmt(0.1, \"\", 1000)", "", 13, "")]
    [InlineData("NPV(-2, a)", "4056800000000000", 0, "Dim a(2) As Double\na(0) = -100: a(1) = 50: a(2) = 60\n")]
    [InlineData("NPV(-1.5, a)", "C054000000000000", 0, "Dim a(2) As Double\na(0) = -100: a(1) = 50: a(2) = 60\n")]
    [InlineData("NPV(1E+200, a)", "", 6, "Dim a(2) As Double\na(0) = -100: a(1) = 50: a(2) = 60\n")]
    [InlineData("NPV(1E+300, a)", "", 6, "Dim a(2) As Double\na(0) = -100: a(1) = 50: a(2) = 60\n")]
    [InlineData("NPV(-0.999999, a)", "", 11, "Dim a(59) As Double, i As Long\nFor i = 0 To 59: a(i) = 1: Next\n")]
    [InlineData("NPV(-0.9, a)", "", 11, "Dim a(399) As Double, i As Long\nFor i = 0 To 399: a(i) = 1: Next\n")]
    [InlineData("NPV(0.1, a)", "7FEEE4C225C7C1BE", 0, "Dim a(1) As Double\na(0) = 1E+308: a(1) = 1E+308\n")]
    [InlineData("IRR(a)", "", 5, "Dim a(1) As Double\na(0) = -100\na(1) = -50\n")]
    [InlineData("IRR(a)", "", 5, "Dim a(1) As Double\na(0) = 0\na(1) = 0\n")]
    [InlineData("IRR(a)", "", 5, "Dim a(1) As Double\na(0) = 0\na(1) = 100\n")]
    [InlineData("IRR(a)", "", 5, "Dim a(1) As Double\na(0) = -100\na(1) = 0\n")]
    [InlineData("IRR(a)", "", 5, "Dim a(0) As Double\na(0) = -100\n")]
    [InlineData("IRR(a)", "", 5, "Dim a(0) As Double\na(0) = 100\n")]
    [InlineData("IRR(a)", "", 5, "Dim a(6) As Double\na(0) = 1\na(1) = -1\na(2) = 1\na(3) = -1\na(4) = 1\na(5) = -1\na(6) = 1\n")]
    [InlineData("IRR(a)", "", 5, "Dim a(1) As Double\na(0) = -100\na(1) = 1E+308\n")]
    [InlineData("IRR(a, -1)", "", 5, "Dim a(1) As Double\na(0) = -100: a(1) = 110\n")]
    [InlineData("IRR(a, -2)", "", 5, "Dim a(1) As Double\na(0) = -100: a(1) = 110\n")]
    [InlineData("IRR(a, 1E+300)", "", 5, "Dim a(1) As Double\na(0) = -100: a(1) = 110\n")]
    [InlineData("IRR(a)", "3FB9999999999994", 0, "Dim a(2) As Double\na(1) = -100: a(2) = 110\n")] // Measured: 3FB999999999999C; authorized closest IRR/MIRR model (#2525).
    [InlineData("MIRR(a, .1, -1)", "", 5, "Dim a(2) As Double\na(0) = -100\na(1) = 50\na(2) = 60\n")]
    [InlineData("MIRR(a, .1, -2)", "BFE5E1764EDBDB78", 0, "Dim a(2) As Double\na(0) = -100\na(1) = 50\na(2) = 60\n")]
    [InlineData("MIRR(a, -2, .1)", "3FB28787C6F7D7A2", 0, "Dim a(2) As Double\na(0) = -100\na(1) = 50\na(2) = 60\n")]
    [InlineData("MIRR(a, .1, .1)", "BFF0000000000000", 0, "Dim a(1) As Double\na(0) = -100\na(1) = -50\n")]
    [InlineData("MIRR(a, .1, .1)", "BFF0000000000000", 0, "Dim a(1) As Double\na(0) = -100\na(1) = 0\n")]
    [InlineData("MIRR(a, .1, .1)", "", 11, "Dim a(1) As Double\na(0) = 0\na(1) = 0\n")]
    [InlineData("MIRR(a, .1, .1)", "", 11, "Dim a(1) As Double\na(0) = 0\na(1) = 100\n")]
    [InlineData("MIRR(a, .1, .1)", "", 5, "Dim a(0) As Double\na(0) = -100\n")]
    [InlineData("MIRR(a, .01, .01)", "BFEE90BA55C3468C", 0, "Dim a(2) As Double\na(0) = -1000\na(1) = 1\na(2) = 1\n")] // Measured: BFEE90BA55C3468B; authorized closest IRR/MIRR model (#2525).
    [InlineData("MIRR(a, .1, .1)", "7F76C8E5CA239029", 0, "Dim a(1) As Double\na(0) = -100\na(1) = 1E+308\n")]
    [InlineData("MIRR(a, 1E+300, .1)", "", 6, "Dim a(1) As Double\na(0) = -100\na(1) = 50\n")]
    [InlineData("MIRR(a, 0.01, 0.01)", "3F8400F70D38208B", 0, "Dim a(10000) As Double, i As Long\na(0) = -1000\nFor i = 1 To 10000: a(i) = 1: Next\n")] // Measured: 3F8400F70D382091; authorized closest IRR/MIRR model (#2525).
    [InlineData("NPV(0.1, a)", "", 5, "Dim a() As Double\n")]
    [InlineData("NPV(0.1, a)", "", 5, "Dim a(1, 1) As Double\n")]
    [InlineData("NPV(0.1, a)", "", 13, "Dim a(2) As Long\n")]
    [InlineData("NPV(0.1, a)", "", 13, "Dim a(2) As Variant\n")]
    [InlineData("NPV(0.1, a)", "", 13, "Dim d(2) As Double, a As Variant\na = d\n")]
    [InlineData("NPV(0.1, a)", "", 13, "Dim a\na = 5\n")]
    [InlineData("IRR(a)", "", 5, "Dim a() As Double\n")]
    [InlineData("IRR(a)", "", 5, "Dim a(1, 1) As Double\n")]
    [InlineData("IRR(a)", "", 13, "Dim a(2) As Long\n")]
    [InlineData("IRR(a)", "", 13, "Dim a(2) As Variant\n")]
    [InlineData("IRR(a)", "", 13, "Dim d(2) As Double, a As Variant\na = d\n")]
    [InlineData("IRR(a)", "", 13, "Dim a\na = 5\n")]
    [InlineData("MIRR(a, 0.1, 0.1)", "", 5, "Dim a() As Double\n")]
    [InlineData("MIRR(a, 0.1, 0.1)", "", 5, "Dim a(1, 1) As Double\n")]
    [InlineData("NPV(0.1, a)", "C012081400C4F3E8", 0, "Dim a(1 To 3) As Double\na(1) = -100: a(2) = 50: a(3) = 60\n")]
    public async Task Measured_edge(string expression, string bits, int error, string setup)
    {
        using var locale = VbLocale.Use(System.Globalization.CultureInfo.GetCultureInfo("en-GB"));
        var run = () => Run($"""
            {setup}
            On Error Resume Next
            Dim answer As Variant
            answer = {expression}
            Debug.Print Err.Number
            If Err.Number = 0 Then Debug.Print answer
            """);
        if (error == -1)
        {
            // The bits parameter preserves the measured answer the fitted model cannot yet match.
            await run.Should().ThrowAsync<NotImplementedException>().WithMessage("*#4883*");
            return;
        }
        await run();
        debug[0].Value.Should().Be((long)error);
        if (error != 0) return;
        debug.Should().HaveCount(2);
        debug[1].Type.Should().Be(Vb6Value.ValueType.Double);
        BitConverter.DoubleToUInt64Bits((double)debug[1].Value!).ToString("X16").Should().Be(bits);
    }
}
