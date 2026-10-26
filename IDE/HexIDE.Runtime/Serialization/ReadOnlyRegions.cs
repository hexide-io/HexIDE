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
/// The run's whole lines, every physical line of a continued attribute line included. Its
/// <see cref="TextRegion.Anchor"/> is <paramref name="Anchor"/> whenever a line of code is above the run, so for
/// a run that describes a member this is exactly the region <see cref="ReadOnlyRegions.Of"/> returns.
/// </param>
/// <param name="Lines">Each attribute line of the run, in order, with what its name says.</param>
/// <param name="DescribedStart">
/// Where the described line starts: the first physical line of the logical line directly above the run.
/// </param>
/// <param name="Anchor">
/// Where the described line ends: the end of the text of the physical line directly above the run, before its
/// terminator. A fold of the run starts here.
/// </param>
/// <param name="LastContentEnd">The end of the text of the run's last physical line, before its terminator.</param>
/// <param name="Declared">
/// The names the described line declares, every statement of it read
/// (<see cref="ReadOnlyRegions.NamesDeclaredOn(string, int, int)"/>), each with where it is written and what it
/// declares. Empty when that line declares nothing.
/// </param>
/// <param name="Kind">
/// What the run describes, which is how VB6 binds its lines; <see cref="MemberRunKind.Orphan"/> for a run that
/// describes nothing, which is inert text and not a read-only region.
/// </param>
/// <param name="Member">
/// The member the run describes, as its declaration spells it: one of <paramref name="Declared"/>. Null for an
/// orphan. For a <see cref="MemberRunKind.Procedure"/> or <see cref="MemberRunKind.RenamedVariable"/> run, this
/// is the name the save writes into every line's qualifier (task 4.4), at each line's
/// <see cref="AttributeLine.QualifierOffset"/>.
/// </param>
/// <param name="Key">
/// Which run this is, in a form that outlives its offsets; null for an orphan. See <see cref="MemberRunKey"/>.
/// </param>
public sealed record MemberRun(
    TextRegion Region,
    IReadOnlyList<AttributeLine> Lines,
    int DescribedStart,
    int Anchor,
    int LastContentEnd,
    IReadOnlyList<DeclaredName> Declared,
    MemberRunKind Kind,
    DeclaredName? Member,
    MemberRunKey? Key)
{
    /// <summary>True for a run that describes a member: anything but an <see cref="MemberRunKind.Orphan"/>.</summary>
    public bool DescribesMember => Kind != MemberRunKind.Orphan;
}

/// <summary>
/// What a run of attribute lines describes (hexide-io/HexIDE#273 task 4.1), which follows how VB6 binds the two
/// kinds of member attribute: a procedure attribute by its position, and a variable attribute by its name
/// (design.md, "Settled before building phase 4").
/// </summary>
public enum MemberRunKind
{
    /// <summary>Nothing. The run is inert text: not read-only, greyed or folded, and kept byte for byte.</summary>
    Orphan,

    /// <summary>
    /// The procedure declared on the line directly above, every line being a procedure attribute. VB6 gives
    /// such a line to that procedure whatever its qualifier names, so the qualifiers are not consulted, and the
    /// save re-writes them to the procedure's name (task 4.4).
    /// </summary>
    Procedure,

    /// <summary>
    /// The module-level variables or constants the line directly above declares, every line being a variable
    /// attribute that names one of them. Left as written by the save.
    /// </summary>
    Variable,

    /// <summary>
    /// The one variable or constant the line directly above declares, every line being a variable attribute
    /// naming one and the same name that nothing in the module declares: the run a rename by retyping leaves.
    /// VB6 carries it to the new name, and the save re-writes its qualifiers to it (task 4.4).
    /// </summary>
    RenamedVariable,
}

/// <summary>One attribute line of a <see cref="MemberRun"/>: a logical line, perhaps continued with <c> _</c>.</summary>
/// <param name="Start">The offset of its first character, which is the start of its first physical line.</param>
/// <param name="End">The end of its text: where its last physical line's terminator starts.</param>
/// <param name="Kind">Which kind of attribute its name makes it.</param>
/// <param name="Qualifier">
/// Its attribute name up to the first <c>.</c>, as written, so <c>Total.VB_ProcData.VB_Invoke_Property</c> gives
/// <c>Total</c>. Null for a <see cref="AttributeLineKind.Dotless"/> line.
/// </param>
/// <param name="QualifierOffset">
/// Where <paramref name="Qualifier"/> is written, which may be on a physical line after the first; it is
/// <c>Qualifier.Length</c> characters long. -1 for a dotless line.
/// </param>
public readonly record struct AttributeLine(int Start, int End, AttributeLineKind Kind, string? Qualifier, int QualifierOffset);

