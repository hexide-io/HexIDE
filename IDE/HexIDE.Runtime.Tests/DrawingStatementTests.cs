using Antlr4.Runtime;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// #988: docs/vb6-fidelity-oracle/the-drawing-methods-line-pset-circle-cls-and-point.md, "At compile time".
public class DrawingStatementTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("Line (0, 0)-(100, 100)", "lineStmt")]
    [InlineData("Line -(100, 100)", "lineStmt")]
    [InlineData("Line -Step(100, 100)", "lineStmt")]
    [InlineData("Line Step(10, 10)-Step(100, 50), , B", "lineStmt")]
    [InlineData("Picture1.Line (0, 0)-(100, 100), vbRed, BF", "lineStmt")]
    [InlineData("Form1.Line (0, 0)-(100, 100)", "lineStmt")]
    [InlineData("o.Line (0, 0)-(100, 100)", "lineStmt")]
    [InlineData("PSet (10, 20), vbRed", "psetStmt")]
    [InlineData("PSet Step(10, 20)", "psetStmt")]
    [InlineData("Picture1.PSet (1, 1)", "psetStmt")]
    [InlineData("Circle (100, 100), 50", "circleStmt")]
    [InlineData("Circle Step(100, 100), 50, , , , 0.5", "circleStmt")]
    [InlineData("Me.Circle (100, 100), 50, vbRed, 0, 3.14, 0.5", "circleStmt")]
    [InlineData("o.Circle (100, 100), 50", "circleStmt")]
    public void Graphics_have_statement_rules(string statement, string rule)
    {
        var parser = Parser("Sub Main()\n" + statement + "\nEnd Sub\n");
        var tree = parser.startRule();
        parser.NumberOfSyntaxErrors.Should().Be(0);
        tree.ToStringTree(parser).Should().Contain("(" + rule + " ");
    }

    [Theory]
    [InlineData("Circle (100, 100), 50, Aspect:=0.5")]
    [InlineData("Line (1, 1)-(2, 2), , F")]
    [InlineData("PSet (1, 1), vbRed, B")]
    [InlineData("Line (1, 1)-(2, 2) B")]
    [InlineData("Dim Circle As Integer")]
    [InlineData("Dim PSet As Integer")]
    [InlineData("Circle:")]
    [InlineData("PSet:")]
    [InlineData("End Sub\nSub Circle()")]
    [InlineData("End Sub\nSub PSet()")]
    public void Invalid_graphics_syntax_and_reserved_names_are_rejected(string statement)
    {
        var parser = Parser("Sub Main()\n" + statement + "\nEnd Sub\n");
        parser.startRule();
        parser.NumberOfSyntaxErrors.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("Dim Line As Integer")]
    [InlineData("Dim Cls As Integer")]
    [InlineData("Line")]
    [InlineData("Line:")]
    [InlineData("Cls:")]
    [InlineData("End Sub\nPublic Sub Line()")]
    [InlineData("End Sub\nPublic Sub Cls()")]
    [InlineData("Me.Cls: Debug.Print 1")]
    public void Line_and_Cls_remain_nonreserved(string statement)
    {
        var parser = Parser("Sub Main()\n" + statement + "\nEnd Sub\n");
        parser.startRule();
        parser.NumberOfSyntaxErrors.Should().Be(0);
    }

    [Theory]
    [InlineData("Line (0, 0)-(1, 1)")]
    [InlineData("PSet (1, 1)")]
    [InlineData("Circle (1, 1), 1")]
    public async Task Bare_graphics_in_a_standard_module_need_a_suitable_object(string statement)
    {
        Func<Task> run = () => Run(statement);
        await run.Should().ThrowAsync<VBCompileErrorException>().WithMessage("*Method not valid without suitable object*");
    }

    [Fact]
    public async Task Bare_Cls_in_a_standard_module_is_an_undefined_sub()
    {
        Func<Task> run = () => Run("Cls");
        await run.Should().ThrowAsync<VBCompileErrorException>().WithMessage("*Sub or Function not defined*");
    }

    [Fact]
    public async Task Cls_at_a_line_start_is_a_label()
    {
        await Run("Sub Main()\nCls: Debug.Print 1\nEnd Sub\nMain");
        AssertDebugLog([new Vb6Value(1)]);
    }

    [Fact]
    public async Task Two_Cls_labels_are_duplicate_declarations()
    {
        Func<Task> run = () => Run("Sub Main()\nCls: Debug.Print 1\nCls: Debug.Print 2\nEnd Sub\nMain");
        await run.Should().ThrowAsync<VBCompileErrorException>().WithMessage("*Duplicate declaration in current scope*");
    }

    private static VB6Parser Parser(string source)
    {
        var parser = new VB6Parser(new CommonTokenStream(new VB6Lexer(new AntlrInputStream(source))));
        parser.RemoveErrorListeners();
        return parser;
    }
}
