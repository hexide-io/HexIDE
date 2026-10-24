using System;
using System.Collections.Generic;
using System.Text;

namespace HexIDE.Runtime.Serialization;

/// <summary>What replacing a document's content came to.</summary>
/// <param name="NewText">The whole buffer to put in place, or null when the replacement was refused.</param>
/// <param name="Refusal">Why it was refused, in words a caller can act on; null when it was not.</param>
/// <param name="HeaderKept">
/// True when the incoming text carried no header of its own, or only part of it, so the document's header was
/// put in front of it. A caller that reports on its write can say so.
/// </param>
public readonly record struct ContentReplacement(string? NewText, string? Refusal, bool HeaderKept);

/// <summary>
/// Replacing a whole document's content without letting the replacement change a read-only region: the rule the
/// add-in <c>SetContent</c> and automation <c>set_file_content</c> rows of the design record share
/// (hexide-io/HexIDE#273 tasks 3.9 and 4.5b).
/// </summary>
/// <remarks>
/// <para>
/// <b>Three shapes are accepted, and one is refused.</b> A caller may send the whole file with its header
/// unchanged; the code alone; or the code preceded by the part of the header that sits in it — a form's
/// <c>Attribute</c> lines, which is what a form's code section has always begun with and what reading one
/// back has always returned. In each case the document's own header is what ends up in front. A header that
/// differs from the document's is refused, because the header is the IDE's to change, from its model.
/// </para>
/// <para>
/// <b>"Unchanged" compares line text, not terminators.</b> A caller that sends <c>\n</c> for a
/// <c>\r\n</c> document has not changed its header, and the header written back is the document's own, byte
/// for byte. A member's attribute lines are compared the same way.
/// </para>
/// <para>
/// <b>A member's attribute run is the other read-only region, and it is checked even when the header differs</b>
/// (task 4.5b; design.md, "Whole replacements"). The runs that describe a member in the document are compared
/// with those in the text the replacement would leave. A run may move with its member, be renamed with it, and go
/// with its declaration; any other change, any run added and any run lost while its declaration stays is refused,
/// with a reason naming each member. A run that describes nothing is inert text, not a region, and is free. See
/// <see cref="MemberRunRefusal"/>.
/// </para>
/// </remarks>
public static class GuardedContent
{
    /// <summary>
    /// The buffer that replacing <paramref name="current"/>'s content with <paramref name="incoming"/> gives,
    /// or the reason it is refused.
    /// </summary>
    /// <param name="current">The document's whole buffer as it stands.</param>
    /// <param name="prefixLength">The length of the header the buffer was composed with; see <see cref="ReadOnlyRegions.Of"/>.</param>
    /// <param name="incoming">What the caller wants the document to hold.</param>
    public static ContentReplacement Replace(string current, int prefixLength, string incoming)
    {
        var header = current[..ReadOnlyRegions.HeaderEnd(current, prefixLength)];
        var incomingHeaderEnd = ReadOnlyRegions.HeaderEnd(incoming, 0);
        string composed;
        bool headerKept;
        string? headerRefusal = null;
        if (incomingHeaderEnd == 0)
        {
            composed = header + incoming;
            headerKept = header.Length > 0;
        }
        else
        {
            // Blank lines in front of the incoming header are the caller's formatting, not a header line.
            var theirs = LinesOf(incoming[..incomingHeaderEnd]);
            while (theirs.Count > 0 && theirs[0].Trim().Length == 0)
                theirs.RemoveAt(0);
            var ours = LinesOf(header);
            if (theirs.Count > ours.Count || !EndsWith(ours, theirs))
                headerRefusal = "The content would change the document's header (its designer block, class header or leading "
                    + "Attribute lines), which only the IDE changes. Send the code alone, or the whole file with its "
                    + "header exactly as the document has it.";
            composed = header + incoming[incomingHeaderEnd..];
            headerKept = theirs.Count < ours.Count;
        }

        // The composed text opens with the document's own header, byte for byte, so the document's prefix length
        // is the composed text's too.
        var memberRefusal = MemberRunRefusal(current, composed, prefixLength);
        var refusal = headerRefusal is null ? memberRefusal
            : memberRefusal is null ? headerRefusal : headerRefusal + " " + memberRefusal;
        return refusal is not null
            ? new ContentReplacement(null, refusal, HeaderKept: false)
            : new ContentReplacement(composed, null, headerKept);
    }

