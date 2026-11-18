using System.Globalization;
using Antlr4.Runtime.Tree;
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

    // VB6 SP6 native and p-code measurements: https://github.com/hexide-io/HexIDE/issues/4626.
    [Theory]
    [InlineData("LSet .S = \"ab\"", true)]
    [InlineData("RSet .S = \"ab\"", true)]
    [InlineData("LSet b.S = \"ab\"", false)]
    public async Task ClassMember_RequiresVariable(string statement, bool withTarget)
    {
        var body = withTarget ? $"With b\n{statement}\nEnd With" : statement;
        var act = async () => await RunClasses($"Dim b As Box\nSet b = New Box\n{body}\n",
            ("Box", "Public S As String\n"));
        (await act.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should()
            .Contain("Variable required - can't assign to this expression");
    }

    [Theory]
    [InlineData("LSet", typeof(VB6Parser.LsetStmtContext))]
    [InlineData("RSet", typeof(VB6Parser.RsetStmtContext))]
    [InlineData("Let", typeof(VB6Parser.LetStmtContext))]
    public void WithMember_ParsesAsStatement(string keyword, Type statementType)
    {
        var vb = new BasicInterpreter(new MockStdLib(debug, written, dialogs), context, rootEnv,
            $"Sub Main()\nWith b\n{keyword} .S = \"ab\"\nEnd With\nEnd Sub\n");
        var statements = Trees.FindAllRuleNodes(vb.PrePass.Procedures["Main"].Body, VB6Parser.RULE_blockStmt);
        statements.Should().Contain(node => node.GetChild(0).GetType() == statementType);
    }

    // Existing behaviour explicitly retained by #4626.
    [Theory]
    [InlineData("LSet", "[ab  ]")]
    [InlineData("RSet", "[  ab]")]
    public async Task PlainString_KeepsJustification(string keyword, string expected)
    {
        await Run($"Dim s As String\ns = \"wxyz\"\n{keyword} s = \"ab\"\nDebug.Print \"[\" & s & \"]\"\n");
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Fact]
    public async Task UdtMember_KeepsRefusal()
    {
        var act = async () => await Run("Type Bag\nS As String\nEnd Type\nDim u As Bag\nLSet u.S = \"ab\"\n");
        (await act.Should().ThrowAsync<NotImplementedException>()).Which.Message.Should()
            .Be("LSet/RSet into this target is not implemented");
    }

}
