using System;
using System.Collections.Generic;

namespace HexIDE.Runtime.Serialization;

/// <summary>
/// A span of a code window's buffer that only the IDE may change: <c>[Start, End)</c>, whole lines, with
/// <see cref="End"/> just past the last line's terminator.
/// </summary>
/// <param name="Start">The offset of the region's first character, which is always the start of a line.</param>
/// <param name="End">The offset just past the region's last line, terminator included.</param>
/// <param name="OpenEnded">
/// True when the region's last line is the last line of the buffer and has no terminator. Text inserted at
/// <see cref="End"/> would then join that line rather than start a new one, so it counts as inside.
/// </param>
/// <param name="Anchor">
/// For a member's attribute run, where the line it describes ends: the start of that line's terminator.
/// Null for the header, which describes nothing above it. See <see cref="Changes"/>.
/// </param>
public readonly record struct TextRegion(int Start, int End, bool OpenEnded = false, int? Anchor = null)
{
    /// <summary>
    /// True when <paramref name="length"/> characters at <paramref name="offset"/> lie in this region, or an
    /// insertion there would land in it.
    /// </summary>
    /// <remarks>
    /// An insertion (<paramref name="length"/> 0) at the region's first character counts as inside, because
    /// it pushes the region away from the line above it: for the header that is text above the top of the
    /// file, and for a member's attribute run it separates the run from the declaration it describes. An
    /// insertion at <see cref="End"/> is the start of the next line and does not count, unless the region is
    /// <see cref="OpenEnded"/>.
    /// <para>
    /// This is membership: whether a line or a span is <em>in</em> the region. Whether an <em>edit</em> may
    /// be made is <see cref="Changes"/>, which is stricter.
    /// </para>
    /// </remarks>
    public bool Touches(int offset, int length) =>
        length > 0
            ? offset < End && offset + length > Start
            : offset >= Start && (offset < End || (OpenEnded && offset == End));

    /// <summary>Where an edit starts to change this region: <see cref="Anchor"/>, or <see cref="Start"/>.</summary>
    public int EditStart => Anchor ?? Start;

    /// <summary>
    /// True when replacing <paramref name="length"/> characters at <paramref name="offset"/> would change
    /// this region: <see cref="Touches"/>, and also any replacement that removes the line break between a
    /// member's attribute run and the line it describes.
    /// </summary>
    /// <remarks>
    /// That line break is outside the run, because the run is whole lines and the break ends the line above.
    /// But removing it joins the first attribute line onto the declaration, and the run is then no run at all:
    /// the line no longer starts with <c>Attribute</c>, and the file no longer compiles. Typing is refused it by
    /// the code window's section provider (#273 task 3.7), which starts a member's protected span at the same
    /// <see cref="EditStart"/>. Every other writer asks this (task 3.11, found by review: a Replace of
    /// <c>Currency\r\n</c> with <c>Currency</c> made exactly that join).
    /// <para>
    /// An insertion is unchanged: text typed at the end of a declaration lands before its line break and
    /// leaves the run where it is.
    /// </para>
    /// </remarks>
    public bool Changes(int offset, int length) =>
        length > 0
            ? offset < End && offset + length > EditStart
            : Touches(offset, 0);
}

