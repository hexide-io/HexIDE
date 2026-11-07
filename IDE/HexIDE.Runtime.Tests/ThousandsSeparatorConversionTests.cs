using System;
using System.Globalization;
using System.Threading.Tasks;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class ThousandsSeparatorConversionTests : BaseVBTestFixture
{
    // Every cell of #1446's SP6 en-GB table, native and p-code identical.
    [Theory]
    [InlineData("1.5,2003", "CDbl(s)", "1.52003", null)]
    [InlineData("1.5,2003", "CLng(s)", "2", null)]
    [InlineData("1.5,2003", "Val(s)", "1.5", null)]
    [InlineData("1.5,2003", "CCur(s)", "1.52", null)]
    [InlineData("1.5,2003", "IsNumeric(s)", "True", null)]
    [InlineData("1.5,2003", "s + 0", "1.52003", null)]
    [InlineData("1.5,", "CDbl(s)", "1.5", null)]
    [InlineData("1.5,", "CLng(s)", "2", null)]
    [InlineData("1.5,", "Val(s)", "1.5", null)]
    [InlineData("1.5,", "CCur(s)", "1.5", null)]
    [InlineData("1.5,", "IsNumeric(s)", "True", null)]
    [InlineData("1.5,", "s + 0", "1.5", null)]
    [InlineData("1,.5", "CDbl(s)", "1.5", null)]
    [InlineData("1,.5", "CLng(s)", "2", null)]
    [InlineData("1,.5", "Val(s)", "1", null)]
    [InlineData("1,.5", "CCur(s)", "1.5", null)]
    [InlineData("1,.5", "IsNumeric(s)", "True", null)]
    [InlineData("1,.5", "s + 0", "1.5", null)]
    [InlineData("1.5,2,3", "CDbl(s)", "1.523", null)]
    [InlineData("1.5,2,3", "CLng(s)", "2", null)]
    [InlineData("1.5,2,3", "Val(s)", "1.5", null)]
    [InlineData("1.5,2,3", "CCur(s)", "1.523", null)]
    [InlineData("1.5,2,3", "IsNumeric(s)", "True", null)]
    [InlineData("1.5,2,3", "s + 0", "1.523", null)]
    [InlineData("1.,5", "CDbl(s)", "1.5", null)]
    [InlineData("1.,5", "CLng(s)", "2", null)]
    [InlineData("1.,5", "Val(s)", "1", null)]
    [InlineData("1.,5", "CCur(s)", "1.5", null)]
    [InlineData("1.,5", "IsNumeric(s)", "True", null)]
    [InlineData("1.,5", "s + 0", "1.5", null)]
    [InlineData("1,5.5", "CDbl(s)", "15.5", null)]
    [InlineData("1,5.5", "CLng(s)", "16", null)]
    [InlineData("1,5.5", "Val(s)", "1", null)]
    [InlineData("1,5.5", "CCur(s)", "15.5", null)]
    [InlineData("1,5.5", "IsNumeric(s)", "True", null)]
    [InlineData("1,5.5", "s + 0", "15.5", null)]
    [InlineData("12,34.5,6", "CDbl(s)", "1234.56", null)]
    [InlineData("12,34.5,6", "CLng(s)", "1235", null)]
    [InlineData("12,34.5,6", "Val(s)", "12", null)]
    [InlineData("12,34.5,6", "CCur(s)", "1234.56", null)]
    [InlineData("12,34.5,6", "IsNumeric(s)", "True", null)]
    [InlineData("12,34.5,6", "s + 0", "1234.56", null)]
    [InlineData(",1.5", "CDbl(s)", null, null)]
    [InlineData(",1.5", "CLng(s)", null, 13)]
    [InlineData(",1.5", "Val(s)", "0", null)]
    [InlineData(",1.5", "CCur(s)", null, 13)]
    [InlineData(",1.5", "IsNumeric(s)", "False", null)]
    [InlineData(",1.5", "s + 0", null, 13)]
    public async Task Measured_cell(string input, string expression, string? expected, int? error)
    {
        using var locale = VbLocale.Use(CultureInfo.GetCultureInfo("en-GB"));
        var code = $"Dim s As String\ns = \"{input}\"\nDebug.Print CStr({expression})\n";
        if (input == "1.5,2003" && expression == "s + 0")
            code += "Debug.Print TypeName(s + 0)\n";
        if (expected is null)
        {
            Func<Task> act = () => Run(code);
            var raised = (await act.Should().ThrowAsync<VBRunTimeException>()).Which;
            // CDbl's error number was overwritten in the probe; only its refusal was measured.
            if (error is { } number) raised.Error.ErrNo.Should().Be(number);
        }
        else
        {
            await Run(code);
            if (input == "1.5,2003" && expression == "s + 0")
                AssertDebugLog([new Vb6Value(expected), new Vb6Value("Double")]);
            else
                AssertDebugLog([new Vb6Value(expected)]);
        }
    }
}
