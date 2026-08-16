// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// Walks the VB6 parse tree (proleap grammar) and extracts declaration symbols.

using Antlr4.Runtime;

namespace HexIDE.VbLspServer;

/// <summary>Walks the VB6 parse tree and extracts declaration symbols.</summary>
public static class VbSymbolProvider
{
    public static List<LspSymbol> GetSymbols(string source)
    {
        var inputStream = new AntlrInputStream(source);
        var lexer = new VisualBasic6Lexer(inputStream);
        lexer.RemoveErrorListeners();
        var tokenStream = new CommonTokenStream(lexer);

        var parser = new VisualBasic6Parser(tokenStream);
        parser.RemoveErrorListeners();
        parser.AddParseListener(new ParseDepthGuard(VbDiagnosticsProvider.MaxParseDepth));
        try
        {
            return GetSymbols(parser.startRule());
        }
        catch (ParseNestingTooDeepException)
        {
            // Degenerate deeply-nested input — no symbols rather than a stack-overflow crash of the server.
            return [];
        }
    }

    /// <summary>Extracts symbols from an already-parsed tree, letting the caller parse once and share it.</summary>
    public static List<LspSymbol> GetSymbols(VisualBasic6Parser.StartRuleContext? tree)
    {
        if (tree is null) return [];
        var visitor = new SymbolVisitor();
        visitor.Visit(tree);
        return visitor.Symbols;
    }

    private sealed class SymbolVisitor : VisualBasic6ParserBaseVisitor<object?>
    {
        public List<LspSymbol> Symbols { get; } = new();

        private int _depth;
        private const int MaxDepth = 500;

        // Depth guard (mandated for every ANTLR visitor over this grammar — see CLAUDE.md): a degenerate/deeply
        // nested parse tree can otherwise recurse until it overflows the C# stack, which is UNCATCHABLE and takes the
        // whole LSP server process down (killing document-symbols/definition/diagnostics until a restart).
        public override object? Visit(Antlr4.Runtime.Tree.IParseTree? tree)
        {
            if (tree is null) return null;
            if (++_depth > MaxDepth) { --_depth; return null; }
            try   { return base.Visit(tree); }
            finally { --_depth; }
        }

        public override object? VisitSubStmt(VisualBasic6Parser.SubStmtContext ctx)
        {
            var name = ctx.ambiguousIdentifier()?.GetText();
            if (name is { Length: > 0 })
                Symbols.Add(MakeSymbol(name, LspSymbolKind.Method, ctx));
            return VisitChildren(ctx);
        }

        public override object? VisitFunctionStmt(VisualBasic6Parser.FunctionStmtContext ctx)
        {
            var name = ctx.ambiguousIdentifier()?.GetText();
            if (name is { Length: > 0 })
                Symbols.Add(MakeSymbol(name, LspSymbolKind.Function, ctx));
            return VisitChildren(ctx);
        }

        public override object? VisitPropertyGetStmt(VisualBasic6Parser.PropertyGetStmtContext ctx)
        {
            var name = ctx.ambiguousIdentifier()?.GetText();
            if (name is { Length: > 0 })
                Symbols.Add(MakeSymbol($"{name} (Get)", LspSymbolKind.Property, ctx));
            return VisitChildren(ctx);
        }

        public override object? VisitPropertyLetStmt(VisualBasic6Parser.PropertyLetStmtContext ctx)
        {
            var name = ctx.ambiguousIdentifier()?.GetText();
            if (name is { Length: > 0 })
                Symbols.Add(MakeSymbol($"{name} (Let)", LspSymbolKind.Property, ctx));
            return VisitChildren(ctx);
        }

        public override object? VisitPropertySetStmt(VisualBasic6Parser.PropertySetStmtContext ctx)
        {
            var name = ctx.ambiguousIdentifier()?.GetText();
            if (name is { Length: > 0 })
                Symbols.Add(MakeSymbol($"{name} (Set)", LspSymbolKind.Property, ctx));
            return VisitChildren(ctx);
        }

        public override object? VisitEnumerationStmt(VisualBasic6Parser.EnumerationStmtContext ctx)
        {
            var name = ctx.ambiguousIdentifier()?.GetText();
            if (name is { Length: > 0 })
                Symbols.Add(MakeSymbol(name, LspSymbolKind.Enum, ctx));
            return VisitChildren(ctx);
        }

        // proleap models a user-defined type (UDT) as `typeStmt` (RD grammar called it udtDeclaration).
        public override object? VisitTypeStmt(VisualBasic6Parser.TypeStmtContext ctx)
        {
            var name = ctx.ambiguousIdentifier()?.GetText();
            if (name is { Length: > 0 })
                Symbols.Add(MakeSymbol(name, LspSymbolKind.Struct, ctx));
            return VisitChildren(ctx);
        }

        private static LspSymbol MakeSymbol(string name, LspSymbolKind kind, ParserRuleContext ctx)
        {
            var start = ctx.Start;
            var stop  = ctx.Stop ?? ctx.Start;
            return new LspSymbol(
                name,
                kind,
                new LspRange(
                    new LspPosition(start.Line - 1, start.Column),
                    new LspPosition(stop.Line - 1,  stop.Column + (stop.Text?.Length ?? 1))));
        }
    }
}

public enum LspSymbolKind
{
    Method   = 6,
    Function = 12,
    Property = 7,
    Enum     = 10,
    Struct   = 23,
}

public record LspSymbol(string Name, LspSymbolKind Kind, LspRange Range);