    /// <summary>
    /// How a refusal names the member the run occupying <paramref name="region"/> of <paramref name="text"/>
    /// describes, or null when no run that describes a member occupies it: the header, say.
    /// </summary>
    /// <remarks>
    /// For the range writers' refusal (the code window's <c>ApplyEdits</c>), so that a refused edit and a refused
    /// replacement name a member the same way: by its name as declared, a property's accessor with it, and a
    /// variable's run by every variable its lines name.
    /// </remarks>
    public static string? MemberNamedBy(string text, int prefixLength, TextRegion region)
    {
        var declarations = ReadOnlyRegions.MemberDeclarations(text, prefixLength);
        foreach (var run in ReadOnlyRegions.MemberRuns(text, prefixLength))
        {
            // Defensive: every region a caller holds comes from ReadOnlyRegions.Of, which never returns an orphan's,
            // so the region test alone decides today. An orphan names no member for NamesOf to read.
            if (run.DescribesMember && run.Region == region)
                return Joined(NamesOf(run, AccessorOf(run, declarations)));
        }
        return null;
    }

    /// <summary>
    /// Why replacing <paramref name="before"/> with <paramref name="after"/> would change a member's attribute run,
    /// or null when it would not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A run is matched by the declaration it hangs from: the member's name, whether it is a procedure, and for a
    /// property the accessor.</b> Not by <see cref="MemberRunKey"/>'s ordinal, which is file order: deleting a
    /// property's first accessor, or putting its Let above its Get, renumbers the runs of the accessors that remain,
    /// and the ordinal would then compare one accessor's run with another's (settled while building 4.5b). What the
    /// guard keeps is a run's text and the declaration it hangs from, not what VB6 compiles from them: a property's
    /// Get and Let are two declarations, so a run moved from one to the other is lost by the one and added to the
    /// other, though the name carries both. A procedure retyped to a variable of its name, or the other way, leaves
    /// a declaration of the other kind, under which the run describes nothing (oracle probes p05i, p05h), so the run
    /// went with its declaration, as it does when the declaration is commented out.
    /// </para>
    /// <para>
    /// <b>A run's lines are read without a qualifier that stands for the run's own member</b>: every qualifier of a
    /// procedure's run and of a renamed variable's, which VB6 does not consult and the save writes the member's name
    /// over (task 4.4), and a variable's where it names the variable the run is keyed by. That is what lets a run
    /// be renamed with its member, by a replacement that re-qualifies it or one that leaves the old name for the
    /// save. Every other qualifier is read as written, because a variable attribute names the declarator it binds
    /// to, and line terminators are not read, as the header's are not.
    /// </para>
    /// <para>
    /// <b>Three passes.</b> Runs of one declaration are matched first, as many to as many by their lines, so
    /// runs that only moved, or a property's Get and Let swapped with a run each, pass. What is left in the
    /// replacement is paired with what is left in the document as a rename, where a declaration went and another
    /// came: the run's lines unchanged, under a member declared fewer times than before, now under one declared
    /// more, the two declarations sharing the name, which is a retype, or the accessor, which is a rename
    /// (<see cref="RenamedOrRetyped"/>). What is left after that in the document is a run gone with its
    /// declaration, while that member is declared fewer times than before, and otherwise a run lost while its
    /// declaration stays; what is left in the replacement is added. A member with both is a run changed.
    /// </para>
    /// <para>
    /// <b>A rename pairs by lines and counts, so it cannot tell which renamed declaration became which</b>: two
    /// members renamed in one replacement may each take the other's run, and two calls, renaming and renaming
    /// back, then leave each name with the other's description, where one call swapping the descriptions is
    /// refused. Accepted (design.md, "Settled while building 4.5b"): the code window gives the same end state by
    /// retyping the two names, since a procedure's run follows its position. Pairing is greedy, in file order,
    /// which can matter only between runs whose lines are the same.
    /// </para>
    /// <para>
    /// <b>An orphan turned into a run is an addition</b>, a pasted line under a declaration included, though in the
    /// code window such a line is a run at once (task 4.5a). A replacement cannot be told from text its caller
    /// wrote, and an orphan is free text, so a caller that could promote one could add any run in two calls. The
    /// developer repairs an orphan in the code window.
    /// </para>
    /// </remarks>
    private static string? MemberRunRefusal(string before, string after, int prefixLength)
    {
        var was = DescribedRuns(before, prefixLength, out var declaredBefore);
        var now = DescribedRuns(after, prefixLength, out var declaredAfter);

        // Runs of one declaration, matched by their lines, in any order.
        var leftBefore = new List<Described>();
        var leftAfter = new List<Described>(now);
        foreach (var run in was)
        {
            var match = leftAfter.FindIndex(n => n.Member == run.Member && n.Lines == run.Lines);
            if (match >= 0)
                leftAfter.RemoveAt(match);
            else
                leftBefore.Add(run);
        }
        // A shortcut: when nothing is left, the passes below find nothing to refuse either.
        if (leftBefore.Count == 0 && leftAfter.Count == 0)
            return null;

        // How many declarations of each member went, and how many came.
        var went = new Dictionary<Member, int>();
        var came = new Dictionary<Member, int>();
        foreach (var (member, count) in declaredBefore)
            went[member] = count - declaredAfter.GetValueOrDefault(member);
        foreach (var (member, count) in declaredAfter)
            came[member] = count - declaredBefore.GetValueOrDefault(member);

        // A rename: a declaration went, another came, and the run's lines came with it unchanged.
        var added = new List<Described>();
        foreach (var run in leftAfter)
        {
            var from = came.GetValueOrDefault(run.Member) > 0
                ? leftBefore.FindIndex(b => went.GetValueOrDefault(b.Member) > 0 && b.Lines == run.Lines
                                            && RenamedOrRetyped(b.Member, run.Member))
                : -1;
            if (from < 0)
            {
                added.Add(run);
                continue;
            }
            went[leftBefore[from].Member]--;
            came[run.Member]--;
            leftBefore.RemoveAt(from);
        }

        // A run gone with its declaration; otherwise lost while its declaration stays.
        var lost = new List<Described>();
        foreach (var run in leftBefore)
        {
            if (went.GetValueOrDefault(run.Member) > 0)
                went[run.Member]--;
            else
                lost.Add(run);
        }
        if (lost.Count == 0 && added.Count == 0)
            return null;

        // A member that lost a run and gained one had a run changed, which is how its caller will think of it.
        var findings = new List<string>();
        foreach (var run in lost)
        {
            var changed = added.FindIndex(a => a.Member == run.Member);
            if (changed >= 0)
            {
                // Named by what either version names: a variable whose line only one of them carries is reached too.
                var names = new List<string>(run.Names);
                foreach (var name in added[changed].Names)
                {
                    if (!names.Contains(name))
                        names.Add(name);
                }
                findings.Add($"the Attribute lines describing {Joined(names)} are changed");
                added.RemoveAt(changed);
            }
            else
                findings.Add($"the Attribute lines describing {run.Who} are removed, though its declaration stays");
        }
        foreach (var run in added)
            findings.Add($"Attribute lines describing {run.Who} are added");

        return "The content would change the Attribute lines that describe a member, which only the IDE changes: "
               + string.Join("; ", findings) + ". A member's Attribute lines may move with it, be renamed with it, "
               + "and go when its declaration goes, and may not otherwise change: send them exactly as the document "
               + "has them. Renaming a member and changing its Attribute lines, or retyping its declaration, in one "
               + "replacement reads as lines added to the new name. Nothing was changed.";
    }

