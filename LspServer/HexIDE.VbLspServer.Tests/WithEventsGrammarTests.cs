using Antlr4.Runtime.Tree;
using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

public class WithEventsGrammarTests
{
    [Theory]
    [InlineData("Public WithEvents A As Class2, WithEvents B As Class2")]
    [InlineData("Private WithEvents A As Class2, WithEvents B As Class2")]
    [InlineData("Dim WithEvents A As Class2, WithEvents B As Class2")]
    [InlineData("Public A As Class2, WithEvents B As Class2")]
    public void WithEvents_CanPrecedeAnyDeclarator(string declaration)
    {
        VbDiagnosticsProvider.GetDiagnostics(declaration + "\n").Should().BeEmpty();
    }

    [Fact]
    public void AnOrdinaryDeclaratorNamedWithEvents_KeepsItsName()
    {
        var (diagnostics, tree) = VbDiagnosticsProvider.GetDiagnosticsAndTree(
            "Dim WithEvents As Long, other As Long\n");
        diagnostics.Should().BeEmpty();
        var names = Trees.FindAllRuleNodes(tree!, VisualBasic6Parser.RULE_variableSubStmt)
            .Cast<VisualBasic6Parser.VariableSubStmtContext>()
            .Select(v => v.ambiguousIdentifier().GetText());
        names.Should().Equal("WithEvents", "other");
    }
}
