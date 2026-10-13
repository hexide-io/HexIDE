// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors

namespace HexIDE.VbLspServer;

/// <summary>
/// The lines of a VB6 file that no answer of this server may change or point into: the file's header, and
/// every member's <c>Attribute</c> lines (hexide-io/HexIDE#273 task 3.10).
/// </summary>
/// <remarks>
/// <para>
/// <b>One helper, read by every answer that could reach them</b> — diagnostics, formatting, rename and
/// highlight — so the four cannot drift apart. Each of them used to know nothing about a header, because
/// until the client started sending the whole file there was none to know about.
/// </para>
/// <para>
/// <b>The header</b> is the top of the file through the last line of the leading <c>Attribute</c> run: a
/// <c>VERSION</c> line, then a class's <c>BEGIN … END</c> block or a form's <c>Object</c> lines and designer
/// block, then the attribute run. A text with none of this has no header. <b>A member's attribute lines</b>
/// are a run of <c>Attribute Name.VB_Something = …</c> lines below the header that describes a member: the
/// logical line directly above the run declares the name before the first <c>.</c> of every line in it
/// (task 4.1). A run that does not — under a blank line, a comment, a body statement or another member, or a
/// line with no <c>.</c> — is an orphan, which is ordinary text here as it is in the IDE.
/// </para>
/// <para>
/// <b>This is the IDE's rule, ported rather than shared.</b> The IDE's own copy is
/// <c>IDE/HexIDE.Runtime/Serialization/ReadOnlyRegions.cs</c>, and the two halves of the repository do not
/// reference each other, for the same reasons the two grammar copies are separate. The server only ever has
/// the text, so this is that file's text-only branch. <c>BundledServerRespectsTheIdesRegionsTests</c> in
/// <c>HexIDE.Tests</c> drives the built server against the IDE's copy over the corpus, which is what keeps
/// the two in step.
/// </para>
/// <para>
/// <b>Lines, not offsets</b>, because every answer here speaks LSP positions. Lines split on <c>\n</c> with a
/// trailing <c>\r</c> stripped, exactly as <see cref="VbTextHelpers.FindAllOccurrences"/> splits them, so a
/// line number means the same thing to both.
/// </para>
/// </remarks>
internal sealed class VbProtectedRegions
{
    private readonly bool[] protectedLines;
    private readonly HashSet<string> declaredNames;

    private VbProtectedRegions(bool[] protectedLines, int headerLineCount, HashSet<string> declaredNames)
    {
        this.protectedLines = protectedLines;
        this.declaredNames = declaredNames;
        HeaderLineCount = headerLineCount;
    }

    /// <summary>The number of lines the header occupies, from line 0. 0 when the text has no header.</summary>
    public int HeaderLineCount { get; }

    /// <summary>The protected lines of <paramref name="source"/>.</summary>
    public static VbProtectedRegions Of(string source)
    {
        var lines = LinesOf(source);
        var header = HeaderLinesFromText(lines);

        var marks = new bool[lines.Count];
        for (var i = 0; i < header; i++)
            marks[i] = true;

        var at = header;
        while (at < lines.Count)
        {
            if (!IsMemberAttribute(lines[at], out _))
            {
                at++;
                continue;
            }
            var first = at;
            var qualifiers = new List<string?>();
            while (at < lines.Count && IsMemberAttribute(lines[at], out var qualifier))
            {
                qualifiers.Add(qualifier);
                at++;
            }
            if (DescribesMember(lines, header, first, qualifiers))
            {
                for (var line = first; line < at; line++)
                    marks[line] = true;
            }
        }
        return new VbProtectedRegions(marks, header, DeclaredNames(lines, header));
    }

    /// <summary>
    /// True when the header's designer block declares <paramref name="word"/>: the name on a <c>Begin</c> line,
    /// which is the form's own, a control's or a menu's. Compared ignoring case, as VB6 compares names.
    /// </summary>
    /// <remarks>
    /// For rename. Such a name's declaration is in the header, so a rename that keeps off the header would
    /// leave the code referring to a control that no longer has that name. The IDE refuses the same rename
    /// by the same rule (<c>ReadOnlyRegions.DeclaredNames</c>).
    /// </remarks>
    public bool Declares(string word) => declaredNames.Contains(word);

    /// <summary>
    /// True when <paramref name="line"/> (0-based) is in the header or is an attribute line of a run that
    /// describes a member. An orphan's lines are not protected.
    /// </summary>
    public bool IsProtected(int line) => line >= 0 && line < protectedLines.Length && protectedLines[line];

