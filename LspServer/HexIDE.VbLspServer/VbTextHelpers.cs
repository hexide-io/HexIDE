// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// Text-based position/word helpers shared by the LSP request handlers (grammar-agnostic).

namespace HexIDE.VbLspServer;

internal static class VbTextHelpers
{
    // Content end and start of the next line; CRLF is one boundary, as in LSP and AvaloniaEdit.
    internal static (int End, int Next) LineEnd(string source, int start)
    {
        var end = start;
        while (end < source.Length && source[end] is not ('\r' or '\n'))
            end++;
        var next = end;
        if (next < source.Length)
        {
            if (source[next++] == '\r' && next < source.Length && source[next] == '\n')
                next++;
        }
        return (end, next);
    }

    public static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// Whether <paramref name="name"/> can be a VB6 name: a letter, then letters, digits and underscores. The
    /// IDE's <c>ProjectNaming.IsValidName</c>, which the code window holds a rename's new name to before it asks
    /// (hexide-io/HexIDE#273 task 4.4); a rename to anything else is answered with nothing here.
    /// </summary>
    /// <remarks>
    /// A leading or lone underscore and a bracketed name were measured illegal against <c>vb6.exe</c>; a leading
    /// digit, whitespace or punctuation cannot be in a name at all. Nor can the six words VB6 was measured to
    /// refuse as a name, in every position tried and accepted in none: <c>Rem</c>, <c>Empty</c>, <c>New</c>,
    /// <c>Nothing</c>, <c>Me</c> and <c>Attribute</c>, the IDE's <c>ProjectNaming.IsReservedWord</c>. No other
    /// keyword is refused, because none other was measured as a name, and a letter outside ASCII is not, because
    /// VB6 may accept one in its code page.
    /// </remarks>
    public static bool IsName(string name)
    {
        if (name.Length == 0 || !char.IsLetter(name[0]))
            return false;
        foreach (var c in name)
        {
            if (!IsIdentifierChar(c))
                return false;
        }
        return !ReservedWords.Contains(name);
    }

    private static readonly HashSet<string> ReservedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Rem", "Empty", "New", "Nothing", "Me", "Attribute",
    };

    public static bool ContainsPosition(LspRange range, int line, int ch)
    {
        var (sl, sc) = (range.Start.Line, range.Start.Character);
        var (el, ec) = (range.End.Line,   range.End.Character);
        if (line < sl || line > el) return false;
        if (line == sl && ch < sc)  return false;
        if (line == el && ch >= ec) return false;
        return true;
    }

    /// <summary>Returns the identifier under (line, character), or null.</summary>
    public static string? GetWordAtPosition(string source, int line, int character)
    {
        // Convert LSP (line, character) to flat offset in source.
        int currentLine = 0;
        int lineStart = 0;
        int lineEnd = source.Length;

        while (true)
        {
            var (contentEnd, next) = LineEnd(source, lineStart);
            lineEnd = contentEnd;
            if (currentLine == line || next == contentEnd)
                break;
            currentLine++;
            lineStart = next;
        }

        if (currentLine != line) return null;

        // lineStart..lineEnd is the target line's content (excluding its terminator).
        var col = character;
        var lineLen = lineEnd - lineStart;
        if (col > lineLen) col = lineLen;

        var start = col;
        while (start > 0 && IsIdentifierChar(source[lineStart + start - 1]))
            start--;

        var end = col;
        while (end < lineLen && IsIdentifierChar(source[lineStart + end]))
            end++;

        return end > start ? source.Substring(lineStart + start, end - start) : null;
    }

    /// <summary>Whole-word, case-insensitive occurrences of <paramref name="word"/>, one range each.</summary>
    public static List<LspRange> FindAllOccurrences(string source, string word)
    {
        var results = new List<LspRange>();
        int lineNum = 0;
        int i = 0;
        while (i < source.Length)
        {
            int lineStart = i;
            var (end, next) = LineEnd(source, i);
            i = next;

            int pos = lineStart;
            while (pos < end)
            {
                int idx = source.IndexOf(word, pos, end - pos, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) break;

                bool leftOk  = idx == lineStart          || !IsIdentifierChar(source[idx - 1]);
                bool rightOk = idx + word.Length >= end  || !IsIdentifierChar(source[idx + word.Length]);

                // Only a real code token counts — never the same word inside a string literal or a ' comment.
                // Renaming those silently corrupted the string shown to the user and the comment.
                if (leftOk && rightOk && !InStringOrComment(source, lineStart, end, idx))
                {
                    int charOffset = idx - lineStart;
                    results.Add(new LspRange(
                        new LspPosition(lineNum, charOffset),
                        new LspPosition(lineNum, charOffset + word.Length)));
                }
                pos = idx + word.Length;
            }
            lineNum++;
        }
        return results;
    }

    // True if position <paramref name="idx"/> on the source line [lineStart, end) falls inside a string literal or a
    // ' comment. Scans from the line start tracking quote state; a doubled "" inside a string is an escaped quote
    // (stays in the string), and a ' outside a string opens a comment to end-of-line.
    private static bool InStringOrComment(string source, int lineStart, int end, int idx)
    {
        bool inString = false;
        for (int p = lineStart; p < idx && p < end; p++)
        {
            char c = source[p];
            if (c == '"')
            {
                if (inString && p + 1 < end && source[p + 1] == '"') { p++; continue; }   // escaped "" — still in string
                inString = !inString;
            }
            else if (c == '\'' && !inString)
            {
                return true;   // comment starts before idx → idx is inside the comment
            }
        }
        return inString;
    }

    /// <summary>
    /// Scans backwards from (line, character) to find the nearest unclosed '(' and the identifier
    /// immediately preceding it. Returns (functionName, activeParamIndex) or (null, 0).
    /// </summary>
    public static (string? FunctionName, int ActiveParameter) FindCallContext(string source, int line, int character)
    {
        int lineStart = 0;
        for (var currentLine = 0; currentLine < line; currentLine++)
        {
            var (end, next) = LineEnd(source, lineStart);
            if (end == next) return (null, 0);
            lineStart = next;
        }
        var lineEnd = LineEnd(source, lineStart).End;
        var offset = lineStart + Math.Clamp(character, 0, lineEnd - lineStart);

        int depth = 0;
        int commas = 0;
        for (int i = offset - 1; i >= 0; i--)
        {
            char c = source[i];
            if (c == ')') { depth++; continue; }
            if (c == '(')
            {
                if (depth > 0) { depth--; continue; }
                int nameEnd = i - 1;
                while (nameEnd >= 0 && source[nameEnd] == ' ') nameEnd--;
                if (nameEnd < 0) return (null, 0);
                if (!char.IsLetterOrDigit(source[nameEnd]) && source[nameEnd] != '_')
                    return (null, 0);
                int nameStart = nameEnd;
                while (nameStart > 0 && (char.IsLetterOrDigit(source[nameStart - 1]) || source[nameStart - 1] == '_'))
                    nameStart--;
                var name = source.Substring(nameStart, nameEnd - nameStart + 1);
                return (name, commas);
            }
            if (c == ',' && depth == 0) commas++;
            // Stop at statement boundaries — don't cross lines.
            if (c is '\r' or '\n' or ':') return (null, 0);
        }
        return (null, 0);
    }
}
