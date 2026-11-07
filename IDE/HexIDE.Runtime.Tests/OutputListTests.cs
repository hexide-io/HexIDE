using System.IO;
using System.Text;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// vb6.exe SP6, native and p-code: https://github.com/hexide-io/HexIDE/pull/3249 (#3248).
public class OutputListTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("Print #ff, Spc(2) - 1", "  -1 ")]
    [InlineData("Print #ff, Spc(2)-1", "  -1 ")]
    [InlineData("Print #ff, Tab - 1", "              -1 ")]
    [InlineData("Print #ff, Tab-1", "              -1 ")]
    [InlineData("Print #ff, Tab(2) + 1", "  1 ")]
    [InlineData("Print #ff, 1(2)", " 1  2 ")]
    [InlineData("Print #ff, 1 (2)", " 1  2 ")]
    [InlineData("Print #ff, 1(2)(3)", " 1  2  3 ")]
    [InlineData("Print #ff, \"a\"x", "ax")]
    [InlineData("Print #ff, Spc(1)Tab(4)\"b\"", "   b")]
    [InlineData("Write #ff, 1 2", "1,2")]
    [InlineData("Print #ff, F (2)", " 20 ")]
    [InlineData("Print #ff, F(2)", " 20 ")]
    [InlineData("Print #ff, Spc(2); \"x\"", "  x")]
    [InlineData("Print #ff, Tab(2); \"x\"", " x")]
    public async Task File_output_matches_measured_bytes(string statement, string expected)
    {
        var path = Path.Combine(Path.GetTempPath(), "hexide-output-" + Guid.NewGuid() + ".txt");
        try
        {
            await Run($$"""
                Function F(n)
                    F = n * 10
                End Function
                Dim x, ff As Integer
                x = "x"
                ff = FreeFile
                Open "{{path}}" For Output As #ff
                {{statement}}
                Close #ff
                """);
            File.ReadAllBytes(path).Should().Equal(Encoding.ASCII.GetBytes(expected + "\r\n"));
        }
        finally { File.Delete(path); }
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
    public void Measured_syntax_errors_are_refused_at_load(string statement)
    {
        var parser = new VB6Parser(new CommonTokenStream(new VB6Lexer(new AntlrInputStream(
            "Sub Main()\n" + statement + "\nEnd Sub\n"))));
        parser.RemoveErrorListeners();
        parser.startRule();
        parser.NumberOfSyntaxErrors.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Debug_print_adjacent_items_compile()
    {
        // Only compile acceptance was measured for Debug.Print in #3249; no new output expectation.
        var parser = new VB6Parser(new CommonTokenStream(new VB6Lexer(new AntlrInputStream(
            "Sub Main()\nDebug.Print 1(2)\nEnd Sub\n"))));
        parser.RemoveErrorListeners();
        parser.startRule();
        parser.NumberOfSyntaxErrors.Should().Be(0);
    }

    // Preserve existing expression grouping while admitting adjacent items (#3248).
    [Theory]
    [InlineData("F (2)")]
    [InlineData("[F](2)")]
    [InlineData("F(2) + 1")]
    [InlineData("obj.Item(1)")]
    [InlineData("obj(1).Item(2)")]
    [InlineData("obj!key.Name")]
    [InlineData("obj.Print(1).Item(2)")]
    [InlineData("obj.Item(1).Name")]
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
        var parser = new VB6Parser(new CommonTokenStream(new VB6Lexer(new AntlrInputStream(source))));
        parser.RemoveErrorListeners();
        var tree = parser.startRule();
        parser.NumberOfSyntaxErrors.Should().Be(0);
        var list = Trees.FindAllRuleNodes(tree!, VB6Parser.RULE_outputList)
            .Cast<VB6Parser.OutputListContext>().Single();
        list.outputList_Expression().Should().ContainSingle();
        list.outputList_Expression(0).valueStmt().Should().NotBeNull();
    }

    [Fact]
    public void An_inline_Else_still_starts_the_other_branch()
    {
        const string source = "Sub Main()\nIf True Then Debug.Print 1 Else Debug.Print 2\nEnd Sub\n";
        var parser = new VB6Parser(new CommonTokenStream(new VB6Lexer(new AntlrInputStream(source))));
        parser.RemoveErrorListeners();
        var tree = parser.startRule();
        parser.NumberOfSyntaxErrors.Should().Be(0);
        Trees.FindAllRuleNodes(tree!, VB6Parser.RULE_inlineIfBody).Should().HaveCount(2);
    }
}
