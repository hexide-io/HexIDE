// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors

using System.Text.RegularExpressions;

namespace HexIDE.VbLspServer;

/// <summary>
/// Produces LSP foldingRange results by scanning VB6/VBA source line-by-line.
/// This is regex-based (no full parse tree needed) and handles all common block
/// constructs.  When proper ANTLR-backed comprehension is added, this provider
/// can be replaced while keeping the same LSP handler plumbing.
/// </summary>
public static class VbFoldingProvider
{
    // ── Opener patterns (case-insensitive) ────────────────────────────────────

    private static readonly Regex RxSub = new(
        @"^\s*(private\s+|public\s+|friend\s+)?(static\s+)?sub\s+\w",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxFunction = new(
        @"^\s*(private\s+|public\s+|friend\s+)?(static\s+)?function\s+\w",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxProperty = new(
        @"^\s*(private\s+|public\s+|friend\s+)?property\s+(get|let|set)\s+\w",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Multiline If: ends with "Then" optionally followed by whitespace/comment only.
    private static readonly Regex RxIf = new(
        @"^\s*if\b.+\bthen\s*('.*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxFor = new(
        @"^\s*for\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Do with no trailing statement — covers "Do", "Do While …", "Do Until …"
    private static readonly Regex RxDo = new(
        @"^\s*do(\s+(while|until)\b.*)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxWhile = new(
        @"^\s*while\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxWith = new(
        @"^\s*with\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxSelectCase = new(
        @"^\s*select\s+case\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxEnum = new(
        @"^\s*(private\s+|public\s+)?enum\s+\w",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxType = new(
        @"^\s*(private\s+|public\s+)?type\s+\w",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxHashIf = new(
        @"^\s*#if\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ── Closer patterns ───────────────────────────────────────────────────────

    private static readonly Regex RxEndSub      = new(@"^\s*end\s+sub\s*$",      RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxEndFunction  = new(@"^\s*end\s+function\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxEndProperty  = new(@"^\s*end\s+property\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxEndIf        = new(@"^\s*end\s+if\s*$",       RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxNext         = new(@"^\s*next(\s+\w+)?\s*$",  RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxLoop         = new(@"^\s*loop(\s+(while|until)\b.*)?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxWend         = new(@"^\s*wend\s*$",            RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxEndWith      = new(@"^\s*end\s+with\s*$",     RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxEndSelect    = new(@"^\s*end\s+select\s*$",   RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxEndEnum      = new(@"^\s*end\s+enum\s*$",     RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxEndType      = new(@"^\s*end\s+type\s*$",     RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxHashEndIf    = new(@"^\s*#end\s+if\s*$",      RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ── Public API ────────────────────────────────────────────────────────────

    public static List<LspFoldingRange> GetFoldings(string source)
    {
        var lines = source.Split('\n');
        var result = new List<LspFoldingRange>();
        var stack  = new Stack<(int startLine, string blockType)>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            // Strip trailing \r (Windows line endings)
            if (line.Length > 0 && line[^1] == '\r')
                line = line[..^1];

            // ── Check closers first (order matters for single-keyword lines) ──

            if (RxEndSub.IsMatch(line))        { TryClose(stack, result, i, "Sub");      continue; }
            if (RxEndFunction.IsMatch(line))   { TryClose(stack, result, i, "Function"); continue; }
            if (RxEndProperty.IsMatch(line))   { TryClose(stack, result, i, "Property"); continue; }
            if (RxEndIf.IsMatch(line))         { TryClose(stack, result, i, "If");       continue; }
            if (RxNext.IsMatch(line))          { TryClose(stack, result, i, "For");      continue; }
            if (RxLoop.IsMatch(line))          { TryClose(stack, result, i, "Do");       continue; }
            if (RxWend.IsMatch(line))          { TryClose(stack, result, i, "While");    continue; }
            if (RxEndWith.IsMatch(line))       { TryClose(stack, result, i, "With");     continue; }
            if (RxEndSelect.IsMatch(line))     { TryClose(stack, result, i, "Select");   continue; }
            if (RxEndEnum.IsMatch(line))       { TryClose(stack, result, i, "Enum");     continue; }
            if (RxEndType.IsMatch(line))       { TryClose(stack, result, i, "Type");     continue; }
            if (RxHashEndIf.IsMatch(line))     { TryClose(stack, result, i, "#If");      continue; }

            // ── Check openers ─────────────────────────────────────────────────

            if (RxSub.IsMatch(line))           { stack.Push((i, "Sub"));      continue; }
            if (RxFunction.IsMatch(line))      { stack.Push((i, "Function")); continue; }
            if (RxProperty.IsMatch(line))      { stack.Push((i, "Property")); continue; }
            if (RxIf.IsMatch(line))            { stack.Push((i, "If"));       continue; }
            if (RxFor.IsMatch(line))           { stack.Push((i, "For"));      continue; }
            if (RxDo.IsMatch(line))            { stack.Push((i, "Do"));       continue; }
            if (RxWhile.IsMatch(line))         { stack.Push((i, "While"));    continue; }
            if (RxWith.IsMatch(line))          { stack.Push((i, "With"));     continue; }
            if (RxSelectCase.IsMatch(line))    { stack.Push((i, "Select"));   continue; }
            if (RxEnum.IsMatch(line))          { stack.Push((i, "Enum"));     continue; }
            if (RxType.IsMatch(line))          { stack.Push((i, "Type"));     continue; }
            if (RxHashIf.IsMatch(line))        { stack.Push((i, "#If"));      continue; }
        }

        result.Sort((a, b) => a.StartLine.CompareTo(b.StartLine));
        return result;
    }

    private static void TryClose(
        Stack<(int startLine, string blockType)> stack,
        List<LspFoldingRange> result,
        int closerLine,
        string expectedBlockType)
    {
        if (stack.Count == 0) return;
        if (stack.Peek().blockType != expectedBlockType) return;

        var (startLine, _) = stack.Pop();
        // Only emit if the fold spans at least 2 lines (opener + closer)
        if (closerLine - startLine >= 1)
            result.Add(new LspFoldingRange(startLine, closerLine));
    }
}

public record LspFoldingRange(int StartLine, int EndLine, string? Kind = null);
