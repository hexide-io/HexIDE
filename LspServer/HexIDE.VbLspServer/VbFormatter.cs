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
    /// <remarks>
    /// <para>
    /// <b>The header and every member's attribute lines come back exactly as they were</b>
    /// (<see cref="VbProtectedRegions"/>; hexide-io/HexIDE#273 task 3.10). They are copied through untrimmed,
    /// and they are not classified, so they cannot move the indent either: a member attribute line sits inside
    /// a procedure, and the body after it must still be indented as the body. Formatting them would rewrite a
    /// form's layout on every save — its designer block is indented by three spaces, VB6 writes a trailing
    /// space after each <c>Begin</c> line, and a class header's <c>END</c> would be re-cased.
    /// </para>
    /// <para>
    /// <b>Every line keeps its own terminator.</b> The formatter used to join its output with <c>\n</c>, so a
    /// CRLF file came back LF on every line, and no VB6 file was ever "already formatted".
    /// </para>
    /// </remarks>
    public static string? Format(string source)
    {
        var lines = FormatLines(source);
        var sb = new StringBuilder(source.Length);
        foreach (var line in lines)
            sb.Append(line.Formatted).Append(line.Terminator);

        var result = sb.ToString();
        return result == source ? null : result;
    }

    /// <summary>
    /// The formatting answer as edits: one per run of consecutive lines whose text changes, each covering
    /// those lines' text and not their terminators. Empty when nothing changes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why not one edit spanning the document</b>, which is what this server used to send: a whole-document
    /// edit points inside the header even when the text it carries leaves the header alone, and the
    /// requirement is that no answer changes or points inside it. The formatter maps lines one to one — it
    /// never adds or removes a line — so the changed lines are found by comparing them in place, and a
    /// protected line is never changed, so no edit can cover one.
    /// </para>
    /// <para>
    /// A client applies the edits together, so the whole format is still one undo step in any client that
    /// groups an answer's edits, as the IDE does.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<VbFormattingEdit> Edits(string source)
    {
        var lines = FormatLines(source);
        var edits = new List<VbFormattingEdit>();

        var i = 0;
        while (i < lines.Count)
        {
            if (lines[i].Formatted == lines[i].Original)
            {
                i++;
                continue;
            }

            var first = i;
            var text = new StringBuilder(lines[i].Formatted);
            while (i + 1 < lines.Count && lines[i + 1].Formatted != lines[i + 1].Original)
            {
                text.Append(lines[i].Terminator).Append(lines[i + 1].Formatted);
                i++;
            }
            edits.Add(new VbFormattingEdit(
                new LspRange(new LspPosition(first, 0), new LspPosition(i, lines[i].Original.Length)),
                text.ToString()));
            i++;
        }
        return edits;
    }

    /// <summary>One line of the source: its text, its formatted text and the terminator it ends with.</summary>
    private readonly record struct FormattedLine(string Original, string Formatted, string Terminator);

    private static List<FormattedLine> FormatLines(string source)
    {
        var regions = VbProtectedRegions.Of(source);
        var result = new List<FormattedLine>();
        int indent = 0;

        var pos = 0;
        var lineNumber = 0;
        while (pos <= source.Length)
        {
            var newline = source.IndexOf('\n', pos);
            var end = newline < 0 ? source.Length : newline;
            var contentEnd = end > pos && source[end - 1] == '\r' ? end - 1 : end;
            var raw = source[pos..contentEnd];
            var terminator = source[contentEnd..(newline < 0 ? source.Length : newline + 1)];

            result.Add(new FormattedLine(raw, FormatLine(raw, regions.IsProtected(lineNumber), ref indent), terminator));

            if (newline < 0)
                break;
            pos = newline + 1;
            lineNumber++;
        }
        return result;
    }

    private static string FormatLine(string raw, bool isProtected, ref int indent)
    {
        // Copied through as it is, and not classified: it neither opens nor closes a block.
        if (isProtected)
            return raw;

        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
            return string.Empty;

        // Check if this line is a closer (dedent before writing)
        bool isCloser = IsCloser(trimmed);
        bool isMidBlock = !isCloser && IsMidBlock(trimmed);

        if (isCloser)
            indent = Math.Max(0, indent - 1);
        else if (isMidBlock)
            indent = Math.Max(0, indent - 1);

        // Normalize keyword casing in the trimmed line, and indent it
        var formatted = indent > 0
            ? new string(' ', indent * IndentSize) + NormalizeKeywords(trimmed)
            : NormalizeKeywords(trimmed);

        // Re-indent after mid-block lines
        if (isMidBlock)
            indent++;

        // Check if this line is an opener (indent after writing)
        if (IsOpener(trimmed))
            indent++;

        return formatted;
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

/// <summary>One edit of a formatting answer: the range it replaces and the text it puts there.</summary>
public readonly record struct VbFormattingEdit(LspRange Range, string NewText);
