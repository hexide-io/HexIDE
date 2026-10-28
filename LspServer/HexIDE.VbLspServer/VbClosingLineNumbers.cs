// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors

using Antlr4.Runtime;

namespace HexIDE.VbLspServer;

/// <summary>
/// A line number may stand on the line that closes a block, or opens its next part: <c>30 Next</c>, <c>30 End If</c>,
/// <c>40 Case Else</c>. vb6.exe compiles every one (measured, #2529), and this grammar has no place
/// for a number there. Giving it one changed how the parser recovers from a broken line, which the diagnostics depend on,
/// so instead the number is hidden from the parser before the parse. Nothing here needs it: the editor's diagnostics are
/// about syntax, and a jump to it is the interpreter's business.
/// </summary>
/// <remarks>
/// A number before the first <c>Case</c> of a <c>Select Case</c> stays, and stays the syntax error vb6.exe reports
/// ("Statements and labels invalid between Select Case and first Case"). A label there, <c>Skip: Next</c>, needs nothing:
/// this grammar reads it as a call, which is its recovery shape (#223), and a call is no syntax error. The interpreter's
/// grammar takes a head on a closing line as a head, in <c>VB6.g4</c>.
/// <para>
/// A number on <c>End Sub</c>, <c>End Function</c> or <c>End Property</c> is not hidden, because the interpreter cannot
/// load one yet (#2561): the editor and the interpreter must agree on what parses.
/// </para>
/// </remarks>
internal static class VbClosingLineNumbers
{
    /// <summary>Moves each such number to the hidden channel, in place.</summary>
    public static void Hide(IList<IToken> tokens)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Type != VisualBasic6Lexer.INTEGERLITERAL || tokens[i] is not CommonToken number || !StartsLine(tokens, i))
                continue;
            var keyword = Skip(tokens, i + 1);
            // `30 Skip: Next`: a label may follow the number on the same head.
            if (keyword < tokens.Count && tokens[keyword].Type == VisualBasic6Lexer.IDENTIFIER)
            {
                var colon = Skip(tokens, keyword + 1);
                if (colon < tokens.Count && tokens[colon].Type == VisualBasic6Lexer.COLON)
                    keyword = Skip(tokens, colon + 1);
            }
            if (keyword >= tokens.Count || !Closes(tokens[keyword].Type))
                continue;
            if (tokens[keyword].Type == VisualBasic6Lexer.CASE && PreviousLineSelects(tokens, i))
                continue;
            number.Channel = TokenConstants.HiddenChannel;
        }
    }

    private static bool Closes(int type) => type is VisualBasic6Lexer.NEXT or VisualBasic6Lexer.LOOP or VisualBasic6Lexer.WEND
        or VisualBasic6Lexer.END_IF or VisualBasic6Lexer.ELSE or VisualBasic6Lexer.ELSEIF or VisualBasic6Lexer.CASE
        or VisualBasic6Lexer.END_SELECT or VisualBasic6Lexer.END_WITH;

    private static bool StartsLine(IList<IToken> tokens, int i)
    {
        var before = SkipBack(tokens, i - 1);
        return before < 0 || tokens[before].Type == VisualBasic6Lexer.NEWLINE;
    }

    // Whether the last line with anything on it, before this one, opens a Select Case: then this Case is its first.
    private static bool PreviousLineSelects(IList<IToken> tokens, int i)
    {
        var end = SkipBack(tokens, i - 1);
        while (end >= 0 && tokens[end].Type == VisualBasic6Lexer.NEWLINE)
            end = SkipBack(tokens, end - 1);
        if (end < 0)
            return false;
        var start = end;
        while (start > 0 && tokens[start - 1].Type != VisualBasic6Lexer.NEWLINE)
            start--;
        start = Skip(tokens, start);
        if (start < tokens.Count && tokens[start].Type == VisualBasic6Lexer.INTEGERLITERAL)
            start = Skip(tokens, start + 1);
        return start < tokens.Count && tokens[start].Type == VisualBasic6Lexer.SELECT;
    }

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
}
