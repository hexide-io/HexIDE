// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors

using Antlr4.Runtime.Tree;
using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

public class ModuleOptionsGrammarTests
{
    [Theory]
    [InlineData("Option Explicit\n", 1)]
    [InlineData("Option Explicit\nOption Base 1\n", 2)]
    public void Options_before_a_broken_procedure_preserve_its_tree_and_diagnostics(string options, int lines)
    {
        const string source = "\nPrivate Sub A()\n    x = = 1\n    List0.\nEnd Sub\n\nPrivate Sub B2()\n    y = 1\nEnd Sub\n";
        var (plainDiagnostics, _) = VbDiagnosticsProvider.GetDiagnosticsAndTree(source);
        var (diagnostics, tree) = VbDiagnosticsProvider.GetDiagnosticsAndTree(options + source);

        Trees.FindAllRuleNodes(tree!, VisualBasic6Parser.RULE_subStmt)
            .Cast<VisualBasic6Parser.SubStmtContext>()
            .Select(s => s.ambiguousIdentifier().GetText()).Should().Contain("A");
        VbSymbolProvider.GetSymbols(tree).Select(s => s.Name).Should().Contain("A");
        plainDiagnostics.Should().NotBeEmpty();
        diagnostics.Select(d => (d.Message, d.Range.Start.Line - lines, d.Range.Start.Character, d.Range.End.Line - lines, d.Range.End.Character))
            .Should().Equal(plainDiagnostics.Select(d => (d.Message, d.Range.Start.Line, d.Range.Start.Character, d.Range.End.Line, d.Range.End.Character)));
    }

    [Theory]
    [InlineData("Option Explicit:")]
    [InlineData("Option Explicit:\n")]
    [InlineData("Option Explicit: Option Base 1:")]
    public void Options_only_keep_accepting_a_trailing_separator(string source)
    {
        VbDiagnosticsProvider.GetDiagnosticsAndTree(source).Diagnostics.Should().BeEmpty();
    }
}
