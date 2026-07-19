// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The HexIDE Authors
// This file is part of HexIDE.VbLspServer, which uses the
// Rubberduck VBA ANTLR4 grammar (GPLv3). See LICENSE for details.

namespace HexIDE.VbLspServer;

/// <summary>
/// Performs scope-level analysis over an already-parsed VB6/VBA parse tree.
/// Currently detects undeclared variable assignments when <c>Option Explicit</c> is present.
/// </summary>
public static class VbScopeAnalyzer
{
    /// <summary>
    /// Returns all names declared in the module with their type annotation.
    /// Used to populate completion lists.
    /// </summary>
    public static IReadOnlyCollection<string> GetDeclaredNames(string source)
    {
        var tree = VbDiagnosticsProvider.ParseSource(source);
        if (tree is null) return [];
        var collector = new DeclarationCollectorVisitor();
        collector.Visit(tree);
        return collector.DeclaredNames;
    }

    /// <summary>
    /// Returns a map of declared name → type annotation string (null if untyped/Sub/Enum member).
    /// Used by the hover handler to show type info.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> GetDeclaredTypes(VBAParser.StartRuleContext tree)
    {
        var collector = new DeclarationCollectorVisitor();
        collector.Visit(tree);
        return collector.DeclaredTypes;
    }

    /// <summary>
    /// Returns diagnostics for undeclared variable assignments.
    /// Returns an empty list when <c>Option Explicit</c> is absent or when the
    /// source has syntax errors (to avoid cascading false positives).
    /// </summary>
    public static List<LspDiagnostic> GetOptionExplicitDiagnostics(VBAParser.StartRuleContext tree)
    {
        var collector = new DeclarationCollectorVisitor();
        collector.Visit(tree);

        if (!collector.HasOptionExplicit)
            return [];

        var checker = new UndeclaredVariableVisitor(collector.DeclaredNames);
        checker.Visit(tree);
        return checker.Diagnostics;
    }

    // ── Declaration collector ────────────────────────────────────────────────

    private sealed class DeclarationCollectorVisitor : VBAParserBaseVisitor<object?>
    {
        public bool HasOptionExplicit { get; private set; }