/// <summary>
/// A run of <c>Attribute</c> lines below a file's header, and the line above it that it may describe
/// (hexide-io/HexIDE#273 task 4.1). See <see cref="ReadOnlyRegions.MemberRuns"/>.
/// </summary>
/// <param name="Region">
/// The run's whole lines. Its <see cref="TextRegion.Anchor"/> is <paramref name="Anchor"/>, so for a run that
/// describes a member this is exactly the region <see cref="ReadOnlyRegions.Of"/> returns.
/// </param>
/// <param name="Qualifiers">
/// Each line's qualifier, in order: its attribute name up to the first <c>.</c>, as written, so
/// <c>Total.VB_ProcData.VB_Invoke_Property</c> gives <c>Total</c>. Null for a line with no <c>.</c>, which
/// names no member.
/// </param>
/// <param name="DescribedStart">
/// Where the described line starts: the first physical line of the logical line directly above the run,
/// reading back over the lines that continue into it with <c> _</c>.
/// </param>
/// <param name="Anchor">
/// Where the described line ends: the end of the text of the physical line directly above the run, before its
/// terminator. A fold of the run starts here.
/// </param>
/// <param name="LastContentEnd">The end of the text of the run's last line, before its terminator.</param>
/// <param name="Declared">
/// The names the described line declares, read lexically (<see cref="ReadOnlyRegions.NamesDeclaredOn(string, int, int)"/>),
/// each with where it is written. Empty when that line declares nothing.
/// </param>
/// <param name="DescribesMember">
/// True when the described line declares the qualifier of every line in the run, compared without case. A
/// run that does not is an <em>orphan</em>: inert text, not a read-only region.
/// </param>
/// <param name="Key">
/// Which run this is, in a form that outlives its offsets; null for an orphan. See <see cref="MemberRunKey"/>.
/// </param>
public sealed record MemberRun(
    TextRegion Region,
    IReadOnlyList<string?> Qualifiers,
    int DescribedStart,
    int Anchor,
    int LastContentEnd,
    IReadOnlyList<DeclaredName> Declared,
    bool DescribesMember,
    MemberRunKey? Key);

/// <summary>
/// A run that describes a member, named so that two texts can be compared and state can be kept across an
/// edit: its first qualifier, upper-cased, and its ordinal among the runs that describe a member with that
/// qualifier, counted from the top of the file.
/// </summary>
/// <remarks>
/// The name alone is not enough, because a property's <c>Get</c> and <c>Let</c> share one and each may carry a
/// run. Orphans are not counted, so repairing or removing one, which is free text, does not renumber the runs
/// of a member that still has its declaration.
/// </remarks>
public readonly record struct MemberRunKey(string Member, int Ordinal);

/// <summary>
/// A name a declaration writes, and where: the name occupies <c>Name.Length</c> characters from
/// <see cref="Offset"/>, a type-declaration character or brackets around it aside.
/// </summary>
public readonly record struct DeclaredName(string Name, int Offset);

/// <summary>
/// Where the read-only regions of a code window's buffer are: the file's header, and each run of attribute
/// lines describing a member (hexide-io/HexIDE#273 phase 3).
/// </summary>
/// <remarks>
/// <para>
/// <b>A read-only region is the header or a member's attribute run, and nothing else.</b> A form the IDE
/// cannot reproduce is held read-only <em>as a whole</em>, and that is a different gate. It must not make the
/// form's code lines part of a region, or a developer could not set a breakpoint anywhere in it and Find
/// would match nothing. So nothing here asks whether a document is faithful.
/// </para>
/// <para>
/// <b>The header is one rule for every kind: the top of the file through the last line of the leading
/// <c>Attribute</c> run.</b> It is not the buffer's prefix, and conflating the two corrupts a file. For a
/// <c>.bas</c> or <c>.cls</c> the prefix already ends after the attribute run. For a <c>.frm</c>,
/// <c>.ctl</c> or <c>.pag</c> it ends at the designer block's closing <c>End</c>, and the attribute run is
/// the first lines of the code, so the region straddles the split.
/// </para>
/// <para>
/// <b>Where the model split nothing off, the header is found from the text.</b> An unparseable
/// <c>.ctl</c>/<c>.pag</c>, or a <c>.bas</c>/<c>.cls</c> whose first line is blank (hexide-io/HexIDE#472),
/// keeps its whole file in the code. Its prefix is then empty, but the buffer still opens with a header.
/// </para>
/// <para>
/// <b>A run of attribute lines is a region only when it describes a member</b> (task 4.1): when the logical
/// line directly above it declares the qualifier of every line in it. Anything else is an orphan — a run under
/// a blank line, a comment, a body statement or another member, or a line with no <c>.</c> below the code — and
/// an orphan is inert text the developer can see and change, which is the only way to repair one.
/// <see cref="MemberRuns"/> reports both kinds, with the verdict.
/// </para>
/// </remarks>
public static class ReadOnlyRegions
{
    /// <summary>
    /// The read-only regions of <paramref name="text"/>, in order: the header first when there is one, then
    /// every attribute run that describes a member. An orphaned run is not one.
    /// </summary>
    /// <param name="text">The whole buffer.</param>
    /// <param name="prefixLength">
    /// The length of the header the buffer was composed with, or 0 when the model split nothing off.
    /// </param>
    public static IReadOnlyList<TextRegion> Of(string text, int prefixLength)
    {
        var (lines, headerLines, runs) = Scan(text, prefixLength);
        var regions = new List<TextRegion>();

        if (headerLines > 0)
            regions.Add(RegionOf(lines, 0, headerLines, text.Length));
        foreach (var run in runs)
        {
            if (run.DescribesMember)
                regions.Add(run.Region);
        }
        return regions;
    }

