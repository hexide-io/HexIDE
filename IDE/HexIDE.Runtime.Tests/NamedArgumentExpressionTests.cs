using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// VB6 SP6 native and p-code measurements: https://github.com/hexide-io/HexIDE/issues/4851.
public class NamedArgumentExpressionTests : BaseVBTestFixture
{
    private const string F = """
        Private Function F(Optional ByVal n As Variant) As Variant
            F = n
        End Function

        """;

    [Theory]
    [InlineData("i + 1", "2", "Long")]
    [InlineData("1 + 2 * 3", "7", "Integer")]
    [InlineData("i = 1", "True", "Boolean")]
    [InlineData("i Or 2", "3", "Long")]
    [InlineData("i & \"2\"", "12", "String")]
    [InlineData("-i + 3", "2", "Long")]
    public async Task A_named_value_takes_the_whole_expression(string expression, string value, string type)
    {
        await Run(F + $"""
            Dim i As Long, result As Variant
            i = 1
            result = F(n:={expression})
            Debug.Print "" & result
            Debug.Print TypeName(result)

            """);
        AssertDebugLog([new Vb6Value(value), new Vb6Value(type)]);
    }

    [Fact]
    public async Task Collection_Before_takes_the_whole_expression()
    {
        await Run(F + """
            Dim c As Collection, i As Long
            Set c = New Collection
            i = 0
            c.Add "a", "ka"
            c.Add "b", "kb", Before:=i + 1
            Debug.Print c(1) & c(2)

            """);
        AssertDebugLog([new Vb6Value("ba")]);
    }

    [Fact]
    public async Task Mid_Start_takes_the_whole_expression()
    {
        await Run(F + """
            Dim i As Long
            i = 1
            Debug.Print Mid$(String:="abcd", Start:=i + 1)

            """);
        AssertDebugLog([new Vb6Value("bcd")]);
    }

    [Fact]
    public void The_call_argument_is_an_assignment_containing_the_addition()
    {
        var parser = new VB6Parser(new CommonTokenStream(new VB6Lexer(new AntlrInputStream("F(n:=i + 1)"))));
        var tree = parser.valueStmt();
        parser.NumberOfSyntaxErrors.Should().Be(0);
        var argument = Trees.FindAllRuleNodes(tree, VB6Parser.RULE_argCall)
            .Cast<VB6Parser.ArgCallContext>().Single();
        var assignment = argument.valueStmt().Should().BeOfType<VB6Parser.VsAssignContext>().Subject;
        assignment.valueStmt().Should().BeOfType<VB6Parser.VsAddContext>();
    }

    [Fact]
    public void A_positional_expression_keeps_its_operator_precedence()
    {
        var parser = new VB6Parser(new CommonTokenStream(new VB6Lexer(new AntlrInputStream("F(i + 1 * 2)"))));
        var tree = parser.valueStmt();
        parser.NumberOfSyntaxErrors.Should().Be(0);
        var argument = Trees.FindAllRuleNodes(tree, VB6Parser.RULE_argCall)
            .Cast<VB6Parser.ArgCallContext>().Single();
        var addition = argument.valueStmt().Should().BeOfType<VB6Parser.VsAddContext>().Subject;
        addition.valueStmt(1).Should().BeOfType<VB6Parser.VsMultContext>();
    }

    [Fact]
    public async Task A_class_initialized_collection_accepts_the_demo_call_shape()
    {
        await RunClasses("""
            Dim holder As Holder
            Set holder = New Holder
            holder.Insert 0

            """, ("Holder", """
            Private p As Collection
            Private Sub Class_Initialize()
                Set p = New Collection
                p.Add "a", "ka"
            End Sub
            Public Sub Insert(ByVal n As Long)
                p.Add Array(1, 2), "k", Before:=n + 1
                Debug.Print "" & p.Count
            End Sub

            """));
        AssertDebugLog([new Vb6Value("2")]);
    }
}