    /// <summary>True when any line <paramref name="range"/> covers is protected.</summary>
    /// <remarks>
    /// A range ending at character 0 of a line stops at the end of the line before it, so it does not cover
    /// the line it ends on.
    /// </remarks>
    public bool Touches(LspRange range)
    {
        var last = range.End.Character == 0 && range.End.Line > range.Start.Line ? range.End.Line - 1 : range.End.Line;
        for (var line = range.Start.Line; line <= last; line++)
        {
            if (IsProtected(line))
                return true;
        }
        return false;
    }

    /// <summary>
    /// True when <paramref name="occurrence"/> of <paramref name="word"/> is the member name in
    /// <c>Attribute word.VB_…</c>: the one place inside a protected line that a rename changes.
    /// </summary>
    /// <remarks>
    /// When <c>Total</c> is renamed to <c>GrandTotal</c>, <c>Attribute Total.VB_Description</c> has to follow,
    /// or the description is left describing a member that no longer exists. The IDE lets exactly this edit
    /// through its own guard for the same reason (<c>CodeEditorViewModel.IsOwnAttributeQualifier</c>).
    /// </remarks>
    public static bool IsOwnQualifier(string lineText, LspRange occurrence, string word)
    {
        var start = occurrence.Start.Character;
        var end = occurrence.End.Character;
        if (occurrence.Start.Line != occurrence.End.Line || end - start != word.Length
            || end >= lineText.Length || lineText[end] != '.')
            return false;
        return lineText.AsSpan(0, start).Trim().Equals("Attribute", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <paramref name="source"/> with every protected line emptied and every line break kept, so each line of
    /// code keeps the line and column it had.
    /// </summary>
    /// <remarks>
    /// For the parse that stands in when the grammar cannot read a protected line
    /// (<c>VbDiagnosticsProvider.ParseSource</c>). <paramref name="source"/> must be the text these regions
    /// were computed from.
    /// </remarks>
    public string Blank(string source)
    {
        var result = new System.Text.StringBuilder(source.Length);
        var line = 0;
        var pos = 0;
        while (pos < source.Length)
        {
            var newline = source.IndexOf('\n', pos);
            var next = newline < 0 ? source.Length : newline + 1;
            var end = newline < 0 ? source.Length : newline;
            if (end > pos && source[end - 1] == '\r')
                end--;

            if (!IsProtected(line))
                result.Append(source, pos, end - pos);
            result.Append(source, end, next - end);

            pos = next;
            line++;
        }
        return result.ToString();
    }

    // ── The rule, ported from ReadOnlyRegions' text-only branch ──────────────────────────────────────────

    private static HashSet<string> DeclaredNames(List<string> lines, int headerLines)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headerLines; i++)
        {
            var line = lines[i].AsSpan().Trim();
            if (!StartsWithKeyword(line, "Begin"))
                continue;
            var rest = line["Begin".Length..].TrimStart();
            var afterType = rest.IndexOfAny(' ', '\t');
            if (afterType < 0)
                continue;
            var name = rest[afterType..].Trim();
            if (!name.IsEmpty)
                names.Add(name.ToString());
        }
        return names;
    }

    private static List<string> LinesOf(string source)
    {
        var lines = new List<string>();
        var pos = 0;
        while (pos < source.Length)
        {
            var newline = source.IndexOf('\n', pos);
            var end = newline < 0 ? source.Length : newline;
            var next = newline < 0 ? source.Length : newline + 1;
            if (end > pos && source[end - 1] == '\r')
                end--;
            lines.Add(source[pos..end]);
            pos = next;
        }
        return lines;
    }

