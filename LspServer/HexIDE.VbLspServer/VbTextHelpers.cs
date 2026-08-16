// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// Text-based position/word helpers shared by the LSP request handlers (grammar-agnostic).

namespace HexIDE.VbLspServer;

internal static class VbTextHelpers
{
    public static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

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

        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == '\r') continue;
            if (source[i] == '\n')
            {
                if (currentLine == line)
                {
                    lineEnd = i;
                    break;
                }
                currentLine++;
                lineStart = i + 1;
            }
        }

        if (currentLine != line) return null;

        // lineStart..lineEnd is the target line's content (excluding \r\n).
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
            while (i < source.Length && source[i] != '\n') i++;
            int lineEnd = i;
            if (i < source.Length) i++; // skip \n

            int end = lineEnd;
            if (end > lineStart && source[end - 1] == '\r') end--;

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
        // Convert (line, character) to a flat offset. Both \n and \r\n are handled (\r is invisible).
        int offset;
        int currentLine = 0;
        int lineStart = 0;
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '\r') continue;
            if (c == '\n')
            {
                if (currentLine == line && character >= (i - lineStart))
                {
                    offset = i;
                    goto found;
                }
                currentLine++;
                lineStart = i + 1;
                continue;
            }
            if (currentLine == line && (i - lineStart) == character)
            {
                offset = i;
                goto found;
            }
        }
        offset = source.Length;
        found:

        int depth = 0;
        int commas = 0;
        for (int i = offset - 1; i >= 0; i--)
        {
            char c = source[i];
            if (c == '\r') continue;
            if (c == ')') { depth++; continue; }
            if (c == '(')
            {
                if (depth > 0) { depth--; continue; }
                int nameEnd = i - 1;
                while (nameEnd >= 0 && (source[nameEnd] == ' ' || source[nameEnd] == '\r')) nameEnd--;
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
            if (c == '\n' || c == ':') return (null, 0);
        }
        return (null, 0);
    }
}