    /// <summary>
    /// Every run of <c>Attribute</c> lines below the header of <paramref name="text"/>, in order, those that
    /// describe a member and the orphans alike, each with the line above it that it describes or fails to.
    /// </summary>
    /// <param name="text">The whole buffer, or any whole file: give 0 for <paramref name="prefixLength"/>.</param>
    /// <param name="prefixLength">
    /// The length of the header the buffer was composed with, or 0 when the model split nothing off.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>A run</b> is a maximal block of consecutive <c>Attribute &lt;name&gt; = …</c> lines below the header,
    /// as <see cref="Of"/> has always found them. A blank line ends one, so a run split by a blank line is two
    /// runs, and the second hangs from the blank line.
    /// </para>
    /// <para>
    /// <b>Whether it describes a member is read lexically and line-locally</b>: the described line's
    /// declaration is read for the names it writes (<see cref="NamesDeclaredOn(string, int, int)"/>), and each
    /// qualifier is compared with them without case. That is collection, not binding — nothing else in the
    /// module is consulted, so a run whose qualifier names a member declared elsewhere is an orphan here.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<MemberRun> MemberRuns(string text, int prefixLength) => Scan(text, prefixLength).Runs;

    /// <summary>
    /// The names the declaration on <c>[start, end)</c> of <paramref name="text"/> writes, each with where it
    /// is written; empty when that text is not a declaration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Lexical and line-local, and biased towards finding a declaration</b>, because a run wrongly judged an
    /// orphan loses its protection, while one wrongly judged to describe a member costs nothing but a fold. It
    /// reads, without case:
    /// </para>
    /// <list type="bullet">
    /// <item><c>[Public|Private|Friend|Global|Static]… Sub|Function|Event &lt;name&gt;</c>, and
    /// <c>Property Get|Let|Set &lt;name&gt;</c> after the same modifiers;</item>
    /// <item><c>[Public|Private] Declare [PtrSafe] Sub|Function &lt;name&gt;</c>, <c>PtrSafe</c> being the same
    /// declaration in VBA7;</item>
    /// <item>variable declarators after at least one of <c>Public|Private|Friend|Global|Static|Dim</c>:
    /// <c>[WithEvents] &lt;name&gt;[(…)] [As …]</c>, as a comma list, each with its own <c>WithEvents</c>.</item>
    /// </list>
    /// <para>
    /// A <c> _</c> line continuation and the line break after it are whitespace, so <c>[start, end)</c> may be
    /// a whole continued logical line. A comment ends the read, and so does a <c>:</c>, so only the first
    /// statement on a line is read. A name may carry a type-declaration character (<c>Total$</c> declares
    /// <c>Total</c>) or be bracketed. <c>Const</c>, <c>Enum</c> and <c>Type</c> declare nothing a member run can
    /// describe.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<DeclaredName> NamesDeclaredOn(string text, int start, int end)
    {
        var tokens = TokensOf(text, start, end);
        var names = new List<DeclaredName>();

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
        // Without a declaring keyword the line is a statement: an assignment, a call, a ReDim.
        if (!modified || IsWord(text, tokens, t, "Const", "Enum", "Type"))
            return names;

        while (t < tokens.Count)
        {
            if (IsWord(text, tokens, t, "WithEvents"))
                t++;
            if (t >= tokens.Count || tokens[t].Kind != TokenKind.Word)
                break;
            AddName(t);

            // Past the rest of this declarator -- its bounds and its As clause -- to the comma that starts the
            // next, which a comma inside the bounds does not.
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
                names.Add(new DeclaredName(text.Substring(tokens[at].Start, tokens[at].Length), tokens[at].Start));
        }
    }

