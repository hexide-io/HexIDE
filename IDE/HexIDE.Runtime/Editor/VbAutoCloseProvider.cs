// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// This file is part of HexIDE.Runtime.

using System.Text.RegularExpressions;

namespace HexIDE.Runtime.Editor;

/// <summary>
/// Provides auto-close logic for VB6 block statements.
/// Given a committed logical line, determines the matching closing statement.
/// Also provides helpers for VB6 logical-line handling (continuations, indentation).
/// </summary>
/// <remarks>
/// Regex patterns are clean-room derived from the VB6 language specification.
/// This class is clean-room and self-contained — it does not reference or copy
/// from the LSP server's own VbFoldingProvider/VbFormatter (a separate copy).
/// </remarks>
public static class VbAutoCloseProvider
{
    private const int IndentSize = 4;

    // ── Block opener patterns ────────────────────────────────────────────────

    private static readonly (Regex Pattern, string Closer)[] BlockRules =
    [
        // Declare Sub/Function must be checked FIRST — it's not a block opener.
        // The negative match is handled by excluding "declare" from Sub/Function patterns.

        // Sub (but not "Declare Sub")
        (new Regex(
            @"^\s*(private\s+|public\s+|friend\s+)?(static\s+)?sub\s+\w",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "End Sub"),

        // Function (but not "Declare Function")
        (new Regex(
            @"^\s*(private\s+|public\s+|friend\s+)?(static\s+)?function\s+\w",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "End Function"),

        // Property Get/Let/Set
        (new Regex(
            @"^\s*(private\s+|public\s+|friend\s+)?property\s+(get|let|set)\s+\w",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "End Property"),

        // If...Then (multi-line only — nothing after "Then" except optional comment)
        // ElseIf is excluded — it's mid-block, not a new block.
        (new Regex(
            @"^\s*if\b.+\bthen\s*('.*)?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "End If"),

        // Select Case
        (new Regex(
            @"^\s*select\s+case\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "End Select"),

        // For / For Each
        (new Regex(
            @"^\s*for\s+(each\s+)?\w",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "Next"),

        // Do [While|Until]
        (new Regex(
            @"^\s*do(\s+(while|until)\b.*)?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "Loop"),

        // While (standalone, not Do While)
        (new Regex(
            @"^\s*while\s+\S",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "Wend"),

        // With
        (new Regex(
            @"^\s*with\s+\S",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "End With"),

        // Type (UDT)
        (new Regex(
            @"^\s*(private\s+|public\s+)?type\s+\w",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "End Type"),

        // Enum
        (new Regex(
            @"^\s*(private\s+|public\s+)?enum\s+\w",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "End Enum"),

        // #If preprocessor
        (new Regex(
            @"^\s*#if\b.+\bthen\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "#End If"),
    ];

    // Declare Sub/Function — explicit exclusion pattern
    private static readonly Regex RxDeclare = new(
        @"^\s*(private\s+|public\s+)?declare\s+(sub|function)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Given a committed logical line (all continuations joined, trimmed of
    /// trailing whitespace), returns the closing statement that should be
    /// inserted, or <c>null</c> if the line doesn't open a block.
    /// </summary>
    public static string? GetClosingStatement(string logicalLine)
    {
        if (string.IsNullOrWhiteSpace(logicalLine))
            return null;

        // Strip inline comment for matching (but keep the original for Declare check)
        var stripped = StripTrailingComment(logicalLine);
        if (string.IsNullOrWhiteSpace(stripped))
            return null;

        // Declare Sub/Function is NOT a block — it's an API declaration
        if (RxDeclare.IsMatch(stripped))
            return null;

        // ElseIf is mid-block — don't auto-close
        if (Regex.IsMatch(stripped, @"^\s*elseif\b", RegexOptions.IgnoreCase))
            return null;

        foreach (var (pattern, closer) in BlockRules)
        {
            if (pattern.IsMatch(stripped))
                return closer;
        }

        return null;
    }

    // ── Logical line helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the line ends with a VB6 continuation character (<c>_</c>),
    /// meaning the logical line continues on the next physical line.
    /// </summary>
    public static bool IsContinuationLine(string line)
    {
        var trimmed = line.TrimEnd();
        return trimmed.Length > 0 && trimmed[^1] == '_';
    }

    /// <summary>
    /// Assembles a complete logical line by walking backward from
    /// <paramref name="lineIndex"/> through any preceding continuation lines.
    /// Returns the joined logical line with continuations stripped.
    /// </summary>
    /// <param name="getLine">Function that returns the text of a given line index.</param>
    /// <param name="lineIndex">The index of the current (final) line.</param>
    /// <returns>The assembled logical line.</returns>
    public static string AssembleLogicalLine(Func<int, string> getLine, int lineIndex)
    {
        // Walk backward to find the start of the logical line
        var startLine = lineIndex;
        while (startLine > 0 && IsContinuationLine(getLine(startLine - 1)))
            startLine--;

        if (startLine == lineIndex)
            return getLine(lineIndex);

        // Join all physical lines, stripping the continuation character
        var parts = new List<string>();
        for (var i = startLine; i <= lineIndex; i++)
        {
            var text = getLine(i);
            if (i < lineIndex && IsContinuationLine(text))
            {
                // Strip trailing _ and whitespace before it;
                // also strip leading whitespace on continuation lines (not the first)
                var trimmed = text.TrimEnd();
                var stripped = trimmed[..^1].TrimEnd();
                parts.Add(i == startLine ? stripped : stripped.TrimStart());
            }
            else
            {
                // Final line: trim leading whitespace (continuation indent)
                parts.Add(i == startLine ? text.TrimEnd() : text.Trim());
            }
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Returns the leading whitespace of the given line.
    /// </summary>
    public static string GetIndentation(string line)
    {
        var i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
            i++;
        return line[..i];
    }

    /// <summary>
    /// Creates the indented body indent string (current indent + 4 spaces).
    /// </summary>
    public static string GetBodyIndent(string currentIndent)
    {
        return currentIndent + new string(' ', IndentSize);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Strips a trailing VB6 comment (starting with <c>'</c>) from the line,
    /// taking care not to strip inside string literals.
    /// </summary>
    private static string StripTrailingComment(string line)
    {
        var inString = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                inString = !inString;
            }
            else if (ch == '\'' && !inString)
            {
                return line[..i].TrimEnd();
            }
        }
        return line;
    }
}
