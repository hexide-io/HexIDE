// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors

namespace HexIDE.Language;

/// <summary>Lexical context before a VB caret, including unfinished strings and continued comments.</summary>
public static class VbFreeText
{
    public static bool ContainsPosition(string source, int line, int column)
    {
        var start = 0;
        for (var current = 0; current < line && start < source.Length; current++)
        {
            while (start < source.Length && source[start] is not ('\r' or '\n')) start++;
            if (start < source.Length && source[start++] == '\r'
                && start < source.Length && source[start] == '\n') start++;
        }
        var end = start;
        while (end < source.Length && source[end] is not ('\r' or '\n')) end++;
        return ContainsCaret(source, start + System.Math.Clamp(column, 0, end - start));
    }

    public static bool ContainsCaret(string source, int offset)
    {
        var end = System.Math.Clamp(offset, 0, source.Length);
        var start = end;
        while (start > 0 && source[start - 1] is not ('\r' or '\n')) start--;
        while (start > 0)
        {
            var previousEnd = start - 1;
            if (source[previousEnd] == '\n' && previousEnd > 0 && source[previousEnd - 1] == '\r')
                previousEnd--;
            if (previousEnd < 2 || source[previousEnd - 1] != '_' || source[previousEnd - 2] != ' ')
                break;
            start = previousEnd;
            while (start > 0 && source[start - 1] is not ('\r' or '\n')) start--;
        }
        var inString = false;
        var inBracket = false;
        for (var i = start; i < end; i++)
        {
            var ch = source[i];
            if (ch is '\r' or '\n') { inBracket = false; continue; }
            if (inBracket)
            {
                if (ch == ']') inBracket = false;
                continue;
            }
            if (ch == '"')
            {
                if (inString && i + 1 < end && source[i + 1] == '"') { i++; continue; }
                inString = !inString;
            }
            if (inString) continue;
            if (ch == '[') { inBracket = true; continue; }
            if (ch == '\'') return true;
            if ((i == 0 || !(char.IsLetterOrDigit(source[i - 1]) || source[i - 1] is '_' or '.'))
                && i + 3 < end
                && source.AsSpan(i, 3).Equals("Rem", System.StringComparison.OrdinalIgnoreCase)
                && (i + 3 == source.Length || !(char.IsLetterOrDigit(source[i + 3]) || source[i + 3] == '_')))
                return true;
        }
        return inString;
    }
}
