// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The HexIDE Authors
// This file is part of HexIDE.VbLspServer, which uses the
// Rubberduck VBA ANTLR4 grammar (GPLv3). See LICENSE for details.

using Antlr4.Runtime;

namespace HexIDE.VbLspServer;

/// <summary>Walks the VBA parse tree and extracts declaration symbols.</summary>
public static class VbSymbolProvider
{
    public static List<LspSymbol> GetSymbols(string source)
    {
        var inputStream = new AntlrInputStream(source);
        var lexer = new VBALexer(inputStream);
        lexer.RemoveErrorListeners();
        var tokenStream = new CommonTokenStream(lexer);

        var parser = new VBAParser(tokenStream);
        parser.RemoveErrorListeners();
        var tree = parser.startRule();

        var visitor = new SymbolVisitor();
        visitor.Visit(tree);
        return visitor.Symbols;
    }

    private sealed class SymbolVisitor : VBAParserBaseVisitor<object?>
    {
        public List<LspSymbol> Symbols { get; } = new();

        public override object? VisitSubStmt(VBAParser.SubStmtContext ctx)
        {
            var name = ctx.subroutineName()?.GetText();
            if (name is { Length: > 0 })
                Symbols.Add(MakeSymbol(name, LspSymbolKind.Method, ctx));
            return VisitChildren(ctx);
        }

        public override object? VisitFunctionStmt(VBAParser.FunctionStmtContext ctx)
        {
            var name = ctx.functionName()?.GetText();
            if (name is { Length: > 0 })
                Symbols.Add(MakeSymbol(name, LspSymbolKind.Function, ctx));
            return VisitChildren(ctx);
        }

        public override object? VisitPropertyGetStmt(VBAParser.PropertyGetStmtContext ctx)
        {
            var name = ctx.functionName()?.GetText();
            if (name is { Length: > 0 })
                Symbols.Add(MakeSymbol($"{name} (Get)", LspSymbolKind.Property, ctx));
            return VisitChildren(ctx);
        }

        public override object? VisitPropertyLetStmt(VBAParser.PropertyLetStmtContext ctx)
        {
            var name = ctx.subroutineName()?.GetText();
            if (name is { Length: > 0 })
                Symbols.Add(MakeSymbol($"{name} (Let)", LspSymbolKind.Property, ctx));
            return VisitChildren(ctx);
        }

        public override object? VisitPropertySetStmt(VBAParser.PropertySetStmtContext ctx)
        {
            var name = ctx.subroutineName()?.GetText();
            if (name is { Length: > 0 })
                Symbols.Add(MakeSymbol($"{name} (Set)", LspSymbolKind.Property, ctx));
            return VisitChildren(ctx);
        }

        public override object? VisitEnumerationStmt(VBAParser.EnumerationStmtContext ctx)
        {
            var name = ctx.identifier()?.GetText();
            if (name is { Length: > 0 })
                Symbols.Add(MakeSymbol(name, LspSymbolKind.Enum, ctx));
            return VisitChildren(ctx);
        }

        public override object? VisitUdtDeclaration(VBAParser.UdtDeclarationContext ctx)
        {
            var name = ctx.untypedIdentifier()?.GetText();
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