    /// <summary>The names <paramref name="line"/> declares; see <see cref="NamesDeclaredOn(string, int, int)"/>.</summary>
    public static IReadOnlyList<DeclaredName> NamesDeclaredOn(string line) => NamesDeclaredOn(line, 0, line.Length);

    /// <summary>
    /// The offset just past the header, which is where the developer's code starts. 0 when there is no header.
    /// </summary>
    public static int HeaderEnd(string text, int prefixLength)
    {
        var lines = LinesOf(text);
        var headerLines = HeaderLineCount(text, lines, prefixLength);
        return headerLines == 0 ? 0 : lines[headerLines - 1].Next;
    }

    /// <summary>The number of lines the header occupies: 0 when there is none.</summary>
    public static int HeaderLineCount(string text, int prefixLength) =>
        HeaderLineCount(text, LinesOf(text), prefixLength);

    /// <summary>
    /// True when the 1-based <paramref name="line"/> of <paramref name="text"/> is a line of one of
    /// <paramref name="regions"/>. False for a line the text does not have.
    /// </summary>
    /// <remarks>
    /// Membership, and deliberately not <see cref="Changes"/>: that one also answers for the line break a
    /// member's attribute run hangs from, which ends the declaration it describes. Asked about a line, it would
    /// put every described declaration out of reach of a breakpoint (#273 task 3.12). The line is asked about
    /// without its terminator for the same reason.
    /// </remarks>
    public static bool IsReadOnlyLine(string text, IReadOnlyList<TextRegion> regions, int line)
    {
        var lines = LinesOf(text);
        // LinesOf yields no entry for the empty line after a final terminator, which is still a line of the
        // buffer: the last line of a file that ends with a line break. It follows every region, so it is in none.
        if (line < 1 || line > lines.Count)
            return false;
        var at = lines[line - 1];
        return Touches(regions, at.Start, at.ContentEnd - at.Start);
    }

