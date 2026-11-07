// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors

using Antlr4.Runtime;

namespace HexIDE.VbLspServer;

/// <summary>
/// <c>Next j, i</c> closes two loops at once, and vb6.exe runs it as <c>Next j: Next i</c>, for For and For Each alike
/// (measured, #2393). No grammar rule can say that: the outer loop has no Next of its own, and how many loops one Next
/// closes depends on how many names it lists. So before the parse each comma in such a list becomes a statement
/// separator followed by a Next of its own, at the comma's position, and every other token keeps the position it had.
/// </summary>
/// <remarks>
/// Only a list VB6 compiles is expanded: a Next that starts a statement, then names with an optional type character,
/// separated by commas. <c>Next j,</c> and <c>Next , i</c> are left as they are, and stay the syntax errors vb6.exe
/// reports. The interpreter's grammar has the same expansion, in <c>NextCounterList</c>.
/// </remarks>
internal static class VbNextCounterList
{
    /// <summary>The lexer's tokens, with each comma of a Next's counter list expanded, and a line number on a closing line
    /// hidden (<see cref="VbClosingLineNumbers"/>). Lexing happens here, so the lexer's error listeners must be attached
    /// first.</summary>
    public static CommonTokenStream Tokens(VisualBasic6Lexer lexer)
    {
        var tokens = lexer.GetAllTokens();
        VbClosingLineNumbers.Hide(tokens);
        var commas = Commas(tokens);
        if (commas.Count == 0)
            return new CommonTokenStream(new ListTokenSource(tokens));
        var expanded = new List<IToken>(tokens.Count + 3 * commas.Count);
        for (var i = 0; i < tokens.Count; i++)
        {
            if (!commas.Contains(i))
            {
                expanded.Add(tokens[i]);
                continue;
            }
            expanded.Add(Synthetic(tokens[i], VisualBasic6Lexer.COLON, null));
            expanded.Add(Synthetic(tokens[i], VisualBasic6Lexer.NEXT, "Next"));
            // The grammar wants one space between Next and its name: the comma's own, past a line continuation.
            var after = i + 1;
            while (after < tokens.Count && tokens[after].Channel != TokenConstants.DefaultChannel)
                after++;
            if (after < tokens.Count && tokens[after].Type != VisualBasic6Lexer.WS)
                expanded.Add(Synthetic(tokens[i], VisualBasic6Lexer.WS, " "));
        }
        return new CommonTokenStream(new ListTokenSource(expanded));
    }

    // The indexes of the commas to expand.
    private static HashSet<int> Commas(IList<IToken> tokens)
    {
        var commas = new HashSet<int>();
        for (var k = 0; k < tokens.Count; k++)
        {
            if (tokens[k].Type != VisualBasic6Lexer.NEXT || !StartsStatement(tokens, k) || Skip(tokens, k + 1) == k + 1)
                continue;
            var p = Skip(tokens, k + 1);
            while (IsName(tokens, p))
            {
                p = Skip(tokens, IsTypeHint(tokens, p + 1) ? p + 2 : p + 1);
                if (p >= tokens.Count || tokens[p].Type != VisualBasic6Lexer.COMMA || !IsName(tokens, Skip(tokens, p + 1)))
                    break;
                commas.Add(p);
                p = Skip(tokens, p + 1);
            }
        }
        return commas;
    }

    // Past white space and the hidden channel: a line continuation, a comment.
    private static int Skip(IList<IToken> tokens, int p)
    {
        while (p < tokens.Count && (tokens[p].Type == VisualBasic6Lexer.WS || tokens[p].Channel != TokenConstants.DefaultChannel))
            p++;
        return p;
    }

    private static int SkipBack(IList<IToken> tokens, int p)
    {
        while (p >= 0 && (tokens[p].Type == VisualBasic6Lexer.WS || tokens[p].Channel != TokenConstants.DefaultChannel))
            p--;
        return p;
    }

    // At the start of the module, after a line break or a colon, or after a line number that starts its line.
    private static bool StartsStatement(IList<IToken> tokens, int k)
    {
        var p = SkipBack(tokens, k - 1);
        if (p >= 0 && tokens[p].Type == VisualBasic6Lexer.INTEGERLITERAL)
            p = SkipBack(tokens, p - 1);
        return p < 0 || tokens[p].Type is VisualBasic6Lexer.NEWLINE or VisualBasic6Lexer.COLON;
    }

    private static bool IsName(IList<IToken> tokens, int p) =>
        p < tokens.Count && tokens[p].Type is not (VisualBasic6Lexer.NEWLINE or VisualBasic6Lexer.COLON or VisualBasic6Lexer.COMMA or VisualBasic6Lexer.WS
            or VisualBasic6Lexer.LPAREN or TokenConstants.EOF) && !IsTypeHint(tokens, p);

    private static bool IsTypeHint(IList<IToken> tokens, int p) =>
        p < tokens.Count && tokens[p].Type is VisualBasic6Lexer.AMPERSAND or VisualBasic6Lexer.AT or VisualBasic6Lexer.DOLLAR or VisualBasic6Lexer.EXCLAMATIONMARK
            or VisualBasic6Lexer.HASH or VisualBasic6Lexer.PERCENT;

    private static CommonToken Synthetic(IToken comma, int type, string? text)
    {
        var token = new CommonToken(comma) { Type = type };
        if (text is not null)
            token.Text = text;
        return token;
    }
}
