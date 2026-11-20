using Antlr4.Runtime;
using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

// #988: docs/vb6-fidelity-oracle/the-drawing-methods-line-pset-circle-cls-and-point.md.
public class DrawingStatementTests
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
        var parser = new VisualBasic6Parser(new CommonTokenStream(new VisualBasic6Lexer(new AntlrInputStream("Sub Main()\n" + statement + "\nEnd Sub\n"))));
        var tree = parser.startRule();
        parser.NumberOfSyntaxErrors.Should().Be(0);
        tree.ToStringTree(parser).Should().Contain("(" + rule + " ");
        VbDiagnosticsProvider.GetDiagnostics("Sub Main()\n" + statement + "\nEnd Sub\n").Should().BeEmpty();
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
        VbDiagnosticsProvider.GetDiagnostics("Sub Main()\n" + statement + "\nEnd Sub\n").Should().NotBeEmpty();
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
        VbDiagnosticsProvider.GetDiagnostics("Sub Main()\n" + statement + "\nEnd Sub\n").Should().BeEmpty();
    }

}
