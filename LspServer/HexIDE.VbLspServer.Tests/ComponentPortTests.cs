// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// Verifies the three grammar-coupled components (diagnostics / symbols / scope) work on the
// proleap tree — not just compile against it.

using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

public class ComponentPortTests
{
    // ── P1: diagnostics ─────────────────────────────────────────────────────────
    [Fact]
    public void CleanModule_has_no_diagnostics()
    {
        VbDiagnosticsProvider.GetDiagnostics("Sub Foo()\nEnd Sub\n").Should().BeEmpty();
    }

    [Fact]
    public void SyntaxError_produces_a_diagnostic()
    {
        // 'End Sub' with no matching 'Sub' open — a parser error.
        VbDiagnosticsProvider.GetDiagnostics("Function Bar()\nEnd Function\nEnd Sub\n")
            .Should().NotBeEmpty();
    }

    [Fact]
    public void UnrecognisedCharacter_produces_a_diagnostic()
    {
        // proleap has no ERRORCHAR token — an unrecognised char must surface via the lexer error listener.
        VbDiagnosticsProvider.GetDiagnostics("Sub Foo()\n    x = §\nEnd Sub\n")
            .Should().NotBeEmpty();
    }

    // ── P3: symbols ─────────────────────────────────────────────────────────────
    [Fact]
    public void Symbols_extracted_for_each_declaration_kind()
    {
        var code =
            "Sub MySub()\nEnd Sub\n" +
            "Function MyFunc() As Integer\nEnd Function\n" +
            "Property Get MyProp() As Integer\nEnd Property\n" +
            "Enum MyEnum\n    A\nEnd Enum\n" +
            "Type MyType\n    X As Integer\nEnd Type\n";

        var symbols = VbSymbolProvider.GetSymbols(code);

        symbols.Select(s => s.Name).Should()
            .Contain(new[] { "MySub", "MyFunc", "MyProp (Get)", "MyEnum", "MyType" });
        symbols.Should().Contain(s => s.Name == "MySub"  && s.Kind == LspSymbolKind.Method);
        symbols.Should().Contain(s => s.Name == "MyFunc" && s.Kind == LspSymbolKind.Function);
        symbols.Should().Contain(s => s.Name == "MyType" && s.Kind == LspSymbolKind.Struct);
    }

    // ── P2: scope analyzer ──────────────────────────────────────────────────────
    [Fact]
    public void DeclaredNames_collected()
    {
        var names = VbScopeAnalyzer.GetDeclaredNames("Dim Counter As Integer\nSub Foo()\nEnd Sub\n");
        names.Should().Contain("Counter").And.Contain("Foo");
    }

    [Fact]
    public void OptionExplicit_undeclared_variable_is_flagged_severity2()
    {
        // The analyzer still flags undeclared vars at severity 2; the live provider path is default-off
        // (see VbDiagnosticsProvider.EnableUndeclaredVariableCheck + the VbScopeAnalyzer tests).
        var tree = VbDiagnosticsProvider.GetDiagnosticsAndTree(
            "Option Explicit\nSub Foo()\n    y = 5\nEnd Sub\n").Tree;
        var diags = VbScopeAnalyzer.GetOptionExplicitDiagnostics(tree!);
        diags.Should().Contain(d => d.Message.Contains("'y'") && d.Severity == 2);
    }

    [Fact]
    public void OptionExplicit_declared_variable_is_not_flagged()
    {
        var diags = VbDiagnosticsProvider.GetDiagnostics(
            "Option Explicit\nDim y As Integer\nSub Foo()\n    y = 5\nEnd Sub\n");
        diags.Should().NotContain(d => d.Message.Contains("not declared"));
    }

    [Fact]
    public void NoOptionExplicit_undeclared_variable_is_not_flagged()
    {
        var diags = VbDiagnosticsProvider.GetDiagnostics("Sub Foo()\n    y = 5\nEnd Sub\n");
        diags.Should().NotContain(d => d.Message.Contains("not declared"));
    }

    [Fact]
    public void MemberAccess_does_not_falsely_flag_the_member()
    {
        // obj is declared; `obj.Missing` must parse cleanly AND not flag "Missing" (the member-call skip).
        var diags = VbDiagnosticsProvider.GetDiagnostics(
            "Option Explicit\nDim obj As Object\nSub Foo()\n    obj.Missing = 5\nEnd Sub\n");
        diags.Should().BeEmpty();
    }
}