    /// <summary>
    /// Whether a run under <paramref name="from"/>'s declaration, gone, may be the run under
    /// <paramref name="to"/>'s, come: across a rename, which keeps the accessor, or a retype, which keeps the name,
    /// and not both at once.
    /// </summary>
    /// <remarks>
    /// Both at once is a run moving between two declarations that both stay, renamed: a property's Get and Let
    /// renamed together, the run leaving the Get for the Let. With the name kept that move is refused as a loss and
    /// an addition, so allowing it here would let two calls, renaming and renaming back, make it. Text cannot tell it
    /// from a lone accessor retyped and renamed in one replacement, which is refused with it and takes two. Whether
    /// a declaration is a procedure is not compared: a procedure's run and a variable's never have the same lines.
    /// </remarks>
    private static bool RenamedOrRetyped(Member from, Member to) => from.Name == to.Name || from.Accessor == to.Accessor;

    /// <summary>
    /// The declaration a run hangs from, as two texts can share it: its name without case, whether it declares a
    /// procedure, and a property's accessor.
    /// </summary>
    private readonly record struct Member(string Name, bool Procedure, PropertyAccessor? Accessor);

    /// <summary>A run that describes a member, with what the comparison reads of it and how a refusal names it.</summary>
    /// <param name="Lines">Its lines as compared: terminators normalised, and every qualifier that stands for the member removed.</param>
    /// <param name="Names">How a refusal names it; see <see cref="NamesOf"/>.</param>
    private sealed record Described(Member Member, string Lines, IReadOnlyList<string> Names)
    {
        public string Who => Joined(Names);
    }

