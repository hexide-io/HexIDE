// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// Guards the two-stage (SLL->LL) parse: it must stay correctness-identical to LL. The regression this
// pins is SLL-only prediction, which mispredicts the call/array/member decision — the most common VB6
// construct — and would flag valid code with false "mismatched input '('" errors (and, because scope
// analysis is skipped when parse errors exist, silently kill Option Explicit checks).

using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

public class ParseHardeningTests
{
    // Each of these is valid VB6 that LL parses cleanly but SLL-only mispredicts. Two-stage must fall
    // back to LL and produce ZERO diagnostics.
    [Theory]
    [InlineData("Sub S()\n    x = Foo(1)\nEnd Sub\n")]
    [InlineData("Sub S()\n    y = Mid(a, b)\nEnd Sub\n")]
    [InlineData("Sub S()\n    z = arr(1)\nEnd Sub\n")]
    [InlineData("Sub S()\n    Call obj.Method(1)\nEnd Sub\n")]
    [InlineData("Sub S()\n    x = Foo(a:=1, b:=2)\nEnd Sub\n")]
    [InlineData("Sub S()\n    v = col!Item.Foo(1)\nEnd Sub\n")]
    [InlineData("Sub S()\n    x = obj.Items(5)\nEnd Sub\n")]
    public void Call_with_args_in_rvalue_produces_no_false_diagnostics(string code)
    {
        VbDiagnosticsProvider.GetDiagnostics(code).Should()
            .BeEmpty("two-stage falls back to LL, which parses calls-with-args cleanly (SLL-only flags them)");
    }

    [Fact]
    public void Oversized_input_pauses_analysis_without_hanging()
    {
        // ~510k chars, over the 400k guard → analysis paused with a single informational diagnostic
        // (and, crucially, no multi-second parse on the keystroke path).
        var huge = string.Concat(Enumerable.Repeat("Dim x As Integer\n", 30_000));

        var diags = VbDiagnosticsProvider.GetDiagnostics(huge);

        diags.Should().ContainSingle();
        diags[0].Message.Should().Contain("too large");
    }

    // ── wall-clock parse backstop ────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Parse_within_the_budget_returns_diagnostics()
    {
        // A syntax error is the budget-independent "a diagnostic exists" signal (the undeclared-variable
        // check is default-off in the live path now). '@@' is an invalid token.
        var result = await VbDiagnosticsProvider.TryGetDiagnosticsAndTreeWithin(
            "Sub S()\n    @@\nEnd Sub\n", TimeSpan.FromSeconds(10));

        result.Should().NotBeNull();
        result!.Value.Diagnostics.Should().NotBeEmpty("a syntax error must surface within the budget");
    }

    [Fact]
    public async Task Parse_exceeding_the_budget_returns_null_analysis_pending()
    {
        // A zero budget: the already-completed delay wins the race before the off-thread parse can
        // finish, so the backstop abandons it and returns null (the caller keeps prior state).
        var result = await VbDiagnosticsProvider.TryGetDiagnosticsAndTreeWithin(
            "Sub Foo()\nEnd Sub\n", TimeSpan.Zero);

        result.Should().BeNull();
    }

    // ── parser depth guard (bug-hunt HIGH) ───────────────────────────────────────────────────────
    // The recursive-descent parser itself overflows the C# stack on deeply-nested input (~600 parens) — an
    // UNCATCHABLE StackOverflowException that kills the server regardless of thread (so the wall-clock backstop
    // can't save it). ParseDepthGuard aborts such a parse with a catchable exception at a rule-depth of 300 —
    // proven (during development) to fire well before the overflow under both SLL and LL prediction. These inputs
    // are ~400 levels: past the guard, below the overflow, so the guard is what stops them. Without it, running
    // either test would abort the whole test run.

    [Fact]
    public void Deeply_nested_parens_pause_analysis_without_crashing()
    {
        var deep = "Sub S()\n    x = " + new string('(', 400) + "1" + new string(')', 400) + "\nEnd Sub\n";

        var diags = VbDiagnosticsProvider.GetDiagnostics(deep);

        diags.Should().ContainSingle();
        diags[0].Message.Should().Contain("too deep");
    }

    [Fact]
    public void Deeply_nested_blocks_pause_analysis_without_crashing()
    {
        // Deep statement-block nesting is a DISTINCT parser recursion path from parenthesised expressions.
        var sb = new System.Text.StringBuilder("Sub S()\n");
        for (int i = 0; i < 400; i++) sb.Append("If True Then\n");
        sb.Append("    x = 1\n");
        for (int i = 0; i < 400; i++) sb.Append("End If\n");
        sb.Append("End Sub\n");

        var diags = VbDiagnosticsProvider.GetDiagnostics(sb.ToString());

        diags.Should().Contain(d => d.Message.Contains("too deep"));
    }

    [Fact]
    public void Deeply_nested_input_yields_no_symbols_without_crashing()
    {
        var deep = "Sub S()\n    x = " + new string('(', 400) + "1" + new string(')', 400) + "\nEnd Sub\n";

        VbSymbolProvider.GetSymbols(deep).Should()
            .BeEmpty("a degenerate deep parse yields no symbols rather than overflowing the server's stack");
    }

    // NB (bug-hunt): VbSymbolProvider's SymbolVisitor also carries the mandated visitor depth guard (MaxDepth=500,
    // matching VbScopeAnalyzer). With the parser guard above it is now defense-in-depth — the parser aborts a
    // degenerate parse before any visitor runs. The guards' benign effect on real code is covered by
    // ComponentPortTests.Symbols_extracted_for_each_declaration_kind.
}