    /// <summary>
    /// A <c>VERSION</c> line and the class or designer block after it, then the leading attribute run.
    /// </summary>
    /// <remarks>
    /// The designer block is walked by nesting, because a form's controls are nested <c>Begin</c>/<c>End</c>
    /// blocks and its fonts and images nested <c>BeginProperty</c>/<c>EndProperty</c> ones.
    /// </remarks>
    private static int HeaderLinesFromText(List<string> lines)
    {
        var i = 0;
        while (i < lines.Count && lines[i].AsSpan().Trim().IsEmpty)
            i++;

        var sawBlock = false;
        if (i < lines.Count && StartsWithKeyword(lines[i].AsSpan().Trim(), "VERSION"))
        {
            sawBlock = true;
            i++;
            if (i < lines.Count && lines[i].AsSpan().Trim().Equals("BEGIN", StringComparison.OrdinalIgnoreCase))
            {
                while (i < lines.Count && !lines[i].AsSpan().Trim().Equals("END", StringComparison.OrdinalIgnoreCase))
                    i++;
                if (i < lines.Count)
                    i++;
            }
            else
            {
                while (i < lines.Count && StartsWithKeyword(lines[i].AsSpan().Trim(), "Object"))
                    i++;
                if (i < lines.Count && StartsWithKeyword(lines[i].AsSpan().Trim(), "Begin"))
                {
                    var depth = 0;
                    do
                    {
                        var line = lines[i].AsSpan().Trim();
                        if (StartsWithKeyword(line, "Begin") || line.StartsWith("BeginProperty", StringComparison.OrdinalIgnoreCase))
                            depth++;
                        else if (line.Equals("End", StringComparison.OrdinalIgnoreCase) || line.Equals("EndProperty", StringComparison.OrdinalIgnoreCase))
                            depth--;
                        i++;
                    }
                    while (i < lines.Count && depth > 0);
                }
            }
        }

        var run = AttributeRunLength(lines, i);
        return sawBlock || run > 0 ? i + run : 0;
    }

    /// <summary>
    /// The number of lines from <paramref name="from"/> up to and including the last line of the
    /// <c>Attribute</c> run that starts there. Blank lines inside or in front of the run count; blank lines
    /// after it do not.
    /// </summary>
    private static int AttributeRunLength(List<string> lines, int from)
    {
        var lastAttribute = -1;
        for (var i = from; i < lines.Count; i++)
        {
            var line = lines[i].AsSpan().Trim();
            if (line.IsEmpty)
                continue;
            if (!StartsWithKeyword(line, "Attribute"))
                break;
            lastAttribute = i;
        }
        return lastAttribute < 0 ? 0 : lastAttribute + 1 - from;
    }

    /// <summary>
    /// True for <c>Attribute Name.VB_Something = …</c>, and false for anything that merely begins with the
    /// word — an assignment to a variable called <c>Attribute</c>, or an array of that name.
    /// </summary>
    /// <param name="qualifier">The attribute name up to its first <c>.</c>; null when it has none.</param>
    private static bool IsMemberAttribute(string text, out string? qualifier)
    {
        qualifier = null;
        var rest = text.AsSpan().Trim();
        if (!StartsWithKeyword(rest, "Attribute"))
            return false;
        rest = rest["Attribute".Length..].TrimStart();

        var name = 0;
        while (name < rest.Length && (char.IsLetterOrDigit(rest[name]) || rest[name] is '_' or '.'))
            name++;
        if (name == 0 || !char.IsLetter(rest[0]))
            return false;

        var dot = rest[..name].IndexOf('.');
        var after = rest[name..].TrimStart();
        if (after.Length == 0 || after[0] != '=')
            return false;
        qualifier = dot < 0 ? null : rest[..dot].ToString();
        return true;
    }

    /// <summary>
    /// True when the logical line directly above the run starting at <paramref name="first"/> declares every
    /// one of <paramref name="qualifiers"/>, compared without case. The IDE's <c>ReadOnlyRegions.Scan</c>.
    /// </summary>
    /// <remarks>
    /// The line above is read back over the lines continued into it with <c> _</c>, never into the header,
    /// and joined with the line breaks kept, which the declaration reader takes for whitespace.
    /// </remarks>
    private static bool DescribesMember(List<string> lines, int header, int first, List<string?> qualifiers)
    {
        if (first == 0)
            return false;
        var top = first - 1;
        while (top - 1 >= header && EndsWithContinuation(lines[top - 1]))
            top--;
        var logical = string.Join("\n", lines.GetRange(top, first - top));
        var declared = NamesDeclaredOn(logical);
        return qualifiers.TrueForAll(q => q is not null && declared.Contains(q));
    }

    /// <summary>
    /// True when <paramref name="line"/> continues into the next: its text ends with <c>_</c> after
    /// whitespace, trailing whitespace aside. The IDE's <c>ReadOnlyRegions.EndsWithContinuation</c>.
    /// </summary>
    private static bool EndsWithContinuation(string line)
    {
        var content = line.AsSpan().TrimEnd();
        return content.Length > 0 && content[^1] == '_' && (content.Length == 1 || char.IsWhiteSpace(content[^2]));
    }

