using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// #3213, "The rule, measured", section 1; oracle tables in measuring PR #3215.
public class ForEachCollectionGrammarTests
{
    [Theory]
    [InlineData("arr")]
    [InlineData("v")]
    [InlineData("Me")]
    [InlineData("o.Items")]
    [InlineData("Me.Items")]
    [InlineData("Module2.Items()")]
    [InlineData("BoomV()")]
    [InlineData("MakeArr(2)")]
    [InlineData("Items(5)")]
    [InlineData("Array(1, 2)")]
    [InlineData("Split(\"a b\")")]
    [InlineData("c!k")]
    public void A_collection_is_a_reference(string collection)
    {
        var source = $"Sub Main()\nFor Each x In {collection}\nNext\nEnd Sub\n";
        var parse = () => new SyntaxChecker().Run(source);
        parse.Should().NotThrow();
    }

    [Theory]
    [InlineData("(arr)")]
    [InlineData("(BoomV())")]
    [InlineData("((BoomArr()))")]
    [InlineData("42")]
    [InlineData("\"abc\"")]
    [InlineData("Nothing")]
    // Literal keywords both grammars read as names: vb6.exe gives "Syntax error" for each (#3213, oracle round 3, PR #3220).
    [InlineData("True")]
    [InlineData("False")]
    [InlineData("Null")]
    [InlineData("Empty")]
    [InlineData("New Collection")]
    [InlineData("-BoomV()")]
    [InlineData("BoomV() + 0")]
    [InlineData("BoomV() & \"\"")]
    public void Other_expressions_are_syntax_errors(string collection)
    {
        var source = $"Sub Main()\nFor Each x In {collection}\nNext\nEnd Sub\n";
        var parse = () => new SyntaxChecker().Run(source);
        parse.Should().Throw<VBCompileErrorException>();
    }

    [Fact]
    public void A_With_member_remains_a_reference()
    {
        const string source = "Sub Main()\nWith c\nFor Each x In .Item(\"k\")\nNext\nEnd With\nEnd Sub\n";
        new SyntaxChecker().Run(source);
    }
}
