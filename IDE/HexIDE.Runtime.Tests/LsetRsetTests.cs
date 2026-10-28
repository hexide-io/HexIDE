using System.Globalization;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class LsetRsetTests : BaseVBTestFixture
{
    public static IEnumerable<object[]> MeasuredRows()
    {
        foreach (var statement in new[] { "LSet", "RSet" })
        {
            var right = statement == "RSet";
            yield return [statement, "String", "\"abcde\"", "\"xy\"", right ? "   xy" : "xy   ", 0];
            yield return [statement, "String", "\"abc\"", "\"123456\"", "123", 0];
            yield return [statement, "String", "\"abc\"", "\"\"", "   ", 0];
            yield return [statement, "String", "\"\"", "\"xy\"", "", 0];
            yield return [statement, "String", "\"abcde\"", "12", right ? "   12" : "12   ", 0];
            yield return [statement, "String", "\"abcde\"", "1.5", right ? "  1.5" : "1.5  ", 0];
            yield return [statement, "String", "\"abcde\"", "True", right ? " True" : "True ", 0];
            yield return [statement, "String", "\"abcde\"", "Null", "abcde", 94];
            yield return [statement, "String", "\"abcde\"", "Empty", "     ", 0];
            yield return [statement, "String", "\"abcde\"", "\"xyz\" & \"w\"", right ? " xyzw" : "xyzw ", 0];
            yield return [statement, "Variant", "\"abcde\"", "\"x\"", right ? "    x" : "x    ", 0];
            yield return [statement, "Variant", "12345", "\"x\"", right ? "    x" : "x    ", 0];
        }
    }

    [Theory]
    [MemberData(nameof(MeasuredRows))]
    public async Task StringAssignment_MatchesMeasuredRow(string statement, string declaration, string initial,
        string value, string expected, int error)
    {
        using var locale = VbLocale.Use(CultureInfo.GetCultureInfo("en-GB"));
        await Run($"Dim target As {declaration}\ntarget = {initial}\nOn Error Resume Next\n{statement} target = {value}\nDebug.Print Err.Number\nDebug.Print target\nDebug.Print Len(target)\nDebug.Print TypeName(target)\n");
        AssertDebugLog([new Vb6Value((long)error), new Vb6Value(expected), new Vb6Value((long)expected.Length), new Vb6Value("String")]);
    }
}