/// <summary>Which kind of attribute a line's name makes it.</summary>
public enum AttributeLineKind
{
    /// <summary>
    /// No <c>.</c> in its name, or no name at all. Below the code VB6 reads it as the module's own, so it
    /// describes no member.
    /// </summary>
    Dotless,

    /// <summary>A dotted name whose part after the first <c>.</c> does not start <c>VB_Var</c>.</summary>
    Procedure,

    /// <summary>A dotted name whose part after the first <c>.</c> starts <c>VB_Var</c>, compared without case.</summary>
    Variable,
}

/// <summary>
/// A run that describes a member, named so that two texts can be compared and state can be kept across an
/// edit: the name of the member it describes as its declaration spells it, upper-cased, and its ordinal among
/// the runs that describe a member of that name, counted from the top of the file.
/// </summary>
/// <remarks>
/// The member's declared name and not a qualifier, because a procedure's run may name anything and a renamed
/// variable's names the old name. The name alone is not enough, because a property's <c>Get</c> and <c>Let</c>
/// share one and each may carry a run. Orphans are not counted, so repairing or removing one, which is free
/// text, does not renumber the runs of a member that still has its declaration.
/// </remarks>
public readonly record struct MemberRunKey(string Member, int Ordinal);

/// <summary>
/// A name a declaration writes, where, and what it declares: the name occupies <c>Name.Length</c> characters
/// from <see cref="Offset"/>, a type-declaration character or brackets around it aside.
/// </summary>
public readonly record struct DeclaredName(string Name, int Offset, DeclarationKind Kind);

/// <summary>What a <see cref="DeclaredName"/> names.</summary>
public enum DeclarationKind
{
    /// <summary>A <c>Sub</c>, <c>Function</c>, <c>Property</c> accessor, <c>Event</c> or <c>Declare</c>.</summary>
    Procedure,

    /// <summary>A variable: a declarator after <c>Dim</c> or a modifier.</summary>
    Variable,

    /// <summary>A constant: a declarator after <c>Const</c>.</summary>
    Constant,
}

/// <summary>Which of a property's accessors a <c>Property</c> declaration declares.</summary>
public enum PropertyAccessor
{
    /// <summary><c>Property Get</c>.</summary>
    Get,

    /// <summary><c>Property Let</c>.</summary>
    Let,

    /// <summary><c>Property Set</c>.</summary>
    Set,
}