    /// <summary>
    /// The names the header's designer block declares: the name on each <c>Begin</c> line, which is the form's
    /// own, a control's or a menu's. Compared ignoring case, as VB6 compares names. Empty when there is none.
    /// </summary>
    /// <remarks>
    /// For rename (hexide-io/HexIDE#273 task 3.10). Such a name's declaration is in the header, so a rename
    /// that keeps off the header would leave the code naming a control that no longer has that name; the code
    /// window refuses it, and the bundled server declines it by the same rule (<c>VbProtectedRegions</c>).
    /// </remarks>
    public static IReadOnlySet<string> DeclaredNames(string text, int prefixLength)
    {
        var lines = LinesOf(text);
        var headerLines = HeaderLineCount(text, lines, prefixLength);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headerLines; i++)
        {
            var line = Trimmed(text, lines[i]);
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

    /// <summary>
    /// True when replacing <paramref name="length"/> characters at <paramref name="offset"/> would change any
    /// of <paramref name="regions"/>.
    /// </summary>
    public static bool Touches(IReadOnlyList<TextRegion> regions, int offset, int length)
    {
        foreach (var region in regions)
        {
            if (region.Touches(offset, length))
                return true;
        }
        return false;
    }

    /// <summary>
    /// True when replacing <paramref name="length"/> characters at <paramref name="offset"/> would change any
    /// of <paramref name="regions"/>, joining a member's attribute run onto its declaration included. The
    /// question every writer asks; see <see cref="TextRegion.Changes"/>.
    /// </summary>
    public static bool Changes(IReadOnlyList<TextRegion> regions, int offset, int length)
    {
        foreach (var region in regions)
        {
            if (region.Changes(offset, length))
                return true;
        }
        return false;
    }

    /// <summary>One line of the buffer: where its text starts and ends, and where the next line starts.</summary>
    private readonly record struct Line(int Start, int ContentEnd, int Next);

    /// <summary>
    /// The lines of <paramref name="text"/>, how many of them the header takes, and every attribute run below
    /// it with its verdict: the one scan <see cref="Of"/> and <see cref="MemberRuns"/> both read.
    /// </summary>
    private static (List<Line> Lines, int HeaderLines, List<MemberRun> Runs) Scan(string text, int prefixLength)
    {
        var lines = LinesOf(text);
        var headerLines = HeaderLineCount(text, lines, prefixLength);
        var runs = new List<MemberRun>();
        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);

        var i = headerLines;
        while (i < lines.Count)
        {
            if (!IsMemberAttribute(text, lines[i], out _))
            {
                i++;
                continue;
            }
            var first = i;
            var qualifiers = new List<string?>();
            while (i < lines.Count && IsMemberAttribute(text, lines[i], out var qualifier))
            {
                qualifiers.Add(qualifier);
                i++;
            }

            // The run hangs from the line above it, which is the member it describes if it describes one. A run
            // on the first line would be the header's, so there always is one; the guard is for a text no rule
            // here produces. The line above is read back over the lines continued into it, and never into the
            // header.
            var region = RegionOf(lines, first, i, text.Length);
            var describedStart = region.Start;
            var anchor = region.Start;
            IReadOnlyList<DeclaredName> declared = [];
            if (first > 0)
            {
                var top = first - 1;
                while (top - 1 >= headerLines && EndsWithContinuation(text, lines[top - 1]))
                    top--;
                describedStart = lines[top].Start;
                anchor = lines[first - 1].ContentEnd;
                declared = NamesDeclaredOn(text, describedStart, anchor);
                region = region with { Anchor = anchor };
            }

            var describes = first > 0 && qualifiers.TrueForAll(q => q is not null && Declares(declared, q));
            MemberRunKey? key = null;
            if (describes)
            {
                var member = qualifiers[0]!.ToUpperInvariant();
                ordinals.TryGetValue(member, out var ordinal);
                ordinals[member] = ordinal + 1;
                key = new MemberRunKey(member, ordinal);
            }
            runs.Add(new MemberRun(region, qualifiers, describedStart, anchor, lines[i - 1].ContentEnd, declared,
                describes, key));
        }
        return (lines, headerLines, runs);
    }

