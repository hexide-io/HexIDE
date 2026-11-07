using System;
using System.Globalization;
using System.Threading.Tasks;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class NumericFormatFunctionsTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("FormatNumber(1234.5678)", "1,234.57", 0)]
    [InlineData("FormatNumber(-1234.5678)", "-1,234.57", 0)]
    [InlineData("FormatNumber(0.5)", "0.50", 0)]
    [InlineData("FormatNumber(-0.5)", "-0.50", 0)]
    [InlineData("FormatNumber(0)", "0.00", 0)]
    [InlineData("FormatNumber(1E+20)", "100,000,000,000,000,000,000.00", 0)]
    [InlineData("FormatNumber(\"123.4\")", "123.40", 0)]
    [InlineData("FormatNumber(Null)", "", 0)]
    [InlineData("FormatNumber(Empty)", "", 0)]
    [InlineData("FormatNumber(True)", "-1.00", 0)]
    [InlineData("FormatNumber(CDec(\"1234.5\"))", "1,234.50", 0)]
    [InlineData("FormatNumber(0.005)", "0.01", 0)]
    [InlineData("FormatNumber(0.015)", "0.02", 0)]
    [InlineData("FormatNumber(0.025)", "0.03", 0)]
    [InlineData("FormatNumber(2.5, 0)", "3", 0)]
    [InlineData("FormatNumber(3.5, 0)", "4", 0)]
    [InlineData("FormatNumber(1234567.891)", "1,234,567.89", 0)]
    [InlineData("FormatNumber(\"abc\")", "", 13)]
    [InlineData("FormatNumber(CCur(12.345))", "12.35", 0)]
    [InlineData("FormatNumber(#2026-10-28#)", "46,323.00", 0)]
    [InlineData("FormatNumber(1234.5678, 0)", "1,235", 0)]
    [InlineData("FormatNumber(1234.5678, 3)", "1,234.568", 0)]
    [InlineData("FormatNumber(1234.5678, -1)", "1,234.57", 0)]
    [InlineData("FormatNumber(1234.5678, 2.6)", "1,234.568", 0)]
    [InlineData("FormatNumber(1234.5678, 99)", "1,234.567800000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000", 0)]
    [InlineData("FormatNumber(1234.5678, -2)", "", 5)]
    [InlineData("FormatNumber(1234.5678, 256)", "1,235.", 0)]
    [InlineData("FormatNumber(1234.5678, Null)", "", 94)]
    [InlineData("FormatNumber(0.5, , vbTrue)", "0.50", 0)]
    [InlineData("FormatNumber(0.5, , vbFalse)", ".50", 0)]
    [InlineData("FormatNumber(0.5, , vbUseDefault)", "0.50", 0)]
    [InlineData("FormatNumber(0.5, , 5)", "", 5)]
    [InlineData("FormatNumber(-1234.5, , , vbTrue)", "(1,234.50)", 0)]
    [InlineData("FormatNumber(-1234.5, , , vbFalse)", "-1,234.50", 0)]
    [InlineData("FormatNumber(1234.5, , , , vbFalse)", "1234.50", 0)]
    [InlineData("FormatNumber(1234.5, , , , vbTrue)", "1,234.50", 0)]
    [InlineData("FormatNumber(-0.5, 2, vbFalse, vbTrue, vbFalse)", "(.50)", 0)]
    [InlineData("FormatCurrency(1234.5678)", "£1,234.57", 0)]
    [InlineData("FormatCurrency(-1234.5678)", "-£1,234.57", 0)]
    [InlineData("FormatCurrency(0.5)", "£0.50", 0)]
    [InlineData("FormatCurrency(-0.5)", "-£0.50", 0)]
    [InlineData("FormatCurrency(0)", "£0.00", 0)]
    [InlineData("FormatCurrency(1E+20)", "£100,000,000,000,000,000,000.00", 0)]
    [InlineData("FormatCurrency(\"123.4\")", "£123.40", 0)]
    [InlineData("FormatCurrency(Null)", "", 0)]
    [InlineData("FormatCurrency(Empty)", "", 0)]
    [InlineData("FormatCurrency(True)", "-£1.00", 0)]
    [InlineData("FormatCurrency(CDec(\"1234.5\"))", "£1,234.50", 0)]
    [InlineData("FormatCurrency(0.005)", "£0.01", 0)]
    [InlineData("FormatCurrency(0.015)", "£0.02", 0)]
    [InlineData("FormatCurrency(0.025)", "£0.03", 0)]
    [InlineData("FormatCurrency(2.5, 0)", "£3", 0)]
    [InlineData("FormatCurrency(3.5, 0)", "£4", 0)]
    [InlineData("FormatCurrency(1234567.891)", "£1,234,567.89", 0)]
    [InlineData("FormatCurrency(\"abc\")", "", 13)]
    [InlineData("FormatCurrency(CCur(12.345))", "£12.35", 0)]
    [InlineData("FormatCurrency(#2026-10-28#)", "£46,323.00", 0)]
    [InlineData("FormatCurrency(1234.5678, 0)", "£1,235", 0)]
    [InlineData("FormatCurrency(1234.5678, 3)", "£1,234.568", 0)]
    [InlineData("FormatCurrency(1234.5678, -1)", "£1,234.57", 0)]
    [InlineData("FormatCurrency(1234.5678, 2.6)", "£1,234.568", 0)]
    [InlineData("FormatCurrency(1234.5678, 99)", "£1,234.567800000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000", 0)]
    [InlineData("FormatCurrency(1234.5678, -2)", "", 5)]
    [InlineData("FormatCurrency(1234.5678, 256)", "", 5)]
    [InlineData("FormatCurrency(1234.5678, Null)", "", 94)]
    [InlineData("FormatCurrency(0.5, , vbTrue)", "£0.50", 0)]
    [InlineData("FormatCurrency(0.5, , vbFalse)", "£.50", 0)]
    [InlineData("FormatCurrency(0.5, , vbUseDefault)", "£0.50", 0)]
    [InlineData("FormatCurrency(0.5, , 5)", "", 5)]
    [InlineData("FormatCurrency(-1234.5, , , vbTrue)", "(£1,234.50)", 0)]
    [InlineData("FormatCurrency(-1234.5, , , vbFalse)", "-£1,234.50", 0)]
    [InlineData("FormatCurrency(1234.5, , , , vbFalse)", "£1234.50", 0)]
    [InlineData("FormatCurrency(1234.5, , , , vbTrue)", "£1,234.50", 0)]
    [InlineData("FormatCurrency(-0.5, 2, vbFalse, vbTrue, vbFalse)", "(£.50)", 0)]
    [InlineData("FormatPercent(1234.5678)", "123,456.78%", 0)]
    [InlineData("FormatPercent(-1234.5678)", "-123,456.78%", 0)]
    [InlineData("FormatPercent(0.5)", "50.00%", 0)]
    [InlineData("FormatPercent(-0.5)", "-50.00%", 0)]
    [InlineData("FormatPercent(0)", "0.00%", 0)]
    [InlineData("FormatPercent(1E+20)", "10,000,000,000,000,000,000,000.00%", 0)]
    [InlineData("FormatPercent(\"123.4\")", "12,340.00%", 0)]
    [InlineData("FormatPercent(Null)", "", 0)]
    [InlineData("FormatPercent(Empty)", "", 0)]
    [InlineData("FormatPercent(True)", "-100.00%", 0)]
    [InlineData("FormatPercent(CDec(\"1234.5\"))", "123,450.00%", 0)]
    [InlineData("FormatPercent(0.005)", "0.50%", 0)]
    [InlineData("FormatPercent(0.015)", "1.50%", 0)]
    [InlineData("FormatPercent(0.025)", "2.50%", 0)]
    [InlineData("FormatPercent(2.5, 0)", "250%", 0)]
    [InlineData("FormatPercent(3.5, 0)", "350%", 0)]
    [InlineData("FormatPercent(1234567.891)", "123,456,789.10%", 0)]
    [InlineData("FormatPercent(\"abc\")", "", 13)]
    [InlineData("FormatPercent(CCur(12.345))", "1,234.50%", 0)]
    [InlineData("FormatPercent(#2026-10-28#)", "4,632,300.00%", 0)]
    [InlineData("FormatPercent(1234.5678, 0)", "123,457%", 0)]
    [InlineData("FormatPercent(1234.5678, 3)", "123,456.780%", 0)]
    [InlineData("FormatPercent(1234.5678, -1)", "123,456.78%", 0)]
    [InlineData("FormatPercent(1234.5678, 2.6)", "123,456.780%", 0)]
    [InlineData("FormatPercent(1234.5678, 99)", "123,456.780000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000%", 0)]
    [InlineData("FormatPercent(1234.5678, -2)", "", 5)]
    [InlineData("FormatPercent(1234.5678, 256)", "123,457.%", 0)]
    [InlineData("FormatPercent(1234.5678, Null)", "", 94)]
    [InlineData("FormatPercent(0.5, , vbTrue)", "50.00%", 0)]
    [InlineData("FormatPercent(0.5, , vbFalse)", "50.00%", 0)]
    [InlineData("FormatPercent(0.5, , vbUseDefault)", "50.00%", 0)]
    [InlineData("FormatPercent(0.5, , 5)", "", 5)]
    [InlineData("FormatPercent(-1234.5, , , vbTrue)", "(123,450.00%)", 0)]
    [InlineData("FormatPercent(-1234.5, , , vbFalse)", "-123,450.00%", 0)]
    [InlineData("FormatPercent(1234.5, , , , vbFalse)", "123450.00%", 0)]
    [InlineData("FormatPercent(1234.5, , , , vbTrue)", "123,450.00%", 0)]
    [InlineData("FormatPercent(-0.5, 2, vbFalse, vbTrue, vbFalse)", "(50.00%)", 0)]
    public async Task MeasuredRow(string expression, string expected, int error)
    {
        using var locale = VbLocale.Use(CultureInfo.GetCultureInfo("en-GB"));
        await Run($"On Error Resume Next\nDim result As Variant\nresult = {expression}\nDebug.Print Err.Number\nIf Err.Number = 0 Then\nDebug.Print result\nDebug.Print TypeName(result)\nEnd If\n");
        if (error == 0)
            AssertDebugLog([new Vb6Value(0L), new Vb6Value(expected), new Vb6Value("String")]);
        else
            AssertDebugLog([new Vb6Value((long)error)]);
    }

    [Theory]
    [InlineData("FormatNumber")]
    [InlineData("FormatCurrency")]
    [InlineData("FormatPercent")]
    public async Task UnmeasuredNamesRefuse(string function)
    {
        Func<Task> run = () => Run($"Debug.Print {function}(Expression:=1)\n");
        await run.Should().ThrowAsync<NotImplementedException>();
    }

    [Theory]
    [InlineData("FormatNumber")]
    [InlineData("FormatCurrency")]
    [InlineData("FormatPercent")]
    public async Task UnmeasuredWideDigitsRefuse(string function)
    {
        Func<Task> run = () => Run($"Debug.Print {function}(1, 100)\n");
        await run.Should().ThrowAsync<NotImplementedException>();
    }

    [Theory]
    [InlineData("FormatNumber()")]
    [InlineData("FormatCurrency()")]
    [InlineData("FormatPercent()")]
    [InlineData("FormatNumber(, 2)")]
    [InlineData("FormatCurrency(, 2)")]
    [InlineData("FormatPercent(, 2)")]
    [InlineData("FormatNumber(1, 2, 0, 0, 0, 0)")]
    [InlineData("FormatCurrency(1, 2, 0, 0, 0, 0)")]
    [InlineData("FormatPercent(1, 2, 0, 0, 0, 0)")]
    public async Task UnmeasuredArgumentListsRefuse(string expression)
    {
        Func<Task> run = () => Run($"Debug.Print {expression}\n");
        await run.Should().ThrowAsync<NotImplementedException>();
    }


    [Theory]
    [InlineData("FormatNumber(1, -3)")]
    [InlineData("FormatCurrency(1, -3)")]
    [InlineData("FormatPercent(1, -3)")]
    [InlineData("FormatNumber(1, , 2)")]
    [InlineData("FormatCurrency(1, , 2)")]
    [InlineData("FormatPercent(1, , 2)")]
    [InlineData("FormatNumber(1, , , 2)")]
    [InlineData("FormatCurrency(1, , , 2)")]
    [InlineData("FormatPercent(1, , , 2)")]
    [InlineData("FormatNumber(1, , , , 2)")]
    [InlineData("FormatCurrency(1, , , , 2)")]
    [InlineData("FormatPercent(1, , , , 2)")]
    public async Task UnmeasuredOptionValuesRefuse(string expression)
    {
        Func<Task> run = () => Run($"Debug.Print {expression}\n");
        await run.Should().ThrowAsync<NotImplementedException>();
    }

}
