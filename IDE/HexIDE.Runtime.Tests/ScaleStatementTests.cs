using Antlr4.Runtime;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// #990, docs/vb6-fidelity-oracle/the-coordinate-system-scalemode-scale-scalex-and-scaley.md.
public class ScaleStatementTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("Scale (0, 100)-(10, 0)")]
    [InlineData("Me.Scale (0, 100)-(10, 0)")]
    [InlineData("Form1.Scale (0, 0)-(1, 1)")]
    [InlineData("Picture1.Scale (0, 100)-(10, 0)")]
    [InlineData("Scale")]
    [InlineData("Me.Scale")]
    public void Scale_is_a_statement(string statement)
    {
        var parser = Parser("Sub Main()\n" + statement + "\nEnd Sub\n");
        var tree = parser.startRule();
        parser.NumberOfSyntaxErrors.Should().Be(0);
        tree.ToStringTree(parser).Should().Contain("(scaleStmt ");
    }

    [Theory]
    [InlineData("Dim Scale As Integer")]
    [InlineData("Scale:")]
    [InlineData("End Sub\nSub Scale()")]
    public void Scale_is_reserved(string statement)
    {
        var parser = Parser("Sub Main()\n" + statement + "\nEnd Sub\n");
        parser.startRule();
        parser.NumberOfSyntaxErrors.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ScaleMode_still_parses_as_an_assignment()
    {
        var parser = Parser("Sub Main()\nScaleMode = 3\nEnd Sub\n");
        var tree = parser.startRule();
        parser.NumberOfSyntaxErrors.Should().Be(0);
        tree.ToStringTree(parser).Should().Contain("(letStmt ");
    }

    [Fact]
    public async Task A_bare_Scale_in_a_standard_module_needs_a_suitable_object()
    {
        Func<Task> run = () => Run("Scale (0, 0)-(1, 1)");
        await run.Should().ThrowAsync<VBCompileErrorException>().WithMessage("*Method not valid without suitable object*");
    }

    private static VB6Parser Parser(string source)
    {
        var parser = new VB6Parser(new CommonTokenStream(new VB6Lexer(new AntlrInputStream(source))));
        parser.RemoveErrorListeners();
        return parser;
    }
}
