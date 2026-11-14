using Antlr4.Runtime;

namespace HexIDE.Runtime.Interpreter;

// Syntax only: #3434's closed list, checked before parsing so no statement can run first.
internal static class OtherDialectRefusal
{
    public static void Check(CommonTokenStream stream, string source, bool wordOperators = false)
    {
        stream.Fill();
        var tokens = stream.GetTokens().Where(t => t.Channel == TokenConstants.DefaultChannel
            && t.Type != VB6Lexer.WS && t.Type != TokenConstants.EOF).ToArray();
        var start = 0;
        var inProcedure = false;
        var declarationType = false;
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            var text = token.Text;
            bool Is(int at, string value) => at < tokens.Length && tokens[at].Text.Equals(value, StringComparison.OrdinalIgnoreCase);
            if (token.Type is VB6Lexer.NEWLINE or VB6Lexer.COLON or VB6Lexer.THEN or VB6Lexer.ELSE)
            {
                start = i + 1;
                declarationType = false;
                continue;
            }
            if (i == start && token.Type == VB6Lexer.INTEGERLITERAL)
            {
                start = i + 1;
                declarationType = false;
                continue;
            }
            if (token.Type is VB6Lexer.END_SUB or VB6Lexer.END_FUNCTION or VB6Lexer.END_PROPERTY)
                inProcedure = false;
            if (token.Type is VB6Lexer.SUB or VB6Lexer.FUNCTION or VB6Lexer.PROPERTY_GET
                or VB6Lexer.PROPERTY_LET or VB6Lexer.PROPERTY_SET)
                inProcedure = true;
            if (text == "[")
            {
                var end = i + 1;
                while (end < tokens.Length && tokens[end].Type != VB6Lexer.NEWLINE && !Is(end, "]")) end++;
                if (end < tokens.Length && Is(end, "]"))
                {
                    var after = end + 1;
                    while (after < tokens.Length && tokens[after].Type == VB6Lexer.NEWLINE) after++;
                    var beforeDeclaration = !inProcedure && after > end + 1 && after < tokens.Length
                        && tokens[after].Type is VB6Lexer.SUB or VB6Lexer.FUNCTION or VB6Lexer.PROPERTY_GET
                            or VB6Lexer.PROPERTY_LET or VB6Lexer.PROPERTY_SET or VB6Lexer.PUBLIC or VB6Lexer.PRIVATE
                            or VB6Lexer.FRIEND or VB6Lexer.DIM or VB6Lexer.STATIC or VB6Lexer.GLOBAL
                            or VB6Lexer.CONST or VB6Lexer.TYPE or VB6Lexer.ENUM or VB6Lexer.EVENT or VB6Lexer.DECLARE;
                    if (i == start && (beforeDeclaration || tokens.Skip(i + 1).Take(end - i - 1).Any(t => t.Text == "(")))
                        Refuse(source[token.StartIndex..(tokens[end].StopIndex + 1)], token);
                    i = end; // Bracketed identifiers are opaque, including names which look like keywords.
                }
                continue;
            }
            if (token.Type == VB6Lexer.COMMA) declarationType = false;
            if (token.Type == VB6Lexer.AS) declarationType = true;
            if (token.Type == VB6Lexer.CONTINUE_DO) Refuse(text, token);
            if (text is "+=" or "-=") Refuse(text, token);
            // In `n&=5` the `&` is n's Long type suffix, written against the name, and the `=` is VB6's own.
            var typeSuffix = text == "&" && i > 0 && tokens[i - 1].StopIndex + 1 == token.StartIndex
                && (char.IsLetterOrDigit(tokens[i - 1].Text[^1]) || tokens[i - 1].Text[^1] is ']' or '_');
            if (text is "*" or "/" or "\\" or "^" or "&" && !typeSuffix && Is(i + 1, "=")
                && token.StopIndex + 1 == tokens[i + 1].StartIndex)
                Refuse(text + "=", token);
            if (text is "<" or ">" && Is(i + 1, text)
                && token.StopIndex + 1 == tokens[i + 1].StartIndex)
                Refuse(text + text, token);
            // These words can also be classic identifiers; only classify them after syntax parsing failed.
            if (wordOperators && (Is(i, "AndAlso") || Is(i, "OrElse")) && i > start
                && i + 1 < tokens.Length && !Is(i - 1, ".") && !Is(i + 1, "As") && !Is(i + 1, "=")
                && tokens[i - 1].Type is not (VB6Lexer.SUB or VB6Lexer.FUNCTION or VB6Lexer.LPAREN or VB6Lexer.COMMA)
                && tokens[i + 1].Type is not (VB6Lexer.NEWLINE or VB6Lexer.COLON or VB6Lexer.COMMA))
                Refuse(text, token);
            if (i == start && Is(i, "Return") && i + 1 < tokens.Length
                && tokens[i + 1].Type is not (VB6Lexer.NEWLINE or VB6Lexer.COLON or VB6Lexer.ELSE))
                Refuse(text, token);
            if (i == start && Is(i, "Continue") && (Is(i + 1, "For") || Is(i + 1, "Do") || Is(i + 1, "While")))
                Refuse(source[token.StartIndex..(tokens[i + 1].StopIndex + 1)], token);
            // `(Of T)` names a type after Of. `Abs(Of)` or `F(Of, 2)` passes a variable named Of, which this interpreter runs.
            if (text == "(" && Is(i + 1, "Of") && i + 3 < tokens.Length
                && (char.IsLetter(tokens[i + 2].Text[0]) || tokens[i + 2].Text[0] == '[')
                && (Is(i + 3, ")") || Is(i + 3, ",") || Is(i + 3, "(")))
            {
                var end = i + 2;
                while (end < tokens.Length && tokens[end].Type != VB6Lexer.NEWLINE && !Is(end, ")")) end++;
                if (end < tokens.Length && Is(end, ")"))
                    Refuse(source[token.StartIndex..(tokens[end].StopIndex + 1)], token);
            }
            if (text == "=" && (Is(start, "Dim") || Is(start, "Private") || Is(start, "Public")
                || Is(start, "Static") || Is(start, "Global"))
                && declarationType
                && !tokens.Skip(start).Take(i - start).Any(t => new[] { "Const", "Sub", "Function", "Property", "Declare", "Event" }
                    .Contains(t.Text, StringComparer.OrdinalIgnoreCase)))
                Refuse(text, token);
        }
        stream.Seek(0);
    }

    internal static void Refuse(string construct, IToken token) =>
        throw new VBCompileErrorException($"{construct} isn't part of VB6, VBA or VBScript, so this interpreter doesn't run it.")
            { Line = token.Line };
}
