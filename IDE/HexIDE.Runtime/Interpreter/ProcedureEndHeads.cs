using Antlr4.Runtime;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// A line number or label may stand on a procedure's last line, <c>90 End Sub</c> or <c>Done: End Sub</c>, and vb6.exe
/// compiles both; a <c>GoTo</c> to one returns from the procedure (measured, #2529, native and p-code). Every way of saying
/// that in the procedure rules moved the parser's error recovery for a broken procedure header, which a module that does
/// not parse depends on (#2561). So before the parse the head is given a statement of its own: an <c>Exit</c> of the
/// procedure's kind on the <c>End</c> line, then a line break, then the <c>End</c>. Returning there is what reaching the
/// <c>End</c> does, the label table and <c>Erl</c> see the head as they see any other, and a debugger step stops on that
/// line once, as it stops on an <c>End Sub</c>.
/// </summary>
/// <remarks>
/// A head starts its line: a number, a label and its colon, or both, as <c>lineHead</c> takes them. After a colon on the
/// same line, as in <c>x = 1: End Sub</c>, there is no head.
/// </remarks>
internal static class ProcedureEndHeads
{
    /// <summary>The tokens, with an Exit before each procedure End that carries a head.</summary>
    public static IList<IToken> Expand(IList<IToken> tokens)
    {
        List<IToken>? expanded = null;
        for (var i = 0; i < tokens.Count; i++)
        {
            var exit = ExitFor(tokens[i].Type);
            if (exit is { } kind && Headed(tokens, i))
            {
                expanded ??= new List<IToken>(tokens.Take(i));
                expanded.Add(Synthetic(tokens[i], kind.Type, kind.Text));
                expanded.Add(Synthetic(tokens[i], VB6Lexer.NEWLINE, "\n"));
            }
            expanded?.Add(tokens[i]);
        }
        return expanded ?? tokens;
    }

    private static (int Type, string Text)? ExitFor(int type) => type switch
    {
        VB6Lexer.END_SUB => (VB6Lexer.EXIT_SUB, "Exit Sub"),
        VB6Lexer.END_FUNCTION => (VB6Lexer.EXIT_FUNCTION, "Exit Function"),
        VB6Lexer.END_PROPERTY => (VB6Lexer.EXIT_PROPERTY, "Exit Property"),
        _ => null,
    };

    // Whether the End at i follows a head that starts its line: `90 `, `Done: ` or `90 Done: `.
    private static bool Headed(IList<IToken> tokens, int i)
    {
        var p = SkipBack(tokens, i - 1);
        var headed = false;
        if (p >= 0 && tokens[p].Type == VB6Lexer.COLON)
        {
            var name = SkipBack(tokens, p - 1);
            if (name < 0 || tokens[name].Type is not (VB6Lexer.IDENTIFIER or VB6Lexer.INTEGERLITERAL))
                return false;
            p = SkipBack(tokens, name - 1);
            headed = true;
        }
        if (p >= 0 && tokens[p].Type == VB6Lexer.INTEGERLITERAL)
        {
            p = SkipBack(tokens, p - 1);
            headed = true;
        }
        return headed && (p < 0 || tokens[p].Type == VB6Lexer.NEWLINE);
    }

    private static int SkipBack(IList<IToken> tokens, int p)
    {
        while (p >= 0 && (tokens[p].Type == VB6Lexer.WS || tokens[p].Channel != TokenConstants.DefaultChannel))
            p--;
        return p;
    }

    private static CommonToken Synthetic(IToken end, int type, string text) => new(end) { Type = type, Text = text };
}
