using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Measured on vb6.exe, native and p-code (#1073): docs/vb6-fidelity-oracle/operator-precedence.md.
// The overflow expectations follow p-code.
public class NegativeLiteralTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("-2 ^ 2", "-4|Double")]
    [InlineData("- 2 ^ 2", "-4|Double")]
    [InlineData("-(2) ^ 2", "-4|Double")]
    [InlineData("(-2) ^ 2", "4|Double")]
    [InlineData("-x ^ 2", "-25|Double")]
    [InlineData("32767", "32767|Integer")]
    [InlineData("-32767", "-32767|Integer")]
    [InlineData("-32768", "-32768|Long")]
    [InlineData("-2147483647", "-2147483647|Long")]
    [InlineData("-2147483648", "-2147483648|Double")]
    [InlineData("-1.5", "-1.5|Double")]
    [InlineData("-1.5!", "-1.5|Single")]
    [InlineData("-1@", "-1|Currency")]
    [InlineData("-1&", "-1|Long")]
    [InlineData("-1%", "-1|Integer")]
    [InlineData("x-1", "4|Integer")]
    [InlineData("x - -1", "6|Integer")]
    [InlineData("x*-1", "-5|Integer")]
    [InlineData("3 - -2", "5|Integer")]
    [InlineData("- -2", "2|Integer")]
    [InlineData("-&H7FFF", "-32767|Integer")]
    [InlineData("-&HFFFF", "1|Integer")]
    [InlineData("-&O10", "-8|Integer")]
    [InlineData("-&H8000&", "-32768|Long")]
    public async Task Negation_follows_power_and_preserves_the_digits_type(string expression, string expected)
    {
        await Run($"Dim x As Integer\nx = 5\nDim v\nv = {expression}\nDebug.Print CStr(v) & \"|\" & TypeName(v)\n");
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("Const C = -2 ^ 2", "-4|Double")]
    [InlineData("Const C = -32768", "-32768|Long")]
    [InlineData("Const C As Integer = -32768", "-32768|Integer")]
    [InlineData("Dim C As Integer\nC = -32768", "-32768|Integer")]
    public async Task A_declaration_converts_after_negation(string declaration, string expected)
    {
        await Run($"{declaration}\nDebug.Print CStr(C) & \"|\" & TypeName(C)\n");
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("v = -&H8000")]
    [InlineData("Dim h As Integer\nh = &H8000\nv = -h")]
    public async Task Negating_the_minimum_Integer_overflows_in_p_code(string statement)
    {
        await Run($"On Error Resume Next\nDim v\n{statement}\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(6L)]);
    }

    [Theory]
    [InlineData("Const C = -&H8000", false)]
    [InlineData("Const C As Integer = -32769", true)]
    public async Task An_overflowing_constant_is_refused(string declaration, bool conversion)
    {
        var run = () => Run($"{declaration}\n");
        if (conversion)
            (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Overflow*");
        else
            // The existing evaluator reports this compile-time Overflow as it executes the initializer.
            (await run.Should().ThrowAsync<VBRunTimeException>()).WithMessage("*Overflow*");
    }

    [Theory]
    [InlineData("VERSION 1.0 CLASS\nBEGIN\nMultiUse = -1\nEND\n")]
    [InlineData("VERSION 5.00\nBegin VB.Form Form1\nLeft = -1\nEnd\n")]
    [InlineData("VERSION 5.00\nBegin VB.Form Form1\nLeft = -1.5\nEnd\n")]
    [InlineData("Attribute VB_Description = -1\n")]
    public async Task Signed_metadata_still_loads(string header)
    {
        await Run($"{header}Debug.Print 1\n");
        AssertDebugLog([new Vb6Value(1)]);
    }

}