    private static bool Declares(IReadOnlyList<DeclaredName> declared, string name)
    {
        foreach (var d in declared)
        {
            if (string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// True when <paramref name="line"/> continues into the next: its text ends with <c>_</c> after
    /// whitespace, trailing whitespace aside.
    /// </summary>
    /// <remarks>
    /// The rule VB6 states, a space and an underscore, and deliberately not
    /// <c>VbAutoCloseProvider.IsContinuationLine</c>'s looser one, which also takes an identifier that ends in
    /// <c>_</c> for a continuation.
    /// </remarks>
    private static bool EndsWithContinuation(string text, Line line)
    {
        var content = text.AsSpan(line.Start, line.ContentEnd - line.Start).TrimEnd();
        return content.Length > 0 && content[^1] == '_' && (content.Length == 1 || char.IsWhiteSpace(content[^2]));
    }

    private enum TokenKind { Word, Open, Close, Comma, Other }

    /// <summary>
    /// A token of a declaration: a name or keyword (<see cref="TokenKind.Word"/>, a bracketed name without its
    /// brackets and a name without its type character), a parenthesis, a comma, or anything else.
    /// </summary>
    private readonly record struct Token(TokenKind Kind, int Start, int Length);

    /// <summary>
    /// The tokens of the first statement on <c>[start, end)</c>: up to a comment, a <c>:</c>, or the end.
    /// </summary>
    private static List<Token> TokensOf(string text, int start, int end)
    {
        var tokens = new List<Token>();
        var i = start;
        while (i < end)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                // A line break here is one a continuation left, and is whitespace like the rest.
                i++;
                continue;
            }
            if (c == '_' && OnlyWhitespaceToLineEnd(text, i + 1, end))
            {
                // A continuation. No name starts with an underscore, so one at the start of a token is this.
                i++;
                continue;
            }
            if (c is '\'' or ':')
                break;
            if (c == '"')
            {
                // A string, "" inside it being a quote: a ' or : in one is neither a comment nor a separator.
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

    private static List<Line> LinesOf(string text)
    {
        var lines = new List<Line>();
        var pos = 0;
        while (pos < text.Length)
        {
            var newline = text.IndexOf('\n', pos);
            var next = newline < 0 ? text.Length : newline + 1;
            var end = newline < 0 ? text.Length : newline;
            if (end > pos && text[end - 1] == '\r')
                end--;
            lines.Add(new Line(pos, end, next));
            pos = next;
        }
        return lines;
    }

    private static ReadOnlySpan<char> Trimmed(string text, Line line) =>
        text.AsSpan(line.Start, line.ContentEnd - line.Start).Trim();

    private static TextRegion RegionOf(List<Line> lines, int firstLine, int endLine, int textLength)
    {
        var last = lines[endLine - 1];
        return new TextRegion(lines[firstLine].Start, last.Next, OpenEnded: last.ContentEnd == textLength);
    }

    private static int HeaderLineCount(string text, List<Line> lines, int prefixLength)
    {
        if (prefixLength > 0 && prefixLength <= text.Length)
        {
            var prefixLines = 0;
            while (prefixLines < lines.Count && lines[prefixLines].Next <= prefixLength)
                prefixLines++;
            return prefixLines + AttributeRunLength(text, lines, prefixLines);
        }
        return HeaderLinesFromText(text, lines);
    }

    /// <summary>
    /// The header of a buffer the model split nothing off from: a <c>VERSION</c> line and the class or
    /// designer block after it, then the leading attribute run, when the text has them.
    /// </summary>
    /// <remarks>
    /// The designer block is walked by nesting, because a <c>.frm</c>'s controls are nested
    /// <c>Begin</c>/<c>End</c> blocks and its fonts and images nested <c>BeginProperty</c>/<c>EndProperty</c>
    /// ones. A text that opens with none of this has no header, and blank lines alone are not one.
    /// </remarks>
    private static int HeaderLinesFromText(string text, List<Line> lines)
    {
        var i = 0;
        while (i < lines.Count && Trimmed(text, lines[i]).IsEmpty)
            i++;

        var sawBlock = false;
        if (i < lines.Count && StartsWithKeyword(Trimmed(text, lines[i]), "VERSION"))
        {
            sawBlock = true;
            i++;
            if (i < lines.Count && Trimmed(text, lines[i]).Equals("BEGIN", StringComparison.OrdinalIgnoreCase))
            {
                while (i < lines.Count && !Trimmed(text, lines[i]).Equals("END", StringComparison.OrdinalIgnoreCase))
                    i++;
                if (i < lines.Count)
                    i++;
            }
            else
            {
                while (i < lines.Count && StartsWithKeyword(Trimmed(text, lines[i]), "Object"))
                    i++;
                if (i < lines.Count && StartsWithKeyword(Trimmed(text, lines[i]), "Begin"))
                {
                    var depth = 0;
                    do
                    {
                        var line = Trimmed(text, lines[i]);
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

        var run = AttributeRunLength(text, lines, i);
        return sawBlock || run > 0 ? i + run : 0;
    }

    /// <summary>
    /// The number of lines, from <paramref name="from"/>, up to and including the last line of the
    /// <c>Attribute</c> run that starts there. Blank lines inside or in front of the run count; blank lines
    /// after it do not, and nor do blank lines with no run after them.
    /// </summary>
    private static int AttributeRunLength(string text, List<Line> lines, int from)
    {
        var lastAttribute = -1;
        for (var i = from; i < lines.Count; i++)
        {
            var line = Trimmed(text, lines[i]);
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
    /// <param name="qualifier">
    /// The attribute name up to its first <c>.</c>, which is the member it names; null when it has none. A line
    /// with no <c>.</c> is still an attribute line, and still part of a run, but it names no member.
    /// </param>
    private static bool IsMemberAttribute(string text, Line line, out string? qualifier)
    {
        qualifier = null;
        var rest = Trimmed(text, line);
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

    private static bool StartsWithKeyword(ReadOnlySpan<char> line, string keyword) =>
        line.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)
        && line.Length > keyword.Length
        && char.IsWhiteSpace(line[keyword.Length]);
}
