using Antlr4.Runtime.Tree;
using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

public class MemberCallParenthesesTests
{
    [Theory]
    [InlineData("Form2.Show(0)", "(0)")]
    [InlineData("Text1.Move(100)", "(100)")]
    [InlineData("Form2.Show (0)", "(0)")]
    [InlineData("Text1.Move 100", "100")]
    public void Member_statement_keeps_its_argument_expression(string statement, string argument)
    {
        var (diagnostics, tree) = VbDiagnosticsProvider.GetDiagnosticsAndTree($"Sub Test()\n{statement}\nEnd Sub\n");
        diagnostics.Should().BeEmpty();
        var call = Trees.FindAllRuleNodes(tree!, VisualBasic6Parser.RULE_iCS_B_MemberProcedureCall)
            .Cast<VisualBasic6Parser.ICS_B_MemberProcedureCallContext>().Should().ContainSingle().Subject;
        call.argsCall().argCall().Should().ContainSingle().Which.valueStmt().GetText()
            .Should().Be(argument);
    }
}
