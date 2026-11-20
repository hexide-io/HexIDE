using Antlr4.Runtime;

namespace HexIDE.Runtime.Interpreter;

// Syntax-only block pairing, used only after a parse has failed. Measured messages: #4415.
internal static class ParseFailureMessage
{
    internal static (string Message, int Line) Classify(IRecognizer recognizer, int line)
    {
        if (recognizer is not VB6Parser parser || parser.TokenStream is not CommonTokenStream stream)
            return ("Syntax error", line);
        stream.Fill();
        var statements = new List<List<IToken>>();
        var statement = new List<IToken>();
        foreach (var token in stream.GetTokens())
        {
            if (token.Type is VB6Lexer.NEWLINE or VB6Lexer.COLON or TokenConstants.EOF)
            {
                // Then followed by a colon is an inline If, even when its first arm is empty.
                if (token.Type == VB6Lexer.COLON && statement.Count > 0 && statement[^1].Type == VB6Lexer.THEN)
                    statement.Add(token);
                if (statement.Count > 0 && statement[0].Type == VB6Lexer.INTEGERLITERAL)
                    statement.RemoveAt(0);
                if (statement.Count > 0)
                    statements.Add(statement);
                statement = new List<IToken>();
            }
            else if (token.Channel == TokenConstants.DefaultChannel && token.Type != VB6Lexer.WS)
                statement.Add(token);
        }
        var blocks = new List<(int Kind, int Line)>();
        int? procedure = null;
        foreach (var tokens in statements)
        {
            var first = tokens[0];
            var types = tokens.Select(t => t.Type).ToArray();
            var declaration = 0;
            while (declaration < types.Length && types[declaration] is VB6Lexer.PRIVATE or VB6Lexer.PUBLIC
                       or VB6Lexer.FRIEND or VB6Lexer.STATIC)
                declaration++;
            if (declaration < types.Length && types[declaration] is VB6Lexer.SUB or VB6Lexer.FUNCTION or VB6Lexer.PROPERTY_GET
                or VB6Lexer.PROPERTY_LET or VB6Lexer.PROPERTY_SET)
            {
                if (types[^1] == VB6Lexer.LPAREN)
                    return ("Expected: identifier", first.Line);
                if (types[declaration] == VB6Lexer.FUNCTION && types[^1] == VB6Lexer.AS)
                    return ("Expected: type name", first.Line);
                procedure = types[declaration];
                blocks.Clear();
                continue;
            }
            if (first.Type is VB6Lexer.END_SUB or VB6Lexer.END_FUNCTION or VB6Lexer.END_PROPERTY)
            {
                if (procedure is null)
                    return first.Type == VB6Lexer.END_SUB
                        ? ("Invalid outside procedure", first.Line) : ("Syntax error", line);
                if (blocks.Count > 0)
                {
                    if (line < blocks[0].Line)
                        return ("Syntax error", line);
                    var message = Missing(blocks[0].Kind);
                    return (message, message == "Syntax error" ? line : first.Line);
                }
                procedure = null;
                continue;
            }
            if (procedure is null)
                continue;
            int? opener = first.Type switch
            {
                VB6Lexer.IF when types[^1] == VB6Lexer.THEN => VB6Lexer.IF,
                VB6Lexer.FOR when (types.Contains(VB6Lexer.EQ) && types.Contains(VB6Lexer.TO)) || types.Contains(VB6Lexer.EACH) => VB6Lexer.FOR,
                VB6Lexer.DO => VB6Lexer.DO,
                VB6Lexer.SELECT => VB6Lexer.SELECT,
                VB6Lexer.WITH => VB6Lexer.WITH,
                VB6Lexer.WHILE => VB6Lexer.WHILE,
                _ => null
            };
            if (opener is { } kind)
            {
                blocks.Add((kind, first.Line));
                continue;
            }
            var close = first.Type switch
            {
                VB6Lexer.END_IF or VB6Lexer.ELSE or VB6Lexer.ELSEIF => (VB6Lexer.IF, "End If without block If"),
                VB6Lexer.NEXT => (VB6Lexer.FOR, "Next without For"),
                VB6Lexer.LOOP => (VB6Lexer.DO, "Loop without Do"),
                VB6Lexer.END_SELECT or VB6Lexer.CASE => (VB6Lexer.SELECT, "Case without Select Case"),
                VB6Lexer.END_WITH => (VB6Lexer.WITH, "End With without With"),
                VB6Lexer.WEND => (VB6Lexer.WHILE, "Wend without While"),
                _ => (0, "")
            };
            if (close.Item1 == 0)
                continue;
            // An earlier malformed statement takes precedence; mixed-error precedence is unmeasured.
            if (line < first.Line)
                return ("Syntax error", line);
            if (blocks.Count == 0 || blocks[^1].Kind != close.Item1)
            {
                if (first.Type is VB6Lexer.ELSEIF or VB6Lexer.END_SELECT)
                    return ("Syntax error", line);
                return (first.Type switch
                {
                    VB6Lexer.ELSE => "Else without If",
                    _ => close.Item2
                }, first.Line);
            }
            if (first.Type is not (VB6Lexer.ELSE or VB6Lexer.ELSEIF or VB6Lexer.CASE))
                blocks.RemoveAt(blocks.Count - 1);
        }
        if (procedure == VB6Lexer.SUB)
            return ("Expected End Sub", stream.GetTokens()[^1].Line);
        return ("Syntax error", line);
    }

    private static string Missing(int kind) => kind switch
    {
        VB6Lexer.IF => "Block If without End If",
        VB6Lexer.FOR => "For without Next",
        VB6Lexer.DO => "Do without Loop",
        VB6Lexer.SELECT => "Select Case without End Select",
        VB6Lexer.WITH => "Expected End With",
        _ => "Syntax error"
    };
}
