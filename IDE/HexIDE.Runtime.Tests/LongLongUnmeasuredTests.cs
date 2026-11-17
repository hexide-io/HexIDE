using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Interpreter.ConditionalCompilation;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Tests;

// #4302 supplies only the measured intrinsic/operator rows; the remaining VBA7 measurements belong to #1548.
public class LongLongUnmeasuredTests : BaseVBTestFixture
{
    public LongLongUnmeasuredTests() => Compilation = CompilationConstants.ForProject(
        new ProjectDefinition(VBProjectType.EXE, "Test") { VbaBitness = VbaBitness.Bit64 });

    [Theory]
    [InlineData("Debug.Print 9223372036854775808^")]
    [InlineData("Debug.Print &H10000000000000000^")]
    [InlineData("Debug.Print CLngLng(5) = CCur(5)")]
    [InlineData("Select Case CLngLng(5)\nCase 5#\nDebug.Print \"hit\"\nEnd Select")]
    [InlineData("Debug.Print CSng(CLngLng(5))")]
    [InlineData("Debug.Print CCur(CLngLng(5))")]
    [InlineData("Debug.Print CDec(CLngLng(5))")]
    [InlineData("Debug.Print CBool(CLngLng(5))")]
    [InlineData("Debug.Print CDate(CLngLng(5))")]
    [InlineData("Debug.Print CVar(CLngLng(5))")]
    [InlineData("Debug.Print Round(CLngLng(5))")]
    [InlineData("Debug.Print IsNumeric(CLngLng(5))")]
    [InlineData("Dim n As LongLong\nn = 5\nDebug.Print Len(n)")]
    [InlineData("v = CLngLng(5)\nDebug.Print LenB(v)")]
    [InlineData("Debug.Print Mid(\"abc\", CLngLng(1))")]
    [InlineData("Debug.Print Format(5, CLngLng(1))")]
    [InlineData("Dim y As Long\ny = 3\nDebug.Print y^ + 1")]
    [InlineData("Debug.Print CLngLng(7) And Empty")]
    [InlineData("Debug.Print Empty And CLngLng(7)")]
    [InlineData("Debug.Print CLngLng(7) And CDate(2)")]
    [InlineData("Debug.Print CDate(2) And CLngLng(7)")]
    [InlineData("Debug.Print CLngLng(7) Or Empty")]
    [InlineData("Debug.Print Empty Or CLngLng(7)")]
    [InlineData("Debug.Print CLngLng(7) Or CDate(2)")]
    [InlineData("Debug.Print CDate(2) Or CLngLng(7)")]
    [InlineData("Debug.Print CLngLng(7) Xor Empty")]
    [InlineData("Debug.Print Empty Xor CLngLng(7)")]
    [InlineData("Debug.Print CLngLng(7) Xor CDate(2)")]
    [InlineData("Debug.Print CDate(2) Xor CLngLng(7)")]
    [InlineData("Debug.Print CLngLng(7) Eqv Empty")]
    [InlineData("Debug.Print Empty Eqv CLngLng(7)")]
    [InlineData("Debug.Print CLngLng(7) Eqv CDate(2)")]
    [InlineData("Debug.Print CDate(2) Eqv CLngLng(7)")]
    [InlineData("Debug.Print CLngLng(7) Imp Empty")]
    [InlineData("Debug.Print Empty Imp CLngLng(7)")]
    [InlineData("Debug.Print CLngLng(7) Imp CDate(2)")]
    [InlineData("Debug.Print CDate(2) Imp CLngLng(7)")]
    [InlineData("Function F() As Long\nF = 3\nEnd Function\nDebug.Print F^() + 1")]
    public async Task UnmeasuredUseRefuses(string code)
    {
        var act = () => Run(code + "\n");
        await act.Should().ThrowAsync<NotImplementedException>().WithMessage("*#1548*");
    }
    [Theory]
    [InlineData("CLngLng(7) \\ CDbl(9.2233720368547758E+18)")]
    [InlineData("CDbl(9.2233720368547758E+18) \\ CLngLng(7)")]
    [InlineData("CLngLng(7) Mod CDec(\"9223372036854775808\")")]
    [InlineData("CDec(\"9223372036854775808\") Mod CLngLng(7)")]
    [InlineData("CLngLng(7) And \"9223372036854775808\"")]
    [InlineData("\"9223372036854775808\" And CLngLng(7)")]
    public async Task UnmeasuredOperandRangeRefuses(string expression)
    {
        // #4302 measures rounding of in-range partners, not an out-of-range operand's coercion diagnostic.
        var act = () => Run($"Debug.Print {expression}\n");
        await act.Should().ThrowAsync<NotImplementedException>().WithMessage("*#1548*");
    }

}
