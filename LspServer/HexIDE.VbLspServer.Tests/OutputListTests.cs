using Antlr4.Runtime.Tree;

namespace HexIDE.VbLspServer.Tests;

// vb6.exe SP6, native and p-code: https://github.com/hexide-io/HexIDE/pull/3249 (#3248).
public class OutputListTests
{
    [Theory]
    [InlineData("Print #ff, Spc(2) - 1")]
    [InlineData("Print #ff, Spc(2)-1")]
    [InlineData("Print #ff, Tab - 1")]
    [InlineData("Print #ff, Tab-1")]
    [InlineData("Print #ff, Tab(2) + 1")]
    [InlineData("Print #ff, 1(2)")]
    [InlineData("Print #ff, 1 (2)")]
    [InlineData("Print #ff, 1(2)(3)")]
    [InlineData("Print #ff, \"a\"x")]
    [InlineData("Print #ff, Spc(1)Tab(4)\"b\"")]
    [InlineData("Write #ff, 1 2")]
    [InlineData("Print #ff, F (2)")]
    [InlineData("Print #ff, F(2)")]
    [InlineData("Print #ff, Spc(2); \"x\"")]
    [InlineData("Print #ff, Tab(2); \"x\"")]
    [InlineData("Debug.Print 1(2)")]
    public void Measured_accepted_forms_have_no_diagnostics(string statement)
    {
        var (diagnostics, tree) = VbDiagnosticsProvider.GetDiagnosticsAndTree("Function F(n)\nF = n * 10\nEnd Function\nSub Main()\nDim x, ff As Integer\nx = \"x\"\n" + statement + "\nEnd Sub\n");
        diagnostics.Should().BeEmpty();
        if (statement.Contains(" - 1", StringComparison.Ordinal) || statement.EndsWith("-1", StringComparison.Ordinal)
            || statement.Contains(" + 1", StringComparison.Ordinal))
        {
            var list = Trees.FindAllRuleNodes(tree!, VisualBasic6Parser.RULE_outputList)
                .Cast<VisualBasic6Parser.OutputListContext>().Single();
            list.outputList_Expression().Should().HaveCount(2);
            list.outputList_Expression(0).valueStmt().Should().BeNull();
            list.outputList_Expression(1).valueStmt().Should().NotBeNull();
        }
    }

    [Theory]
    [InlineData("Print #ff, Spc(2) & \"x\"")]
    [InlineData("Print #ff, Spc(2)&\"x\"")]
    [InlineData("Print #ff, Tab(2) & \"x\"")]
    [InlineData("Print #ff, Tab & \"x\"")]
    [InlineData("Print #ff, Tab(2) = 5")]
    [InlineData("Print #ff, Spc(2) = 5")]
    [InlineData("Print #ff, Tab = 5")]
    [InlineData("Print #ff, x; Tab(2) = 5")]
    [InlineData("Debug.Print Tab(2) & \"x\"")]
    // Existing refusals measured in corpus/conformance/results.json.
    [InlineData("Debug.Print ab _\nDebug.Print \"AFTER\"")]
    [InlineData("If x Then Debug.Print \"A\" ElseIf y Then Debug.Print \"B\"")]
    public void Measured_refused_forms_have_syntax_diagnostics(string statement)
    {
        VbDiagnosticsProvider.GetDiagnostics("Sub Main()\n" + statement + "\nEnd Sub\n")
            .Should().Contain(d => d.Message.StartsWith("Syntax error", StringComparison.Ordinal));
    }

    // Preserve existing expression grouping while admitting adjacent items (#3248).
    [Theory]
    [InlineData("F (2)")]
    [InlineData("[F](2)")]
    [InlineData("F(2) + 1")]
    [InlineData("obj.Item(1)")]
    [InlineData("obj(1)")]
    [InlineData("obj!key")]
    [InlineData("1 - 2")]
    [InlineData("1 + -2")]
    [InlineData("-(1 + -2)")]
    [InlineData("F(-2) + -1")]
    [InlineData("1 + F(-2)")]
    [InlineData("Spc(2) * 1")]
    [InlineData("Tab Mod 2")]
    [InlineData("Tab()")]
    [InlineData("Spc()")]
    public void Existing_expressions_stay_one_output_item(string expression)
    {
        var source = "Sub Main()\nPrint #ff, " + expression + "\nEnd Sub\n";
        var (diagnostics, tree) = VbDiagnosticsProvider.GetDiagnosticsAndTree(source);
        diagnostics.Should().BeEmpty();
        var list = Trees.FindAllRuleNodes(tree!, VisualBasic6Parser.RULE_outputList)
            .Cast<VisualBasic6Parser.OutputListContext>().Single();
        list.outputList_Expression().Should().ContainSingle();
        list.outputList_Expression(0).valueStmt().Should().NotBeNull();
    }

    [Fact]
    public void An_inline_Else_still_starts_the_other_branch()
    {
        const string source = "Sub Main()\nIf True Then Debug.Print 1 Else Debug.Print 2\nEnd Sub\n";
        var (diagnostics, tree) = VbDiagnosticsProvider.GetDiagnosticsAndTree(source);
        diagnostics.Should().BeEmpty();
        Trees.FindAllRuleNodes(tree!, VisualBasic6Parser.RULE_inlineIfBody).Should().HaveCount(2);
    }
}
