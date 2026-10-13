// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// The body of a While...Wend is one block, as a Do loop's is (#882).

using Antlr4.Runtime.Tree;
using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

public class WhileWendGrammarTests
{
    // The rule was `blockSep block* NEWLINE* WEND`. Two blocks could then sit side by side with no separator, so
    // `k.Bump` parsed as a call of a procedure named k followed by `.Bump`, a With member call. That raised no syntax
    // error here, because the text was still accepted, but the interpreter's copy of the rule made it a run-time
    // "Sub or Function not defined". Both now read `blockSep (block blockSep)? WEND`, as doLoopStmt does.
    [Theory]
    [InlineData("Sub Foo()\n    While v\n        k.Bump\n    Wend\nEnd Sub\n", 1)]
    [InlineData("Sub Foo()\n    While v\n        Debug.Print 2\n        v = 0\n    Wend\nEnd Sub\n", 2)]
    [InlineData("Sub Foo()\n    While v\n        v = 0\n        k.Add 10\n    Wend\nEnd Sub\n", 2)]
    [InlineData("Sub Foo()\n    While v\n        With k\n            .Bump\n        End With\n    Wend\nEnd Sub\n", 1)]
    [InlineData("Sub Foo()\n    While v: k.Bump: v = 0: Wend\nEnd Sub\n", 2)]
    public void A_While_body_is_one_block_of_whole_statements(string source, int statements)
    {
        var (diagnostics, tree) = VbDiagnosticsProvider.GetDiagnosticsAndTree(source);

        diagnostics.Should().BeEmpty();
        var loop = Trees.FindAllRuleNodes(tree!, VisualBasic6Parser.RULE_whileWendStmt)
            .Cast<VisualBasic6Parser.WhileWendStmtContext>().Should().ContainSingle().Subject;
        loop.block().Should().NotBeNull();
        loop.block().blockStmt().Should().HaveCount(statements);
    }

    [Fact]
    public void An_empty_While_has_no_block()
    {
        var (diagnostics, tree) = VbDiagnosticsProvider.GetDiagnosticsAndTree("Sub Foo()\n    While v\n    Wend\nEnd Sub\n");

        diagnostics.Should().BeEmpty();
        var loop = Trees.FindAllRuleNodes(tree!, VisualBasic6Parser.RULE_whileWendStmt)
            .Cast<VisualBasic6Parser.WhileWendStmtContext>().Should().ContainSingle().Subject;
        loop.block().Should().BeNull();
    }
}
