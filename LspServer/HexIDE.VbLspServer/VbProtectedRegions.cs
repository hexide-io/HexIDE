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
/// are a run of logical lines below the header whose first word is <c>Attribute</c> that describes a member
/// (task 4.1): procedure attributes under a procedure's declaration, whatever they name, or <c>VB_Var…</c>
/// lines under a module-level variable or constant's, naming what it declares or, on a line declaring one
/// name, one name nothing in the module declares. A run that does not — under a blank line, a comment or a
/// body statement, mixing the two kinds, or a variable attribute away from its variable — is an orphan, which
/// is ordinary text here as it is in the IDE.
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
/// <b>Lines, not offsets</b>, because every answer here speaks LSP positions. Lines split on CR, LF and
/// CRLF, exactly as <see cref="VbTextHelpers.FindAllOccurrences"/> splits them, so a line number means the
/// same thing to both.
/// </para>
/// </remarks>
internal sealed class VbProtectedRegions
{
    private readonly bool[] protectedLines;
    private readonly HashSet<string> declaredNames;
    private readonly Dictionary<(int Line, int Character), string> qualifierOwners;

    private VbProtectedRegions(bool[] protectedLines, int headerLineCount, HashSet<string> declaredNames,
        Dictionary<(int Line, int Character), string> qualifierOwners)
    {
        this.protectedLines = protectedLines;
        this.declaredNames = declaredNames;
        this.qualifierOwners = qualifierOwners;
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
        var owners = new Dictionary<(int Line, int Character), string>();
        foreach (var (first, last, qualifiers) in DescribingRuns(lines, header))
        {
            for (var line = first; line <= last; line++)
                marks[line] = true;
            foreach (var (at, owner) in qualifiers)
                owners[at] = owner;
        }
        return new VbProtectedRegions(marks, header, DeclaredNames(lines, header), owners);
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

    /// <summary>The names declared by the designer block, for the scope check's baseline.</summary>
    internal IEnumerable<string> DesignerNames => declaredNames;

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
    /// True when <paramref name="occurrence"/> of <paramref name="word"/> is the qualifier of an attribute line in
    /// a run that describes a member called <paramref name="word"/>: the one place inside a protected line that a
    /// rename of <paramref name="word"/> changes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When <c>Total</c> is renamed to <c>GrandTotal</c>, <c>Attribute Total.VB_Description</c> under it follows.
    /// The IDE lets exactly this edit through its own guard (<c>CodeEditorViewModel.RefusedRenameEdit</c>): a
    /// qualifier of a run whose member the same answer renames. A rename here renames every occurrence of the
    /// word, the member's declaration included, so "the answer renames the member" is "the member is called the
    /// word".
    /// </para>
    /// <para>
    /// <b>The member a line belongs to is the IDE's</b> (hexide-io/HexIDE#273 task 4.4): a procedure's run is the
    /// procedure's whatever its qualifier names, a variable run's line is the declarator it names, and a run a
    /// rename by retyping left is the one name its line declares. So <c>Attribute Total.VB_Description</c> under
    /// <c>Sub Halt()</c> is <c>Halt</c>'s, and renaming <c>Total</c> leaves it alone. The rule this replaced
    /// matched the qualifier's text and sent that edit, and the IDE now refuses a rename that writes into a run
    /// it does not rename. The IDE's next save rewrites that qualifier to <c>Halt</c> anyway.
    /// </para>
    /// <para>
    /// Read at the qualifier's own position, which may be on a physical line after the attribute line's first
    /// (<c>Attribute _</c> above <c>Wrapped.VB_HelpID = 3</c>, oracle probe p11b), where a reading of the line's
    /// text before the occurrence would find no <c>Attribute</c>.
    /// </para>
    /// </remarks>
    public bool IsOwnQualifier(LspRange occurrence, string word) =>
        occurrence.Start.Line == occurrence.End.Line
        && occurrence.End.Character - occurrence.Start.Character == word.Length
        && qualifierOwners.TryGetValue((occurrence.Start.Line, occurrence.Start.Character), out var owner)
        && string.Equals(owner, word, StringComparison.OrdinalIgnoreCase);

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
            var (end, next) = VbTextHelpers.LineEnd(source, pos);

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
            var (end, next) = VbTextHelpers.LineEnd(source, pos);
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

    private enum AttributeLineKind { Dotless, Procedure, Variable }

    private enum DeclarationKind { Procedure, Variable, Constant }

    private readonly record struct Declared(string Name, DeclarationKind Kind);

    /// <param name="QualifierAt">Where <paramref name="Qualifier"/> starts in the logical line's joined text; -1 for none.</param>
    private readonly record struct AttributeLine(AttributeLineKind Kind, string? Qualifier, int QualifierAt);

    /// <summary>
    /// The first and last physical line of every run below the header that describes a member, and where each of
    /// its lines' qualifiers is with the member it belongs to. The IDE's <c>ReadOnlyRegions.Scan</c>, which
    /// documents the rule, and its <c>MemberRun.Member</c>.
    /// </summary>
    private static IEnumerable<(int First, int Last, List<((int Line, int Character) At, string Owner)> Qualifiers)> DescribingRuns(
        List<string> lines, int header)
    {
        var logical = LogicalLinesOf(lines, header);
        int? firstBody = null;
        HashSet<string>? moduleNames = null;

        var k = 0;
        while (k < logical.Count)
        {
            if (!IsAttributeLine(lines[logical[k].First]))
            {
                k++;
                continue;
            }
            var first = k;
            var attributes = new List<AttributeLine>();
            for (; k < logical.Count && IsAttributeLine(lines[logical[k].First]); k++)
                attributes.Add(ReadAttributeLine(Text(logical[k])));

            // The first line of the code has no line of its own above it, and so describes nothing.
            if (first == 0)
                continue;
            var declared = ReadDeclarations(Text(logical[first - 1]), out _);
            if (!Describes(attributes, declared, () => first - 1 < FirstBody(), name => ModuleNames().Contains(name)))
                continue;

            var qualifiers = new List<((int Line, int Character) At, string Owner)>();
            for (var a = 0; a < attributes.Count; a++)
            {
                if (attributes[a].Qualifier is { } qualifier)
                    qualifiers.Add((PositionIn(logical[first + a], attributes[a].QualifierAt), Owner(attributes, declared, qualifier)));
            }
            yield return (logical[first].First, logical[k - 1].Last, qualifiers);
        }

        // Where an offset into a logical line's joined text falls: the physical line, and the character on it.
        (int Line, int Character) PositionIn((int First, int Last) line, int offset)
        {
            var physical = line.First;
            var start = 0;
            var text = Text(line);
            for (var i = 0; i < offset; i++)
            {
                if (text[i] == '\n')
                {
                    physical++;
                    start = i + 1;
                }
            }
            return (physical, offset - start);
        }

        // A logical line's physical lines, joined with the line breaks kept, which the declaration reader takes
        // for whitespace.
        string Text((int First, int Last) line) => string.Join("\n", lines.GetRange(line.First, line.Last - line.First + 1));

        int FirstBody()
        {
            if (firstBody is null)
            {
                firstBody = logical.Count;
                for (var i = 0; i < logical.Count; i++)
                {
                    ReadDeclarations(Text(logical[i]), out var opensBody);
                    if (opensBody)
                    {
                        firstBody = i;
                        break;
                    }
                }
            }
            return firstBody.Value;
        }

        HashSet<string> ModuleNames()
        {
            if (moduleNames is null)
            {
                moduleNames = DeclaredNames(lines, header);
                foreach (var line in logical)
                {
                    foreach (var d in ReadDeclarations(Text(line), out _))
                        moduleNames.Add(d.Name);
                }
            }
            return moduleNames;
        }
    }

    /// <summary>Whether a run of <paramref name="attributes"/> under a line declaring <paramref name="declared"/> describes a member. The IDE's <c>ReadOnlyRegions.Verdict</c>.</summary>
    private static bool Describes(List<AttributeLine> attributes, List<Declared> declared, Func<bool> moduleLevel,
        Func<string, bool> declaredInModule)
    {
        if (attributes.TrueForAll(a => a.Kind == AttributeLineKind.Procedure))
            return declared.Exists(d => d.Kind == DeclarationKind.Procedure);

        if (!attributes.TrueForAll(a => a.Kind == AttributeLineKind.Variable) || !moduleLevel())
            return false;
        if (attributes.TrueForAll(a => declared.Exists(d => IsVariableNamed(d, a.Qualifier!))))
            return true;

        var stale = attributes[0].Qualifier!;
        return declared.Count == 1 && declared[0].Kind != DeclarationKind.Procedure
            && attributes.TrueForAll(a => string.Equals(a.Qualifier, stale, StringComparison.OrdinalIgnoreCase))
            && !declaredInModule(stale);
    }

    /// <summary>
    /// The member a line of a run that <see cref="Describes"/> its line belongs to, named as declared: the procedure
    /// for a procedure's run, whatever <paramref name="qualifier"/> says; the declarator <paramref name="qualifier"/>
    /// names for a variable run; and the line's one name for the run a rename by retyping left. The IDE's
    /// <c>MemberRun.Member</c>, and for a variable run the declarator <c>CodeEditorViewModel.RefusedRenameEdit</c>
    /// reads off the line.
    /// </summary>
    private static string Owner(List<AttributeLine> attributes, List<Declared> declared, string qualifier)
    {
        if (attributes.TrueForAll(a => a.Kind == AttributeLineKind.Procedure))
            return declared.Find(d => d.Kind == DeclarationKind.Procedure).Name;
        if (attributes.TrueForAll(a => declared.Exists(d => IsVariableNamed(d, a.Qualifier!))))
            return declared.Find(d => IsVariableNamed(d, qualifier)).Name;
        return declared[0].Name;
    }

    private static bool IsVariableNamed(Declared declared, string name) =>
        declared.Kind != DeclarationKind.Procedure && string.Equals(declared.Name, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>The logical lines from <paramref name="from"/> on. The IDE's <c>ReadOnlyRegions.LogicalLinesOf</c>.</summary>
    private static List<(int First, int Last)> LogicalLinesOf(List<string> lines, int from)
    {
        var logical = new List<(int, int)>();
        var i = from;
        while (i < lines.Count)
        {
            var last = i;
            while (last + 1 < lines.Count && EndsWithContinuation(lines[last]))
                last++;
            logical.Add((i, last));
            i = last + 1;
        }
        return logical;
    }

    /// <summary>
    /// True when <paramref name="line"/> continues into the next: its text ends with an underscore after
    /// whitespace, trailing whitespace aside, so a line of nothing but an underscore continues only when it is
    /// indented. The IDE's <c>ReadOnlyRegions.EndsWithContinuation</c>.
    /// </summary>
    private static bool EndsWithContinuation(string line)
    {
        var content = line.AsSpan().TrimEnd();
        return content.Length > 1 && content[^1] == '_' && char.IsWhiteSpace(content[^2]);
    }

    /// <summary>
    /// True when a logical line starting on <paramref name="line"/> is an attribute line: its first word is
    /// <c>Attribute</c>. The IDE's <c>ReadOnlyRegions.IsAttributeLine</c>.
    /// </summary>
    private static bool IsAttributeLine(string line)
    {
        var content = line.AsSpan().TrimStart();
        return content.StartsWith("Attribute", StringComparison.OrdinalIgnoreCase)
               && (content.Length == "Attribute".Length || !IsNameCharacter(content["Attribute".Length]));
    }

    /// <summary>The kind and qualifier of the attribute line <paramref name="text"/>. The IDE's <c>ReadOnlyRegions.ReadAttributeLine</c>.</summary>
    private static AttributeLine ReadAttributeLine(string text)
    {
        var end = text.Length;
        var i = 0;
        while (char.IsWhiteSpace(text[i]))
            i++;
        i += "Attribute".Length;
        while (i < end && (char.IsWhiteSpace(text[i]) || (text[i] == '_' && OnlyWhitespaceToLineEnd(text, i + 1, end))))
            i++;

        var name = i;
        if (i < end && char.IsLetter(text[i]))
        {
            while (i < end && (IsNameCharacter(text[i]) || text[i] == '.'))
                i++;
        }
        var dot = text.IndexOf('.', name, i - name);
        if (dot < 0)
            return new AttributeLine(AttributeLineKind.Dotless, null, -1);

        var kind = text.AsSpan(dot + 1, i - dot - 1).StartsWith("VB_Var", StringComparison.OrdinalIgnoreCase)
            ? AttributeLineKind.Variable
            : AttributeLineKind.Procedure;
        return new AttributeLine(kind, text[name..dot], name);
    }

    /// <summary>
    /// The names the declarations on <paramref name="text"/> write, every statement read. The IDE's
    /// <c>ReadOnlyRegions.NamesDeclaredOn</c>, which documents the forms read.
    /// </summary>
    /// <param name="opensBody">True when a statement on it opens a procedure's body.</param>
    private static List<Declared> ReadDeclarations(string text, out bool opensBody)
    {
        var tokens = TokensOf(text);
        var names = new List<Declared>();
        opensBody = false;
        var from = 0;
        while (from < tokens.Count)
        {
            var to = from;
            while (to < tokens.Count && tokens[to].Kind != TokenKind.Colon)
                to++;
            opensBody |= ReadStatement(text, tokens, from, to, names);
            from = to + 1;
        }
        return names;
    }

    /// <summary>One statement's declarations. The IDE's <c>ReadOnlyRegions.ReadStatement</c>.</summary>
    private static bool ReadStatement(string text, List<Token> tokens, int t, int end, List<Declared> names)
    {
        var modified = false;
        while (IsWord(text, tokens, t, end, "Public", "Private", "Friend", "Global", "Static"))
        {
            t++;
            modified = true;
        }

        if (IsWord(text, tokens, t, end, "Declare"))
        {
            t++;
            if (IsWord(text, tokens, t, end, "PtrSafe"))
                t++;
            if (IsWord(text, tokens, t, end, "Sub", "Function"))
                AddName(t + 1, DeclarationKind.Procedure);
            return false;
        }
        if (IsWord(text, tokens, t, end, "Event"))
        {
            AddName(t + 1, DeclarationKind.Procedure);
            return false;
        }
        if (IsWord(text, tokens, t, end, "Sub", "Function"))
        {
            AddName(t + 1, DeclarationKind.Procedure);
            return true;
        }
        if (IsWord(text, tokens, t, end, "Property"))
        {
            if (!IsWord(text, tokens, t + 1, end, "Get", "Let", "Set"))
                return false;
            AddName(t + 2, DeclarationKind.Procedure);
            return true;
        }
        if (IsWord(text, tokens, t, end, "Const"))
        {
            AddDeclarators(t + 1, DeclarationKind.Constant);
            return false;
        }

        if (IsWord(text, tokens, t, end, "Dim"))
        {
            t++;
            modified = true;
        }
        if (!modified || IsWord(text, tokens, t, end, "Enum", "Type"))
            return false;
        AddDeclarators(t, DeclarationKind.Variable);
        return false;

        void AddDeclarators(int at, DeclarationKind kind)
        {
            while (at < end)
            {
                if (IsWord(text, tokens, at, end, "WithEvents"))
                    at++;
                if (at >= end || tokens[at].Kind != TokenKind.Word)
                    break;
                AddName(at, kind);

                var depth = 0;
                at++;
                while (at < end && !(depth == 0 && tokens[at].Kind == TokenKind.Comma))
                {
                    depth += tokens[at].Kind switch { TokenKind.Open => 1, TokenKind.Close => -1, _ => 0 };
                    at++;
                }
                at++;
            }
        }

        void AddName(int at, DeclarationKind kind)
        {
            if (at < end && tokens[at].Kind == TokenKind.Word)
                names.Add(new Declared(text.Substring(tokens[at].Start, tokens[at].Length), kind));
        }
    }

    private enum TokenKind { Word, Open, Close, Comma, Colon, Other }

    private readonly record struct Token(TokenKind Kind, int Start, int Length);

    /// <summary>The tokens on <paramref name="text"/>, up to a comment; the IDE's <c>ReadOnlyRegions.TokensOf</c>.</summary>
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
            if (c == '\'')
                break;
            if (c == '"')
            {
                var opening = i++;
                while (i < end && text[i] != '\n')
                {
                    if (text[i] != '"')
                        i++;
                    // Inside the string, as VB6 lexes it; no verdict turns on it (the IDE's copy says why).
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
                while (j < end && IsNameCharacter(text[j]))
                    j++;
                // Rem where a statement starts; the position test is defensive (the IDE's copy says why).
                if ((tokens.Count == 0 || tokens[^1].Kind == TokenKind.Colon)
                    && text.AsSpan(i, j - i).Equals("Rem", StringComparison.OrdinalIgnoreCase))
                    break;
                tokens.Add(new Token(TokenKind.Word, i, j - i));
                i = j;
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
            tokens.Add(new Token(c switch
            {
                '(' => TokenKind.Open,
                ')' => TokenKind.Close,
                ',' => TokenKind.Comma,
                ':' => TokenKind.Colon,
                _ => TokenKind.Other,
            }, i, 1));
            i++;
        }
        return tokens;
    }

    private static bool IsNameCharacter(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool OnlyWhitespaceToLineEnd(string text, int from, int end)
    {
        for (var i = from; i < end && text[i] != '\n'; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
                return false;
        }
        return true;
    }

    private static bool IsWord(string text, List<Token> tokens, int at, int end, params string[] words)
    {
        if (at >= end || tokens[at].Kind != TokenKind.Word)
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
