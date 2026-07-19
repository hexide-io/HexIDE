// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The HexIDE Authors
// This file is part of HexIDE.VbLspServer, which uses the
// Rubberduck VBA ANTLR4 grammar (GPLv3). See LICENSE for details.

using Antlr4.Runtime;

namespace HexIDE.VbLspServer;

/// <summary>Parses VB6/VBA source and returns LSP diagnostics.</summary>
public static class VbDiagnosticsProvider
{
    public static List<LspDiagnostic> GetDiagnostics(string source)
    {
        var diagnostics = new List<LspDiagnostic>();
        var tree = ParseSource(source, diagnostics);

        // Only run scope analysis when the syntax is clean — syntax errors can produce
        // an incomplete parse tree which would cause spurious undeclared-variable warnings.
        if (diagnostics.Count == 0 && tree is not null)
            diagnostics.AddRange(VbScopeAnalyzer.GetOptionExplicitDiagnostics(tree));

        return diagnostics;
    }

    /// <summary>
    /// Parses source once, returns both the diagnostics list and the parse tree.
    /// Use this when callers also need the tree (e.g. to extract declared types).
    /// </summary>
    public static (List<LspDiagnostic> Diagnostics, VBAParser.StartRuleContext? Tree) GetDiagnosticsAndTree(string source)
    {
        var diagnostics = new List<LspDiagnostic>();
        var tree = ParseSource(source, diagnostics);

        if (diagnostics.Count == 0 && tree is not null)
            diagnostics.AddRange(VbScopeAnalyzer.GetOptionExplicitDiagnostics(tree));

        return (diagnostics, tree);
    }

    /// <summary>
    /// Parses <paramref name="source"/> and returns the parse tree.
    /// Any lexer/parser errors are collected into <paramref name="diagnostics"/> if provided.
    /// Returns null only if the source is empty/unparseable at the grammar level.
    /// </summary>
    internal static VBAParser.StartRuleContext? ParseSource(string source, List<LspDiagnostic>? diagnostics = null)
    {
        var inputStream = new AntlrInputStream(source);
        var lexer = new VBALexer(inputStream);
        lexer.RemoveErrorListeners();
        DiagnosticErrorListener? errorListener = diagnostics is not null ? new DiagnosticErrorListener(diagnostics) : null;
        if (errorListener is not null)
            lexer.AddErrorListener(errorListener);

        var tokenStream = new CommonTokenStream(lexer);
        tokenStream.Fill();

        if (diagnostics is not null)
        {
            // ERRORCHAR tokens = unrecognised characters the lexer couldn't match
            foreach (var token in tokenStream.GetTokens())
            {
                if (token.Type == VBALexer.ERRORCHAR)
                {
                    var line = token.Line - 1;  // LSP is 0-based
                    var col = token.Column;
                    var len = token.Text.Length;
                    diagnostics.Add(new LspDiagnostic(
                        new LspRange(new LspPosition(line, col), new LspPosition(line, col + len)),
                        $"Unexpected character: '{token.Text}'",
                        1));
                }
            }
        }

        var parser = new VBAParser(tokenStream);
        parser.RemoveErrorListeners();
        if (errorListener is not null)
            parser.AddErrorListener(errorListener);

        return parser.startRule();
    }

    private sealed class DiagnosticErrorListener(List<LspDiagnostic> diagnostics)
        : IAntlrErrorListener<IToken>, IAntlrErrorListener<int>
    {
        public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol,
            int line, int charPositionInLine, string msg, RecognitionException e)
        {
            var tokenText = offendingSymbol?.Text;
            var tokenLen = tokenText is { } t && t != "<EOF>" ? t.Length : 1;
            diagnostics.Add(new LspDiagnostic(
                new LspRange(
                    new LspPosition(line - 1, charPositionInLine),
                    new LspPosition(line - 1, charPositionInLine + tokenLen)),
                VbErrorMessages.Prettify(msg, tokenText),
                1));
        }

        // Lexer error overload
        public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol,
            int line, int charPositionInLine, string msg, RecognitionException e)
        {
            diagnostics.Add(new LspDiagnostic(
                new LspRange(
                    new LspPosition(line - 1, charPositionInLine),
                    new LspPosition(line - 1, charPositionInLine + 1)),
                VbErrorMessages.Prettify(msg, ((char)offendingSymbol).ToString()),
                1));
        }
    }
}

// ── Minimal LSP types used by the server ──────────────────────────────────────

public record LspPosition(int Line, int Character);
public record LspRange(LspPosition Start, LspPosition End);
public record LspDiagnostic(LspRange Range, string Message, int Severity);

public enum LspCompletionItemKind
{
    Text     = 1,
    Function = 3,
    Variable = 6,
    Property = 10,
    Keyword  = 14,
    Constant = 21,
}

public record LspCompletionItem(
    string Label,
    LspCompletionItemKind Kind,
    string? Detail = null,
    string? InsertText = null);
