// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors

using System.Text;
using System.Text.RegularExpressions;

namespace HexIDE.VbLspServer;

/// <summary>
/// Formats VB6/VBA source code: normalizes keyword casing to PascalCase
/// and applies canonical indentation (4-space indent per block depth).
/// </summary>
public static class VbFormatter
{
    private const int IndentSize = 4;

    // ── Keyword casing table ──────────────────────────────────────────────────

    private static readonly Dictionary<string, string> KeywordMap = BuildKeywordMap();

    private static Dictionary<string, string> BuildKeywordMap()
    {
        // Single-word keywords and built-ins in canonical PascalCase.
        // Multi-word completions like "End Sub" are handled by the multi-word list.
        string[] keywords =
        [
            "Dim", "ReDim", "Preserve", "Static", "Public", "Private", "Friend",
            "Global", "Const", "Enum", "Type", "Implements", "As", "New",
            "ByVal", "ByRef", "Optional", "ParamArray",
            "Sub", "Function", "Property", "Get", "Let", "Set",
            "Exit", "End",
            "If", "Then", "Else", "ElseIf",
            "Select", "Case",
            "For", "To", "Step", "Next", "Each", "In",
            "Do", "Loop", "While", "Until", "Wend",
            "With", "GoTo", "GoSub", "Return", "Resume", "On", "Error",
            "Call", "Nothing",
            "True", "False", "Empty", "Null",
            "Boolean", "Byte", "Integer", "Long", "Single", "Double",
            "Currency", "Date", "String", "Object", "Variant",
            "Not", "And", "Or", "Xor", "Mod", "Like", "Is",
            "Option", "Explicit", "Base",
            // Common built-ins
            "MsgBox", "InputBox", "Print", "Debug", "Err",
            "Len", "Left", "Right", "Mid", "Trim", "LTrim", "RTrim",
            "UCase", "LCase", "InStr", "InStrRev", "Replace", "Split", "Join",
            "CStr", "CInt", "CLng", "CDbl", "CBool", "CDate",
            "Int", "Fix", "Abs", "Sqr", "Rnd",
            "Now", "Time", "Timer", "Array", "UBound", "LBound",
            "TypeName", "VarType", "Chr", "Asc", "Format",
            "IsNull", "IsEmpty", "IsObject", "IsNumeric", "IsMissing",
        ];

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kw in keywords)
            map[kw] = kw;
        return map;
    }

    // ── Block indent patterns (reuse same logic as VbFoldingProvider) ─────────

    private static readonly Regex RxOpenerSub = new(
        @"^\s*(private\s+|public\s+|friend\s+)?(static\s+)?sub\s+\w",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxOpenerFunction = new(
        @"^\s*(private\s+|public\s+|friend\s+)?(static\s+)?function\s+\w",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxOpenerProperty = new(
        @"^\s*(private\s+|public\s+|friend\s+)?property\s+(get|let|set)\s+\w",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // NB: matches `If … Then` only — NOT `ElseIf … Then`. ElseIf is a mid-block line (RxElseIf) that dedents its own
    // line then re-indents its body; also treating it as an opener would add a SECOND indent level, double-indenting
    // the ElseIf body (and pushing End If in with it). A block-If opener is `If <cond> Then` with nothing after Then.
    private static readonly Regex RxOpenerIf = new(
        @"^\s*if\b.+\bthen\s*('.*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxOpenerFor = new(
        @"^\s*for\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxOpenerDo = new(
        @"^\s*do(\s+(while|until)\b.*)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxOpenerWhile = new(
        @"^\s*while\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxOpenerWith = new(
        @"^\s*with\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxOpenerSelect = new(
        @"^\s*select\s+case\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxOpenerEnum = new(
        @"^\s*(private\s+|public\s+)?enum\s+\w",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxOpenerType = new(
        @"^\s*(private\s+|public\s+)?type\s+\w",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Closers
    private static readonly Regex RxCloserEndSub      = new(@"^\s*end\s+sub\b",      RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxCloserEndFunction  = new(@"^\s*end\s+function\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxCloserEndProperty  = new(@"^\s*end\s+property\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxCloserEndIf        = new(@"^\s*end\s+if\b",       RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxCloserNext         = new(@"^\s*next\b",           RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxCloserLoop         = new(@"^\s*loop\b",           RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxCloserWend         = new(@"^\s*wend\b",           RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxCloserEndWith      = new(@"^\s*end\s+with\b",     RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxCloserEndSelect    = new(@"^\s*end\s+select\b",   RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxCloserEndEnum      = new(@"^\s*end\s+enum\b",     RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxCloserEndType      = new(@"^\s*end\s+type\b",     RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Mid-block lines that dedent then re-indent (Else, ElseIf, Case)
    private static readonly Regex RxElse    = new(@"^\s*else\s*$",     RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxElseIf  = new(@"^\s*elseif\b",    RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxCase    = new(@"^\s*case\b",      RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Formats the given VB6/VBA source code. Returns <c>null</c> if the
    /// formatted text is identical to the input (no edits needed).
    /// </summary>
    public static string? Format(string source)
    {
        var lines = source.Split('\n');
        var sb = new StringBuilder(source.Length);
        int indent = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            // Strip trailing \r
            if (raw.Length > 0 && raw[^1] == '\r')
                raw = raw[..^1];

            var trimmed = raw.Trim();

            // Blank lines: emit empty
            if (trimmed.Length == 0)
            {
                if (i > 0) sb.Append('\n');
                continue;
            }

            // Check if this line is a closer (dedent before writing)
            bool isCloser = IsCloser(trimmed);
            bool isMidBlock = !isCloser && IsMidBlock(trimmed);

            if (isCloser)
                indent = Math.Max(0, indent - 1);
            else if (isMidBlock)
                indent = Math.Max(0, indent - 1);

            // Normalize keyword casing in the trimmed line
            var formatted = NormalizeKeywords(trimmed);

            // Write indented line
            if (i > 0) sb.Append('\n');
            if (indent > 0)
                sb.Append(' ', indent * IndentSize);
            sb.Append(formatted);

            // Re-indent after mid-block lines
            if (isMidBlock)
                indent++;

            // Check if this line is an opener (indent after writing)
            if (IsOpener(trimmed))
                indent++;
        }

        var result = sb.ToString();
        return result == source || result == source.TrimEnd('\r', '\n') ? null : result;
    }

    // ── Indent classification ─────────────────────────────────────────────────

    private static bool IsOpener(string trimmed)
    {
        return RxOpenerSub.IsMatch(trimmed)
            || RxOpenerFunction.IsMatch(trimmed)
            || RxOpenerProperty.IsMatch(trimmed)
            || RxOpenerIf.IsMatch(trimmed)
            || RxOpenerFor.IsMatch(trimmed)
            || RxOpenerDo.IsMatch(trimmed)
            || RxOpenerWhile.IsMatch(trimmed)
            || RxOpenerWith.IsMatch(trimmed)
            || RxOpenerSelect.IsMatch(trimmed)
            || RxOpenerEnum.IsMatch(trimmed)
            || RxOpenerType.IsMatch(trimmed);
    }

    private static bool IsCloser(string trimmed)
    {
        return RxCloserEndSub.IsMatch(trimmed)
            || RxCloserEndFunction.IsMatch(trimmed)
            || RxCloserEndProperty.IsMatch(trimmed)
            || RxCloserEndIf.IsMatch(trimmed)
            || RxCloserNext.IsMatch(trimmed)
            || RxCloserLoop.IsMatch(trimmed)
            || RxCloserWend.IsMatch(trimmed)
            || RxCloserEndWith.IsMatch(trimmed)
            || RxCloserEndSelect.IsMatch(trimmed)
            || RxCloserEndEnum.IsMatch(trimmed)
            || RxCloserEndType.IsMatch(trimmed);
    }

    private static bool IsMidBlock(string trimmed)
    {
        return RxElse.IsMatch(trimmed)
            || RxElseIf.IsMatch(trimmed)
            || RxCase.IsMatch(trimmed);
    }

    // ── Keyword casing ────────────────────────────────────────────────────────

    private static string NormalizeKeywords(string line)
    {
        // Walk the line, find identifier-like tokens, replace known keywords
        var sb = new StringBuilder(line.Length);
        int i = 0;
        bool inString = false;

        while (i < line.Length)
        {
            char c = line[i];

            // Track string literals — don't touch content inside quotes
            if (c == '"')
            {
                inString = !inString;
                sb.Append(c);
                i++;
                continue;
            }

            if (inString)
            {
                sb.Append(c);
                i++;
                continue;
            }

            // Comment — rest of line is untouched
            if (c == '\'')
            {
                sb.Append(line, i, line.Length - i);
                break;
            }

            // Identifier / keyword token
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_'))
                    i++;

                var token = line[start..i];
                if (KeywordMap.TryGetValue(token, out var canonical))
                    sb.Append(canonical);
                else
                    sb.Append(token);
                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }
}