    /// <summary>
    /// The names the declaration on <paramref name="text"/> writes, compared without case; empty when it is
    /// not a declaration. The IDE's <c>ReadOnlyRegions.NamesDeclaredOn</c>, which documents the forms read.
    /// </summary>
    private static HashSet<string> NamesDeclaredOn(string text)
    {
        var tokens = TokensOf(text);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var t = 0;
        var modified = false;
        while (IsWord(text, tokens, t, "Public", "Private", "Friend", "Global", "Static"))
        {
            t++;
            modified = true;
        }

        if (IsWord(text, tokens, t, "Declare"))
        {
            t++;
            if (IsWord(text, tokens, t, "PtrSafe"))
                t++;
            if (IsWord(text, tokens, t, "Sub", "Function"))
                AddName(t + 1);
            return names;
        }
        if (IsWord(text, tokens, t, "Sub", "Function", "Event"))
        {
            AddName(t + 1);
            return names;
        }
        if (IsWord(text, tokens, t, "Property"))
        {
            if (IsWord(text, tokens, t + 1, "Get", "Let", "Set"))
                AddName(t + 2);
            return names;
        }

        if (IsWord(text, tokens, t, "Dim"))
        {
            t++;
            modified = true;
        }
        if (!modified || IsWord(text, tokens, t, "Const", "Enum", "Type"))
            return names;

        while (t < tokens.Count)
        {
            if (IsWord(text, tokens, t, "WithEvents"))
                t++;
            if (t >= tokens.Count || tokens[t].Kind != TokenKind.Word)
                break;
            AddName(t);

            var depth = 0;
            t++;
            while (t < tokens.Count && !(depth == 0 && tokens[t].Kind == TokenKind.Comma))
            {
                depth += tokens[t].Kind switch { TokenKind.Open => 1, TokenKind.Close => -1, _ => 0 };
                t++;
            }
            t++;
        }
        return names;

        void AddName(int at)
        {
            if (at < tokens.Count && tokens[at].Kind == TokenKind.Word)
                names.Add(text.Substring(tokens[at].Start, tokens[at].Length));
        }
    }

    private enum TokenKind { Word, Open, Close, Comma, Other }

    private readonly record struct Token(TokenKind Kind, int Start, int Length);

    /// <summary>The tokens of the first statement on <paramref name="text"/>; the IDE's <c>ReadOnlyRegions.TokensOf</c>.</summary>
    private static List<Token> TokensOf(string text)
    {
        var tokens = new List<Token>();
        var end = text.Length;
        var i = 0;
        while (i < end)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }
            if (c == '_' && OnlyWhitespaceToLineEnd(text, i + 1, end))
            {
                i++;
                continue;
            }
            if (c is '\'' or ':')
                break;
            if (c == '"')
            {
                var opening = i++;
                while (i < end && text[i] != '\n')
                {
                    if (text[i] != '"')
                        i++;
                    else if (i + 1 < end && text[i + 1] == '"')
                        i += 2;
                    else
                    {
                        i++;
                        break;
                    }
                }
                tokens.Add(new Token(TokenKind.Other, opening, i - opening));
                continue;
            }
            if (char.IsLetter(c))
            {
                var j = i + 1;
                while (j < end && (char.IsLetterOrDigit(text[j]) || text[j] == '_'))
                    j++;
                if (tokens.Count == 0 && text.AsSpan(i, j - i).Equals("Rem", StringComparison.OrdinalIgnoreCase))
                    break;
                tokens.Add(new Token(TokenKind.Word, i, j - i));
                i = j < end && text[j] is '%' or '&' or '!' or '#' or '@' or '$' ? j + 1 : j;
                continue;
            }
            if (c == '[')
            {
                var close = text.IndexOf(']', i + 1, end - i - 1);
                if (close > i + 1)
                {
                    tokens.Add(new Token(TokenKind.Word, i + 1, close - i - 1));
                    i = close + 1;
                    continue;
                }
            }
            tokens.Add(new Token(c switch { '(' => TokenKind.Open, ')' => TokenKind.Close, ',' => TokenKind.Comma, _ => TokenKind.Other }, i, 1));
            i++;
        }
        return tokens;
    }

    private static bool OnlyWhitespaceToLineEnd(string text, int from, int end)
    {
        for (var i = from; i < end && text[i] != '\n'; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
                return false;
        }
        return true;
    }

    private static bool IsWord(string text, List<Token> tokens, int at, params string[] words)
    {
        if (at >= tokens.Count || tokens[at].Kind != TokenKind.Word)
            return false;
        var word = text.AsSpan(tokens[at].Start, tokens[at].Length);
        foreach (var w in words)
        {
            if (word.Equals(w, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool StartsWithKeyword(ReadOnlySpan<char> line, string keyword) =>
        line.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)
        && line.Length > keyword.Length
        && char.IsWhiteSpace(line[keyword.Length]);
}
