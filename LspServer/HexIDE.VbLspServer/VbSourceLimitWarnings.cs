// SPDX-License-Identifier: MIT
using Antlr4.Runtime;

namespace HexIDE.VbLspServer;

// Compatibility notices only: #3203 deliberately keeps the interpreter's existing limits.
internal static class VbSourceLimitWarnings
{
    internal static void Add(string source, IList<IToken> tokens, List<LspDiagnostic> diagnostics,
        VbProtectedRegions? protectedRegions)
    {
        // The lexer already knows which breaks end logical lines, including comments, strings and split keywords.
        var boundaries = new HashSet<int>();
        foreach (var token in tokens)
        {
            if (token.Type != VisualBasic6Lexer.NEWLINE)
                continue;
            var index = token.StartIndex;
            while (index < source.Length && source[index] is not ('\r' or '\n'))
                index++;
            boundaries.Add(index);
        }

        var continuations = 0;
        var line = 0;
        for (var start = 0; start < source.Length; line++)
        {
            var end = start;
            while (end < source.Length && source[end] is not ('\r' or '\n'))
                end++;
            var length = end - start;
            var visible = protectedRegions?.IsProtected(line) != true;
            if (length > 1023 && visible)
                diagnostics.Add(new LspDiagnostic(
                    new LspRange(new LspPosition(line, 0), new LspPosition(line, length)),
                    "VB6 reads physical lines over 1,023 characters in 1,023-character pieces. A cut inside a number or string is a syntax error there; in some layouts VB6 silently drops the line's end. HexIDE keeps the whole line.",
                    2));

            // Each extra piece counts before the explicit join at the end of this physical line (#3202).
            for (var column = 1023; column < length; column += 1023)
                Continuation(column);
            if (end < source.Length && !boundaries.Contains(end))
            {
                var column = length - 1;
                while (column > 0 && source[start + column] is ' ' or '\t')
                    column--;
                Continuation(column);
            }
            else
                continuations = 0;

            start = end + 1;
            if (end < source.Length && source[end] == '\r' && start < source.Length && source[start] == '\n')
                start++;

            void Continuation(int column)
            {
                if (++continuations == 25 && visible)
                    diagnostics.Add(new LspDiagnostic(
                        new LspRange(new LspPosition(line, column), new LspPosition(line, column + 1)),
                        "VB6 refuses this logical line: Too many line continuations. HexIDE runs it.",
                        2));
            }
        }
    }
}