    /// <summary>
    /// Every run of <paramref name="text"/> that describes a member, read for comparison, and how many times the text
    /// declares each member a run could describe.
    /// </summary>
    private static List<Described> DescribedRuns(string text, int prefixLength, out Dictionary<Member, int> declared)
    {
        var declarations = ReadOnlyRegions.MemberDeclarations(text, prefixLength);
        declared = new Dictionary<Member, int>();
        foreach (var declaration in declarations)
        {
            var member = new Member(declaration.Name.Name.ToUpperInvariant(),
                declaration.Name.Kind == DeclarationKind.Procedure, declaration.Accessor);
            declared[member] = declared.GetValueOrDefault(member) + 1;
        }

        var runs = new List<Described>();
        foreach (var run in ReadOnlyRegions.MemberRuns(text, prefixLength))
        {
            if (run is not { DescribesMember: true, Member: { } described })
                continue;
            var accessor = AccessorOf(run, declarations);
            runs.Add(new Described(
                new Member(described.Name.ToUpperInvariant(), described.Kind == DeclarationKind.Procedure, accessor),
                ComparedLines(text, run), NamesOf(run, accessor)));
        }
        return runs;
    }

    /// <summary>
    /// The accessor of the property <paramref name="run"/> describes, found by the offset its declaration shares with
    /// <paramref name="declarations"/>; null when it describes no property.
    /// </summary>
    private static PropertyAccessor? AccessorOf(MemberRun run, IReadOnlyList<MemberDeclaration> declarations)
    {
        foreach (var declaration in declarations)
        {
            if (declaration.Name == run.Member)
                return declaration.Accessor;
        }
        return null;
    }

    /// <summary>
    /// <paramref name="run"/>'s lines as the comparison reads them: each line's text, its terminators as <c>\n</c>,
    /// without a qualifier that stands for the run's own member.
    /// </summary>
    private static string ComparedLines(string text, MemberRun run)
    {
        var lines = new StringBuilder();
        foreach (var line in run.Lines)
        {
            var standsForMember = run.Kind != MemberRunKind.Variable
                                  || string.Equals(line.Qualifier, run.Member!.Value.Name, StringComparison.OrdinalIgnoreCase);
            if (standsForMember && line.Qualifier is { } qualifier)
            {
                lines.Append(text, line.Start, line.QualifierOffset - line.Start);
                lines.Append(text, line.QualifierOffset + qualifier.Length, line.End - line.QualifierOffset - qualifier.Length);
            }
            else
                lines.Append(text, line.Start, line.End - line.Start);
            // Lines are compared as lines: a comma list's run, read without its own qualifier and with another
            // declarator's, would read the same with two of its lines joined into one.
            lines.Append('\n');
        }
        return lines.Replace("\r\n", "\n").Replace('\r', '\n').ToString();
    }

    /// <summary>
    /// The member a run describes, as a refusal names it: its name as declared, with its accessor for a property, and
    /// for a variable's run every variable its lines name, once each, since one run may describe several declarators
    /// of a line.
    /// </summary>
    private static List<string> NamesOf(MemberRun run, PropertyAccessor? accessor)
    {
        var member = run.Member!.Value;
        if (accessor is { } which)
            return [$"Property {which} {member.Name}"];
        if (run.Kind != MemberRunKind.Variable)
            return [member.Name];

        var names = new List<string>();
        foreach (var line in run.Lines)
        {
            foreach (var declared in run.Declared)
            {
                // Defensive, the kind test: every line of a variable's run names a variable or constant its line
                // declares, so a procedure matches a qualifier here only on a line declaring a procedure and a
                // variable of one name, which VB6 refuses as an ambiguous name.
                if (declared.Kind != DeclarationKind.Procedure
                    && string.Equals(declared.Name, line.Qualifier, StringComparison.OrdinalIgnoreCase)
                    && !names.Contains(declared.Name))
                    names.Add(declared.Name);
            }
        }
        return names.Count == 0 ? [member.Name] : names;
    }

    /// <summary><paramref name="names"/> as a refusal writes them: "A", "A and B", "A, B and C".</summary>
    private static string Joined(IReadOnlyList<string> names)
    {
        var joined = new StringBuilder(names[0]);
        for (var i = 1; i < names.Count; i++)
            joined.Append(i == names.Count - 1 ? " and " : ", ").Append(names[i]);
        return joined.ToString();
    }

    private static List<string> LinesOf(string text)
    {
        var lines = new List<string>();
        foreach (var line in SourceLines.Split(text))
            lines.Add(line);
        if (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    private static bool EndsWith(List<string> whole, List<string> tail)
    {
        var skip = whole.Count - tail.Count;
        for (var i = 0; i < tail.Count; i++)
        {
            if (!string.Equals(whole[skip + i], tail[i], StringComparison.Ordinal))
                return false;
        }
        return true;
    }
}