        /// <summary>Declared names → type annotation string (null = no explicit type).</summary>
        public Dictionary<string, string?> DeclaredTypes { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>All declared names (computed from DeclaredTypes keys).</summary>
        public HashSet<string> DeclaredNames => new(DeclaredTypes.Keys, StringComparer.OrdinalIgnoreCase);

        private static string? TypeText(VBAParser.AsTypeClauseContext? asType)
        {
            if (asType is null) return null;
            // The grammar includes "As" in the node; strip it.
            var raw = asType.type()?.GetText() ?? asType.GetText();
            if (raw.StartsWith("As", StringComparison.OrdinalIgnoreCase) && raw.Length > 2)
                raw = raw[2..].TrimStart();
            return raw.Length > 0 ? raw : null;
        }

        public override object? VisitOptionExplicitStmt(VBAParser.OptionExplicitStmtContext ctx)
        {
            HasOptionExplicit = true;
            return null;
        }

        public override object? VisitVariableSubStmt(VBAParser.VariableSubStmtContext ctx)
        {
            var name = ctx.identifier()?.GetText();
            if (name is { Length: > 0 })
                DeclaredTypes[name] = TypeText(ctx.asTypeClause());
            return VisitChildren(ctx);
        }

        public override object? VisitConstSubStmt(VBAParser.ConstSubStmtContext ctx)
        {
            var name = ctx.identifier()?.GetText();
            if (name is { Length: > 0 })
                DeclaredTypes[name] = TypeText(ctx.asTypeClause());
            return VisitChildren(ctx);
        }

        public override object? VisitArg(VBAParser.ArgContext ctx)
        {
            var name = ctx.unrestrictedIdentifier()?.GetText();
            if (name is { Length: > 0 })
                DeclaredTypes[name] = TypeText(ctx.asTypeClause());
            return VisitChildren(ctx);
        }

        public override object? VisitSubStmt(VBAParser.SubStmtContext ctx)
        {
            var name = ctx.subroutineName()?.GetText();
            if (name is { Length: > 0 })
                DeclaredTypes[name] = null; // Sub has no return type
            return VisitChildren(ctx);
        }

        public override object? VisitFunctionStmt(VBAParser.FunctionStmtContext ctx)
        {
            var name = ctx.functionName()?.GetText();
            if (name is { Length: > 0 })
                DeclaredTypes[name] = TypeText(ctx.asTypeClause());
            return VisitChildren(ctx);
        }

        public override object? VisitPropertyGetStmt(VBAParser.PropertyGetStmtContext ctx)
        {
            var name = ctx.functionName()?.GetText();
            if (name is { Length: > 0 })
                DeclaredTypes[name] = TypeText(ctx.asTypeClause());
            return VisitChildren(ctx);
        }

        public override object? VisitPropertyLetStmt(VBAParser.PropertyLetStmtContext ctx)
        {
            var name = ctx.subroutineName()?.GetText();
            if (name is { Length: > 0 })
                DeclaredTypes[name] = null;
            return VisitChildren(ctx);
        }

        public override object? VisitPropertySetStmt(VBAParser.PropertySetStmtContext ctx)
        {
            var name = ctx.subroutineName()?.GetText();
            if (name is { Length: > 0 })
                DeclaredTypes[name] = null;
            return VisitChildren(ctx);
        }

        public override object? VisitEnumerationStmt_Constant(VBAParser.EnumerationStmt_ConstantContext ctx)
        {
            var name = ctx.identifier()?.GetText();
            if (name is { Length: > 0 })
                DeclaredTypes[name] = null;
            return VisitChildren(ctx);
        }

        public override object? VisitUdtDeclaration(VBAParser.UdtDeclarationContext ctx)
        {
            var name = ctx.untypedIdentifier()?.GetText();
            if (name is { Length: > 0 })
                DeclaredTypes[name] = null;
            return VisitChildren(ctx);
        }

        public override object? VisitEnumerationStmt(VBAParser.EnumerationStmtContext ctx)
        {
            var name = ctx.identifier()?.GetText();
            if (name is { Length: > 0 })
                DeclaredTypes[name] = null;
            return VisitChildren(ctx);
        }
    }

    // ── Undeclared variable checker (expressions + assignments) ─────────────

    private sealed class UndeclaredVariableVisitor(HashSet<string> declaredNames)
        : VBAParserBaseVisitor<object?>
    {
        private readonly HashSet<(int Line, int Col)> _reported = [];
        private int _depth;
        private const int MaxDepth = 500;

        public List<LspDiagnostic> Diagnostics { get; } = [];

        // Depth guard: the RD VBA grammar has known ambiguities; bail out before
        // overflowing the stack on degenerate inputs.
        public override object? Visit(Antlr4.Runtime.Tree.IParseTree? tree)
        {
            if (tree is null) return null;
            if (++_depth > MaxDepth) { --_depth; return null; }
            try   { return base.Visit(tree); }
            finally { --_depth; }
        }

        public override object? VisitSimpleNameExpr(VBAParser.SimpleNameExprContext ctx)
        {
            var name = ctx.identifier()?.GetText();
            if (name is { Length: > 0 }
                && !declaredNames.Contains(name)
                && !VbBuiltins.IsKnownName(name))
            {
                var token = ctx.Start;
                var line  = token.Line - 1; // LSP is 0-based
                var col   = token.Column;
                // Deduplicate: grammar quirks can produce the same node position twice
                if (_reported.Add((line, col)))
                {
                    Diagnostics.Add(new LspDiagnostic(
                        new LspRange(new LspPosition(line, col), new LspPosition(line, col + name.Length)),
                        $"Variable '{name}' is not declared. Consider adding 'Dim {name}' or removing 'Option Explicit'.",
                        2));
                }
            }
            // Simple name expressions are leaves — don't recurse further.
            return null;
        }
    }
}
