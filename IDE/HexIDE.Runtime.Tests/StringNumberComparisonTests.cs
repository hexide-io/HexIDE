using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class StringNumberComparisonTests : BaseVBTestFixture
{
    // Exact rows from docs/vb6-fidelity-oracle/a-string-compared-with-a-number.md (#211).
    [Theory]
    [InlineData("sT < nL", "True")]
    [InlineData("sT < 10", "True")]
    [InlineData("nL > sT", "True")]
    [InlineData("\"2\" < 10", "True")]
    [InlineData("10 > \"5\"", "True")]
    [InlineData("s1 = 1", "True")]
    [InlineData("\"1.0\" = 1", "True")]
    [InlineData("1 = \"1\"", "True")]
    [InlineData("sT = 2", "True")]
    [InlineData("s1 <> 1", "False")]
    [InlineData("nI > sT", "True")]
    [InlineData("nD > sT", "True")]
    [InlineData("nC > sT", "True")]
    [InlineData("nB > sT", "True")]
    [InlineData("sT <> nL", "True")]
    [InlineData("sA < nL", "13")]
    [InlineData("nL = sA", "13")]
    [InlineData("sA <> nL", "13")]
    [InlineData("\"abc\" > 1", "13")]
    [InlineData("\"abc\" = 1", "13")]
    [InlineData("sE = 0", "13")]
    [InlineData("s25 = 2", "False")]
    [InlineData("s25 = nI2", "False")]
    [InlineData("s25 = nL2", "False")]
    [InlineData("s25 = nS", "False")]
    [InlineData("\"2.5\" = 2", "False")]
    [InlineData("nI2 = \"2.5\"", "False")]
    [InlineData("s25 > nI2", "True")]
    [InlineData("s25 = 2.5", "True")]
    [InlineData("s35 = 4", "False")]
    [InlineData("s4 > nI", "True")]
    [InlineData("s300 > nB", "True")]
    [InlineData("sc = nC1", "True")]
    [InlineData("bT = \"True\"", "True")]
    [InlineData("bT = sT", "True")]
    [InlineData("bF = \"0\"", "True")]
    [InlineData("bT = sA", "13")]
    [InlineData("dt = \"1/1/2000\"", "True")]
    [InlineData("dt > sT", "True")]
    [InlineData("vS < nL", "True")]
    [InlineData("vS < 10", "True")]
    [InlineData("v1 = 1", "True")]
    [InlineData("vS = 2", "True")]
    [InlineData("vS25 > nI2", "True")]
    [InlineData("vS = nL", "False")]
    [InlineData("vS25 = nI2", "False")]
    [InlineData("vA > nL", "True")]
    [InlineData("vA > 1", "True")]
    [InlineData("vA <> nL", "True")]
    [InlineData("vA = nL", "False")]
    [InlineData("vN = \"10\"", "True")]
    [InlineData("vN > \"5\"", "False")]
    [InlineData("vL > \"5\"", "False")]
    [InlineData("vN = sT", "False")]
    [InlineData("vS < vN", "False")]
    [InlineData("vN < vS", "True")]
    public async Task Measured_comparison(string expression, string expected)
    {
        await Run($"""
            Dim sT As String, sA As String, s1 As String, sE As String, s25 As String, s35 As String
            Dim s4 As String, sc As String, s300 As String
            sT = "2": sA = "abc": s1 = "1.0": sE = "": s25 = "2.5": s35 = "3.5"
            s4 = "40000": sc = "1.00001": s300 = "300"
            Dim vS, vA, v1, vS25, vN, vL
            vS = "2": vA = "abc": v1 = "1.0": vS25 = "2.5": vN = 10: vL = CLng(10)
            Dim nL As Long, nI As Integer, nD As Double, nC As Currency, nB As Byte
            nL = 10: nI = 10: nD = 10: nC = 10: nB = 10
            Dim nI2 As Integer, nL2 As Long, nS As Single, nC1 As Currency
            nI2 = 2: nL2 = 2: nS = 2: nC1 = 1
            Dim bT As Boolean, bF As Boolean, dt As Date
            bT = True: bF = False: dt = #1/1/2000#
            On Error Resume Next
            Dim result
            result = {expression}
            If Err.Number <> 0 Then
                Debug.Print CStr(Err.Number)
            Else
                Debug.Print CStr(result)
            End If
            """);
        AssertDebugLog([new Vb6Value(expected)]);
    }
    [Theory]
    [InlineData("String", "CDec(1)")]
    [InlineData("Variant", "CDec(1)")]
    [InlineData("Variant", "CCur(1)")]
    [InlineData("Variant", "True")]
    [InlineData("Variant", "#1/1/2000#")]
    public async Task Unmeasured_domains_refuse_untrappably(string declaredType, string number)
    {
        foreach (var op in new[] { "=", "<>", "<", ">", "<=", ">=" })
        foreach (var expression in new[] { $"s {op} {number}", $"{number} {op} s" })
        {
            var run = () => Run($"Dim s As {declaredType}\ns = \"1\"\nOn Error Resume Next\nDebug.Print {expression}");
            (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*#211*");
        }
    }

}