/// <summary>
/// A declaration a run of attribute lines could describe, and for a property which accessor it is
/// (hexide-io/HexIDE#273 task 4.5b). See <see cref="ReadOnlyRegions.MemberDeclarations"/>.
/// </summary>
/// <param name="Name">The name it declares, where, and what it declares.</param>
/// <param name="Accessor">The accessor a <c>Property</c> declaration declares; null for every other declaration.</param>
/// <remarks>
/// A record of its own rather than a field on <see cref="DeclaredName"/>, whose positional equality tests pin.
/// </remarks>
public readonly record struct MemberDeclaration(DeclaredName Name, PropertyAccessor? Accessor);

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
/// <c>.ctl</c>/<c>.pag</c>, or a <c>.bas</c>/<c>.cls</c> whose header sits under a comment (hexide-io/HexIDE#472),
/// keeps its whole file in the code. Its prefix is then empty, but the buffer still opens with a header.
/// </para>
/// <para>
/// <b>A run of attribute lines is a region only when it describes a member</b> (task 4.1): when it sits
/// directly under a declaration and VB6 reads every line of it as that declaration's. Anything else is an
/// orphan — a run under a blank line, a comment or a body statement, a run mixing the two kinds of attribute,
/// a variable attribute away from its variable, or a line with no <c>.</c> below the code — and an orphan is
/// inert text the developer can see and change, which is the only way to repair one. <see cref="MemberRuns"/>
/// reports both kinds, with the verdict.
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
    /// <b>A run</b> is a maximal block of consecutive logical lines below the header whose first word is
    /// <c>Attribute</c>, in any case and at any indent, each with the physical lines it continues into with
    /// <c> _</c>. A physical line that continues the line above it is never an attribute line: it is part of that
    /// line's statement, or of its comment, which is how VB6 reads it (oracle probes p07 and p07c). A blank line
    /// ends a run, so a run split by a blank line is two runs, and the second hangs from the blank line.
    /// </para>
    /// <para>
    /// <b>What it describes is how VB6 binds its lines</b> (design.md, "Settled before building phase 4"). Under
    /// a procedure's declaration, a run of procedure attributes describes that procedure, whatever its
    /// qualifiers name, because VB6 binds such a line by position. Under a module-level variable or constant's
    /// declaration, a run of <c>VB_Var…</c> lines describes it when each line names a name that line declares,
    /// because VB6 binds such a line by name; or, when the line declares one name, when every line names one and
    /// the same name that nothing in the module declares, which is the run a rename by retyping leaves. Every
    /// other run is an orphan. See <see cref="MemberRunKind"/>.
    /// </para>
    /// <para>
    /// <b>It is read lexically</b>: the described line for the names it writes
    /// (<see cref="NamesDeclaredOn(string, int, int)"/>), and, for the renamed kind alone, every line of the
    /// module and the designer block for the names they write, and the module for where its first procedure
    /// starts. That is collection, not binding: a qualifier is compared with names written down, and never
    /// resolved to a declaration. The module's names err towards too many, a procedure's locals included, which
    /// errs towards an orphan and never towards giving a run to a member whose name it does not carry.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<MemberRun> MemberRuns(string text, int prefixLength) => Scan(text, prefixLength).Runs;

    /// <summary>
    /// The names the declarations on <c>[start, end)</c> of <paramref name="text"/> write, each with where it is
    /// written and what it declares; empty when that text declares nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Lexical, and line-local.</b> Every statement on the line is read, <c>:</c>-separated ones included,
    /// because VB6's own Procedure Attributes dialog writes a run under <c>Public A As Long: Public B As Long</c>
    /// for <c>B</c> (IDE probe E). Each statement is read, without case, as:
    /// </para>
    /// <list type="bullet">
    /// <item><c>[Public|Private|Friend|Global|Static]… Sub|Function|Event &lt;name&gt;</c>, and
    /// <c>Property Get|Let|Set &lt;name&gt;</c> after the same modifiers: a procedure;</item>
    /// <item><c>[Public|Private] Declare [PtrSafe] Sub|Function &lt;name&gt;</c>, <c>PtrSafe</c> being the same
    /// declaration in VBA7: a procedure;</item>
    /// <item>declarators after <c>[Public|Private|Global] Const</c>: constants, <c>Const C1 = 1, C2 = 2</c>
    /// declaring both (IDE probe V5);</item>
    /// <item>declarators after at least one of <c>Public|Private|Friend|Global|Static|Dim</c>:
    /// <c>[WithEvents] &lt;name&gt;[(…)] [As …]</c>, as a comma list, each with its own <c>WithEvents</c>:
    /// variables.</item>
    /// </list>
    /// <para>
    /// A <c> _</c> line continuation and the line break after it are whitespace, so <c>[start, end)</c> may be a
    /// whole continued logical line. A comment ends the line, a <c>'</c> anywhere or <c>Rem</c> where a statement
    /// starts, and a <c>:</c> in it is the comment's. A string is one token, so a <c>:</c>, <c>'</c> or <c>,</c> in
    /// a constant's value is none of those. A name may carry a type-declaration character (<c>Total$</c> declares
    /// <c>Total</c>) or be bracketed. <c>Enum</c> and <c>Type</c> declare nothing a run can describe.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<DeclaredName> NamesDeclaredOn(string text, int start, int end) =>
        ReadDeclarations(text, start, end, out _);

    /// <summary>The names <paramref name="line"/> declares; see <see cref="NamesDeclaredOn(string, int, int)"/>.</summary>
    public static IReadOnlyList<DeclaredName> NamesDeclaredOn(string line) => NamesDeclaredOn(line, 0, line.Length);

    /// <summary>
    /// Every declaration below the header of <paramref name="text"/> that a run of attribute lines could describe,
    /// in order: every procedure, and the variables and constants of the declarations section.
    /// </summary>
    /// <param name="text">The whole buffer, or any whole file: give 0 for <paramref name="prefixLength"/>.</param>
    /// <param name="prefixLength">
    /// The length of the header the buffer was composed with, or 0 when the model split nothing off.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>What is declared, described or not</b>, for comparing two texts (task 4.5b): a run's own
    /// <see cref="MemberRun.Member"/> says what that run describes, and is one of these, at the same offset. Read as
    /// <see cref="MemberRuns"/> reads the line above a run, every statement of every logical line, and with the same
    /// declarations section: before the first logical line that opens a procedure's body, so a procedure's own
    /// <c>Dim</c>, which no run can describe, is not one.
    /// </para>
    /// <para>
    /// <b>A property's accessor is reported</b> because the name alone cannot say which of a property's
    /// declarations a run hangs from, and it matters to VB6: it reads a property's description from its first
    /// accessor in the file alone, and a run under the second reaches nothing (oracle probes p14d, p14e).
    /// </para>
    /// </remarks>
    public static IReadOnlyList<MemberDeclaration> MemberDeclarations(string text, int prefixLength)
    {
        var lines = LinesOf(text);
        var declarations = new List<MemberDeclaration>();
        var accessors = new List<PropertyAccessor?>();
        var inBody = false;
        foreach (var line in LogicalLinesOf(text, lines, HeaderLineCount(text, lines, prefixLength)))
        {
            accessors.Clear();
            var names = ReadDeclarations(text, lines[line.First].Start, lines[line.Last].ContentEnd, out var opensBody, accessors);
            // A line that opens a body is past the declarations section itself, as Scan reads it: a variable declared
            // before a Sub on one line is not a module-level one.
            var moduleLevel = !inBody && !opensBody;
            for (var i = 0; i < names.Count; i++)
            {
                if (names[i].Kind == DeclarationKind.Procedure || moduleLevel)
                    declarations.Add(new MemberDeclaration(names[i], accessors[i]));
            }
            inBody |= opensBody;
        }
        return declarations;
    }

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
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddDesignerNames(text, lines, HeaderLineCount(text, lines, prefixLength), names);
        return names;
    }

    /// <summary>Adds the name on each <c>Begin</c> line of the header's first <paramref name="headerLines"/> lines.</summary>
    private static void AddDesignerNames(string text, List<Line> lines, int headerLines, HashSet<string> names)
    {
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

    /// <summary>A logical line: its first physical line and its last, which is the first not continued with <c> _</c>.</summary>
    private readonly record struct LogicalLine(int First, int Last);

    /// <summary>
    /// The lines of <paramref name="text"/>, how many of them the header takes, and every attribute run below
    /// it with its verdict: the one scan <see cref="Of"/> and <see cref="MemberRuns"/> both read.
    /// </summary>
    private static (List<Line> Lines, int HeaderLines, List<MemberRun> Runs) Scan(string text, int prefixLength)
    {
        var lines = LinesOf(text);
        var headerLines = HeaderLineCount(text, lines, prefixLength);
        var logical = LogicalLinesOf(text, lines, headerLines);
        var runs = new List<MemberRun>();
        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);

        // Read only for a variable run, and then once: where the first procedure body starts, and the names the
        // whole module writes. Most files never need either.
        int? firstBody = null;
        HashSet<string>? moduleNames = null;

        var k = 0;
        while (k < logical.Count)
        {
            if (!IsAttributeLine(text, lines[logical[k].First]))
            {
                k++;
                continue;
            }
            var first = k;
            var attributes = new List<AttributeLine>();
            for (; k < logical.Count && IsAttributeLine(text, lines[logical[k].First]); k++)
                attributes.Add(ReadAttributeLine(text, lines[logical[k].First].Start, lines[logical[k].Last].ContentEnd));

            // The run hangs from the logical line above it, which is the member it describes if it describes one.
            // The first line of the code has none of its own: above it is the header, which declares nothing. A run
            // there is a line the header's own reading does not take for an attribute line, such as
            // Attribute(1) = 2, which names nothing either.
            var region = RegionOf(lines, logical[first].First, logical[k - 1].Last + 1, text.Length);
            var describedStart = region.Start;
            var anchor = region.Start;
            IReadOnlyList<DeclaredName> declared = [];
            var kind = MemberRunKind.Orphan;
            DeclaredName? member = null;
            if (first > 0)
            {
                var above = logical[first - 1];
                describedStart = lines[above.First].Start;
                anchor = lines[above.Last].ContentEnd;
                region = region with { Anchor = anchor };
                var names = ReadDeclarations(text, describedStart, anchor, out _);
                declared = names;
                (kind, member) = Verdict(attributes, names, () => first - 1 < FirstBody(), name => ModuleNames().Contains(name));
            }

            MemberRunKey? key = null;
            if (member is { } described)
            {
                var name = described.Name.ToUpperInvariant();
                ordinals.TryGetValue(name, out var ordinal);
                ordinals[name] = ordinal + 1;
                key = new MemberRunKey(name, ordinal);
            }
            runs.Add(new MemberRun(region, attributes, describedStart, anchor, lines[logical[k - 1].Last].ContentEnd,
                declared, kind, member, key));
        }
        return (lines, headerLines, runs);

        // The first logical line that opens a procedure's body. Before it is the declarations section, which is
        // the only place VB6 allows a module-level variable or constant; after it, a declaration is a procedure's
        // own. An Event or a Declare opens no body.
        int FirstBody()
        {
            if (firstBody is null)
            {
                firstBody = logical.Count;
                for (var i = 0; i < logical.Count; i++)
                {
                    ReadDeclarations(text, lines[logical[i].First].Start, lines[logical[i].Last].ContentEnd, out var opensBody);
                    if (opensBody)
                    {
                        firstBody = i;
                        break;
                    }
                }
            }
            return firstBody.Value;
        }

        // Every name the module writes a declaration of, read the same way from every logical line, a procedure's
        // own locals included, and the designer block's controls.
        HashSet<string> ModuleNames()
        {
            if (moduleNames is null)
            {
                moduleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                AddDesignerNames(text, lines, headerLines, moduleNames);
                foreach (var line in logical)
                {
                    foreach (var d in ReadDeclarations(text, lines[line.First].Start, lines[line.Last].ContentEnd, out _))
                        moduleNames.Add(d.Name);
                }
            }
            return moduleNames;
        }
    }

    /// <summary>
    /// What a run of <paramref name="attributes"/> under a line declaring <paramref name="declared"/> describes,
    /// and which of those names it is.
    /// </summary>
    /// <param name="moduleLevel">Whether the described line is in the module's declarations section.</param>
    /// <param name="declaredInModule">Whether the module writes a declaration of a name anywhere.</param>
    private static (MemberRunKind Kind, DeclaredName? Member) Verdict(List<AttributeLine> attributes,
        List<DeclaredName> declared, Func<bool> moduleLevel, Func<string, bool> declaredInModule)
    {
        // A procedure attribute goes to the procedure declared directly above it, whatever its qualifier names
        // (oracle probes p04, p05a, p05b).
        if (attributes.TrueForAll(a => a.Kind == AttributeLineKind.Procedure))
        {
            foreach (var d in declared)
            {
                if (d.Kind == DeclarationKind.Procedure)
                    return (MemberRunKind.Procedure, d);
            }
            return (MemberRunKind.Orphan, null);
        }

        // A variable attribute goes to the module-level variable or constant its qualifier names. A mixed run, a
        // dotless line, and a variable attribute under a procedure's own declaration describe nothing here.
        if (!attributes.TrueForAll(a => a.Kind == AttributeLineKind.Variable) || !moduleLevel())
            return (MemberRunKind.Orphan, null);
        if (attributes.TrueForAll(a => VariableNamed(declared, a.Qualifier!) is not null))
            return (MemberRunKind.Variable, VariableNamed(declared, attributes[0].Qualifier!));

        // The run a rename by retyping leaves: a line declaring one name, under it a run naming one other name, and
        // that name declared nowhere. Were it declared elsewhere, VB6 would give the run to that one.
        var stale = attributes[0].Qualifier!;
        if (declared.Count == 1 && declared[0].Kind != DeclarationKind.Procedure
            && attributes.TrueForAll(a => string.Equals(a.Qualifier, stale, StringComparison.OrdinalIgnoreCase))
            && !declaredInModule(stale))
            return (MemberRunKind.RenamedVariable, declared[0]);
        return (MemberRunKind.Orphan, null);
    }

    /// <summary>The variable or constant among <paramref name="declared"/> called <paramref name="name"/>, compared without case.</summary>
    private static DeclaredName? VariableNamed(List<DeclaredName> declared, string name)
    {
        foreach (var d in declared)
        {
            if (d.Kind != DeclarationKind.Procedure && string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase))
                return d;
        }
        return null;
    }

    /// <summary>The logical lines of <paramref name="lines"/> from <paramref name="from"/> on.</summary>
    /// <remarks>
    /// From the first line of the code, and never back into the header: a header's last line is an
    /// <c>Attribute</c> line or a block's <c>End</c>, neither of which a developer continues.
    /// </remarks>
    private static List<LogicalLine> LogicalLinesOf(string text, List<Line> lines, int from)
    {
        var logical = new List<LogicalLine>();
        var i = from;
        while (i < lines.Count)
        {
            var last = i;
            while (last + 1 < lines.Count && EndsWithContinuation(
                       text.AsSpan(lines[last].Start, lines[last].ContentEnd - lines[last].Start)))
                last++;
            logical.Add(new LogicalLine(i, last));
            i = last + 1;
        }
        return logical;
    }

    /// <summary>
    /// True when <paramref name="line"/> continues into the next: its text ends with an underscore after
    /// whitespace, trailing whitespace aside.
    /// </summary>
    /// <remarks>
    /// The rule VB6 holds to, whitespace then an underscore, with no exemption at the start of a line: a line of
    /// nothing but an underscore continues only when it is indented (docs/vb6-fidelity-oracle/line-continuations-and-statement-separators.md). And
    /// deliberately not <c>VbAutoCloseProvider.IsContinuationLine</c>'s looser one, which also takes an identifier
    /// that ends in <c>_</c> for a continuation.
    /// </remarks>
    public static bool EndsWithContinuation(ReadOnlySpan<char> line)
    {
        var content = line.TrimEnd();
        return content.Length > 1 && content[^1] == '_' && char.IsWhiteSpace(content[^2]);
    }

    /// <summary>
    /// The declarations on <c>[start, end)</c>, as <see cref="NamesDeclaredOn(string, int, int)"/> reads them.
    /// </summary>
    /// <param name="opensBody">
    /// True when a statement on it declares a <c>Sub</c>, a <c>Function</c> or a <c>Property</c> accessor, which
    /// opens a procedure's body: the end of the module's declarations section.
    /// </param>
    /// <param name="accessors">
    /// When given, receives one entry for each name returned, in step: the accessor a <c>Property</c> declaration
    /// declares, and null for every other (<see cref="MemberDeclarations"/>).
    /// </param>
    private static List<DeclaredName> ReadDeclarations(string text, int start, int end, out bool opensBody,
        List<PropertyAccessor?>? accessors = null)
    {
        var tokens = TokensOf(text, start, end);
        var names = new List<DeclaredName>();
        opensBody = false;
        var from = 0;
        while (from < tokens.Count)
        {
            var to = from;
            while (to < tokens.Count && tokens[to].Kind != TokenKind.Colon)
                to++;
            opensBody |= ReadStatement(text, tokens, from, to, names, accessors);
            from = to + 1;
        }
        return names;
    }

    /// <summary>
    /// Adds the names the statement of <paramref name="tokens"/> <c>[t, end)</c> declares to
    /// <paramref name="names"/>, and to <paramref name="accessors"/> when given what each declares of a property;
    /// true when it opens a procedure's body.
    /// </summary>
    private static bool ReadStatement(string text, List<Token> tokens, int t, int end, List<DeclaredName> names,
        List<PropertyAccessor?>? accessors)
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
            AddName(t + 2, DeclarationKind.Procedure,
                IsWord(text, tokens, t + 1, end, "Get") ? PropertyAccessor.Get
                : IsWord(text, tokens, t + 1, end, "Let") ? PropertyAccessor.Let
                : PropertyAccessor.Set);
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
        // Without a declaring keyword the statement is not a declaration: an assignment, a call, a ReDim.
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

                // Past the rest of this declarator -- its bounds, its As clause, a constant's value -- to the comma
                // that starts the next, which a comma inside parentheses does not.
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

        void AddName(int at, DeclarationKind kind, PropertyAccessor? accessor = null)
        {
            if (at < end && tokens[at].Kind == TokenKind.Word)
            {
                names.Add(new DeclaredName(text.Substring(tokens[at].Start, tokens[at].Length), tokens[at].Start, kind));
                accessors?.Add(accessor);
            }
        }
    }

    private enum TokenKind { Word, Open, Close, Comma, Colon, Other }

    /// <summary>
    /// A token of a declaration: a name or keyword (<see cref="TokenKind.Word"/>, a bracketed name without its
    /// brackets and a name without its type character), a parenthesis, a comma, a <c>:</c>, or anything else.
    /// </summary>
    private readonly record struct Token(TokenKind Kind, int Start, int Length);

    /// <summary>
    /// The tokens on <c>[start, end)</c>, up to a comment or the end: every statement's, each ended by a
    /// <see cref="TokenKind.Colon"/>.
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
            if (c == '\'')
                break;
            if (c == '"')
            {
                // A string, "" inside it being a quote: a ' or : or , in one is none of those.
                var opening = i++;
                // A line end, a lone CR included, ends an unclosed string (#1020). In text VB6 accepts a string closes on
                // its own line and a continuation cannot sit inside one, so no verdict turns on it (equivalent under
                // mutation).
                while (i < end && !SourceLines.IsLineEnd(text[i]))
                {
                    if (text[i] != '"')
                        i++;
                    // A doubled quote is read as VB6 lexes it, inside the string. Taking it for an end would split
                    // the string in two at the pair and leave every character either side inside one string or the
                    // other, and nothing counts strings, so no verdict turns on this (equivalent under mutation).
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
                // A type-declaration character after the name is left to be read as a token of its own, which
                // nothing reads.
                var j = i + 1;
                while (j < end && IsNameCharacter(text[j]))
                    j++;
                // Rem is a comment where a statement starts, and like ' takes the rest of the line, a : included. The
                // position test is defensive: Rem is a reserved word that cannot stand inside a statement VB6
                // accepts, so on such code no verdict turns on it (equivalent under mutation).
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
        for (var i = from; i < end && !SourceLines.IsLineEnd(text[i]); i++)
        {
            if (!char.IsWhiteSpace(text[i]))
                return false;
        }
        return true;
    }

    /// <summary>True when the token at <paramref name="at"/>, before <paramref name="end"/>, is one of <paramref name="words"/>.</summary>
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

    private static List<Line> LinesOf(string text)
    {
        var lines = new List<Line>();
        var pos = 0;
        while (pos < text.Length)
        {
            var (end, next) = SourceLines.LineAt(text, pos);
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
    /// True when the logical line starting on <paramref name="line"/> is an attribute line: its first word is
    /// <c>Attribute</c>, in any case and at any indent.
    /// </summary>
    /// <remarks>
    /// <c>Attribute</c> is a reserved word in code (oracle probe p07e), so a line that starts with the word is
    /// one however it goes on, <c>Attribute = 5</c> included; such a line names nothing, and describes nothing.
    /// <c>AttributeCount = 3</c> starts with another word.
    /// </remarks>
    private static bool IsAttributeLine(string text, Line line)
    {
        var content = text.AsSpan(line.Start, line.ContentEnd - line.Start).TrimStart();
        return content.StartsWith("Attribute", StringComparison.OrdinalIgnoreCase)
               && (content.Length == "Attribute".Length || !IsNameCharacter(content["Attribute".Length]));
    }

    /// <summary>
    /// The attribute line on <c>[start, end)</c>, a logical line <see cref="IsAttributeLine"/> accepted: its
    /// name, read past any <c> _</c> continuation after the word, and the kind and qualifier the name gives it.
    /// </summary>
    /// <remarks>
    /// The name may be on a later physical line than the word, as in <c>Attribute _</c> above
    /// <c>Foo.VB_Description = "x"</c>, which VB6 reads as one attribute line (oracle probe p11b).
    /// </remarks>
    private static AttributeLine ReadAttributeLine(string text, int start, int end)
    {
        var i = start;
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
            return new AttributeLine(start, end, AttributeLineKind.Dotless, null, -1);

        // Not measured with the name in another case; compared without case, as VB6 compares names.
        var kind = text.AsSpan(dot + 1, i - dot - 1).StartsWith("VB_Var", StringComparison.OrdinalIgnoreCase)
            ? AttributeLineKind.Variable
            : AttributeLineKind.Procedure;
        return new AttributeLine(start, end, kind, text[name..dot], name);
    }

    private static bool StartsWithKeyword(ReadOnlySpan<char> line, string keyword) =>
        line.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)
        && line.Length > keyword.Length
        && char.IsWhiteSpace(line[keyword.Length]);
}
