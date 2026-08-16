// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// Scope-level analysis over a parsed VB6 tree (proleap grammar): declaration collection +
// the Option Explicit undeclared-variable inspection.

namespace HexIDE.VbLspServer;

/// <summary>
/// Performs scope-level analysis over an already-parsed VB6 parse tree.
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
    public static IReadOnlyDictionary<string, string?> GetDeclaredTypes(VisualBasic6Parser.StartRuleContext tree)
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
    public static List<LspDiagnostic> GetOptionExplicitDiagnostics(VisualBasic6Parser.StartRuleContext tree)
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

    private sealed class DeclarationCollectorVisitor : VisualBasic6ParserBaseVisitor<object?>
    {
        public bool HasOptionExplicit { get; private set; }

        /// <summary>Declared names → type annotation string (null = no explicit type).</summary>
        public Dictionary<string, string?> DeclaredTypes { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>All declared names (computed from DeclaredTypes keys).</summary>
        public HashSet<string> DeclaredNames => new(DeclaredTypes.Keys, StringComparer.OrdinalIgnoreCase);

        private static string? TypeText(VisualBasic6Parser.AsTypeClauseContext? asType)
        {
            // proleap: asTypeClause : AS WS (NEW WS)? type_ (WS fieldLength)? — the type is `type_`,
            // so (unlike the RD grammar) there is no leading "As" to strip.
            var raw = asType?.type_()?.GetText();
            return raw is { Length: > 0 } ? raw : null;
        }

        public override object? VisitOptionExplicitStmt(VisualBasic6Parser.OptionExplicitStmtContext ctx)
        {
            HasOptionExplicit = true;
            return null;
        }

        public override object? VisitVariableSubStmt(VisualBasic6Parser.VariableSubStmtContext ctx)
        {
            var name = ctx.ambiguousIdentifier()?.GetText();
            if (name is { Length: > 0 })
                DeclaredTypes[name] = TypeText(ctx.asTypeClause());
            return VisitChildren(ctx);
        }

        public override object? VisitConstSubStmt(VisualBasic6Parser.ConstSubStmtContext ctx)
        {
            var name = ctx.ambiguousIdentifier()?.GetText();
            if (name is { Length: > 0 })
                DeclaredTypes[name] = TypeText(ctx.asTypeClause());
            return VisitChildren(ctx);
        }

        public override object? VisitArg(VisualBasic6Parser.ArgContext ctx)
        {
            var name = ctx.ambiguousIdentifier()?.GetText();
            if (name is { Length: > 0 })
                DeclaredTypes[name] = TypeText(ctx.asTypeClause());
            return VisitChildren(ctx);
        }

        public override object? VisitSubStmt(VisualBasic6Parser.SubStmtContext ctx)
        {
            var name = ctx.ambiguousIdentifier()?.GetText();
            if (name is { Length: > 0 })
                DeclaredTypes[name] = null; // Sub has no return type
            return VisitChildren(ctx);
        }

        public override object? VisitFunctionStmt(VisualBasic6Parser.FunctionStmtContext ctx)
        {
            var name = ctx.ambiguousIdentifier()?.GetText();
            if (name is { Length: > 0 })
                DeclaredTypes[name] = TypeText(ctx.asTypeClause());
            return VisitChildren(ctx);
        }

        public override object? VisitPropertyGetStmt(VisualBasic6Parser.PropertyGetStmtContext ctx)
        {
            var name = ctx.ambiguousIdentifier()?.GetText();
            if (name is { Length: > 0 })
                DeclaredTypes[name] = TypeText(ctx.asTypeClause());
            return VisitChildren(ctx);
        }

        public override object? VisitPropertyLetStmt(VisualBasic6Parser.PropertyLetStmtContext ctx)
        {
            var name = ctx.ambiguousIdentifier()?.GetText();
            if (name is { Length: > 0 })
                DeclaredTypes[name] = null;
            return VisitChildren(ctx);
        }

        public override object? VisitPropertySetStmt(VisualBasic6Parser.PropertySetStmtContext ctx)
        {
            var name = ctx.ambiguousIdentifier()?.GetText();
            if (name is { Length: > 0 })
                DeclaredTypes[name] = null;
            return VisitChildren(ctx);
        }

        public override object? VisitEnumerationStmt_Constant(VisualBasic6Parser.EnumerationStmt_ConstantContext ctx)
        {
            var name = ctx.ambiguousIdentifier()?.GetText();
            if (name is { Length: > 0 })
                DeclaredTypes[name] = null;
            return VisitChildren(ctx);
        }

        // proleap models a user-defined type (UDT) as `typeStmt` (RD grammar called it udtDeclaration).
        public override object? VisitTypeStmt(VisualBasic6Parser.TypeStmtContext ctx)
        {
            var name = ctx.ambiguousIdentifier()?.GetText();
            if (name is { Length: > 0 })
                DeclaredTypes[name] = null;
            return VisitChildren(ctx);
        }

        public override object? VisitEnumerationStmt(VisualBasic6Parser.EnumerationStmtContext ctx)
        {
            var name = ctx.ambiguousIdentifier()?.GetText();
            if (name is { Length: > 0 })
                DeclaredTypes[name] = null;
            return VisitChildren(ctx);
        }
    }

    // ── Undeclared variable checker (expressions + assignments) ─────────────

    private sealed class UndeclaredVariableVisitor(HashSet<string> declaredNames)
        : VisualBasic6ParserBaseVisitor<object?>
    {
        private readonly HashSet<(int Line, int Col)> _reported = [];
        private int _depth;
        private const int MaxDepth = 500;

        public List<LspDiagnostic> Diagnostics { get; } = [];

        // Depth guard: degenerate inputs can produce deep trees; bail out before overflowing the stack.
        public override object? Visit(Antlr4.Runtime.Tree.IParseTree? tree)
        {
            if (tree is null) return null;
            if (++_depth > MaxDepth) { --_depth; return null; }
            try   { return base.Visit(tree); }
            finally { --_depth; }
        }

        // proleap's simple variable/procedure reference. (RD grammar called this simpleNameExpr.)
        public override object? VisitICS_S_VariableOrProcedureCall(VisualBasic6Parser.ICS_S_VariableOrProcedureCallContext ctx)
        {
            // Skip the member half of a member access (obj.Foo): only `obj` is a module-scope variable
            // reference; `Foo` is a member on it, not a name to check against declarations.
            if (ctx.Parent is VisualBasic6Parser.ICS_S_MemberCallContext)
                return null;

            var name = ctx.ambiguousIdentifier()?.GetText();
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
            // Simple name references are leaves for our purposes — don't recurse further.
            return null;
        }
    }
}
