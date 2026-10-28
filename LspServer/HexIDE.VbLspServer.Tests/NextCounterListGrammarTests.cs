// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// One Next closing several loops, `Next j, i`, which vb6.exe compiles and runs as `Next j: Next i` (#2393).

using Antlr4.Runtime.Tree;
using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

public class NextCounterListGrammarTests
{
    private static int Loops(IParseTree tree) =>
        Trees.FindAllRuleNodes(tree, VisualBasic6Parser.RULE_forNextStmt).Count
        + Trees.FindAllRuleNodes(tree, VisualBasic6Parser.RULE_forEachStmt).Count;

    [Theory]
    [InlineData("Sub Foo()\n    For i = 1 To 2\n        For j = 1 To 2\n            s = s & i & j\n        Next j, i\nEnd Sub\n", 2)]
    [InlineData("Sub Foo()\n    For i = 1 To 2\n        For j = 1 To 2\n        Next j,i\nEnd Sub\n", 2)]
    [InlineData("Sub Foo()\n    For i = 1 To 2: For j = 1 To 2: s = s & i: Next j , i\nEnd Sub\n", 2)]
    [InlineData("Sub Foo()\n    For i = 1 To 2\n        For j = 1 To 2\n            For k = 1 To 2\n            Next k, j, i\nEnd Sub\n", 3)]
    [InlineData("Sub Foo()\n    For i = 1 To 2\n        For j = 1 To 2\n            For k = 1 To 2\n            Next k, j\n    Next i\nEnd Sub\n", 3)]
    [InlineData("Sub Foo()\n    For Each a In c\n        For i = 1 To 2\n        Next i, a\nEnd Sub\n", 2)]
    [InlineData("Sub Foo()\n    For i% = 1 To 2\n        For j& = 1 To 2\n        Next j&, i%\nEnd Sub\n", 2)]
    [InlineData("Sub Foo()\n    For i = 1 To 2\n        For j = 1 To 2\n        Next j, _\n            i\nEnd Sub\n", 2)]
    [InlineData("Sub Foo()\n    For i = 1 To 2\n        For j = 1 To 2\n        Next j _\n            , i\nEnd Sub\n", 2)]
    public void One_Next_may_close_several_loops(string source, int loops)
    {
        var (diagnostics, tree) = VbDiagnosticsProvider.GetDiagnosticsAndTree(source);

        diagnostics.Should().BeEmpty();
        Loops(tree!).Should().Be(loops);
    }

    // vb6.exe's "Syntax error" for both (measured).
    [Theory]
    [InlineData("Sub Foo()\n    For i = 1 To 2\n        For j = 1 To 2\n        Next j,\n    Next i\nEnd Sub\n")]
    [InlineData("Sub Foo()\n    For i = 1 To 2\n        For j = 1 To 2\n        Next , i\nEnd Sub\n")]
    public void A_list_with_a_missing_name_is_still_a_syntax_error(string source)
    {
        VbDiagnosticsProvider.GetDiagnostics(source).Should().NotBeEmpty();
    }

    [Fact]
    public void Positions_after_the_list_are_the_source_positions()
    {
        // The error is the `)` on line 6; it must be reported there, at its own column, after the expanded Next.
        var diagnostics = VbDiagnosticsProvider.GetDiagnostics(
            "Sub Foo()\n    For i = 1 To 2\n        For j = 1 To 2\n        Next j, i\n    s = 1\n    s = )\nEnd Sub\n");

        diagnostics.Should().NotBeEmpty();
        diagnostics[0].Range.Start.Line.Should().Be(5);
        diagnostics[0].Range.Start.Character.Should().Be(8);
    }

    [Fact]
    public void The_outer_loop_is_still_a_symbol_free_region_of_its_procedure()
    {
        VbSymbolProvider.GetSymbols("Sub Foo()\n    For i = 1 To 2\n        For j = 1 To 2\n        Next j, i\nEnd Sub\nSub Bar()\nEnd Sub\n")
            .Select(s => s.Name).Should().Equal("Foo", "Bar");
    }
}
