using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Forms;

/// <summary>
/// What typing may change in a code window: nothing at all while the document is held read-only as a whole,
/// and otherwise everything but its header and its members' attribute runs (hexide-io/HexIDE#273 task 3.7).
/// And the two rules that keep a run with the line it describes (tasks 4.5 and 4.5a): where a line break typed
/// at that line's end goes, and how far a deletion of that line reaches.
/// </summary>
/// <remarks>
/// <para>
/// <b>One provider carries both gates, because the editor has room for only one.</b> Binding
/// <c>TextEditor.IsReadOnly</c> replaces the text area's section provider outright, so a form held
/// read-only as a whole would lose the region rules the moment it became editable again, and vice versa.
/// Both verdicts are read live on every call, so a reload that flips either one takes effect at the next
/// keystroke with nothing to re-install.
/// </para>
/// <para>
/// <b>The whole-document gate is not a region.</b> It refuses typing and nothing else. Breakpoints and Find
/// ask <see cref="ViewModels.CodeEditorViewModel.IsReadOnlyRegion"/>, which never consults it.
/// </para>
/// <para>
/// <b>A deletion can only be narrowed here, never widened.</b> AvaloniaEdit's
/// <c>TextArea.GetDeletableSegments</c> throws if a provider returns anything outside the span it was asked
/// about. So "deleting a member takes its attribute run with it" is two halves (task 4.5a): the window widens
/// the request before the library asks (<see cref="WidenDeletion"/>), on each route that deletes, and the
/// provider does not carve out a run the request already takes (<see cref="Takes"/>). The library then removes
/// the line and its run as one change, which is one undo step.
/// </para>
/// <para>
/// <b>The line a run describes is its logical line</b>, <see cref="MemberRun.DescribedStart"/> to its anchor, as
/// 4.1's reader decides it, and not the physical line above the run, which is what this read until task 4.5a. The
/// two differ for a continued declaration only, and the difference is what deleting part of one does. Deleting
/// its last physical line no longer takes the run: what is left above it is still the declaration, which the
/// developer is part-way through rewriting, and the run stays under it, describing it while it still declares the
/// member and an orphan, kept as text, if it no longer does. Only a deletion of every line of it takes the run.
/// </para>
/// </remarks>
internal sealed class CodeWindowReadOnlySections : TextSegmentReadOnlySectionProvider<TextSegment>
{
    private readonly TextDocument document;
    private readonly Func<bool> wholeDocumentReadOnly;
    private readonly Func<IReadOnlyList<TextRegion>> regions;
    private readonly Func<IReadOnlyList<MemberRun>> runs;

    public CodeWindowReadOnlySections(TextDocument document, Func<bool> wholeDocumentReadOnly,
        Func<IReadOnlyList<TextRegion>> regions, Func<IReadOnlyList<MemberRun>> runs)
        : base(document)
    {
        this.document = document;
        this.wholeDocumentReadOnly = wholeDocumentReadOnly;
        this.regions = regions;
        this.runs = runs;
    }

    /// <summary>
    /// False inside a region, including at its first character — the stock provider allows insertion at
    /// both edges of a segment, which would let text land above the top of the file or between a
    /// declaration and its attributes. True at a region's end, which is the start of the next line.
    /// </summary>
    public override bool CanInsert(int offset)
    {
        if (wholeDocumentReadOnly())
            return false;
        foreach (var region in regions())
            if (region.Touches(offset, 0))
                return false;
        return true;
    }

    /// <summary>
    /// <paramref name="segment"/> minus every protected span it overlaps, in order.
    /// </summary>
    /// <remarks>
    /// A member's protected span starts at the terminator of the line it describes, not at the run itself:
    /// deleting that terminator would join the first attribute line onto the declaration. That start is the
    /// region's <see cref="TextRegion.EditStart"/>, which every other writer tests through
    /// <see cref="TextRegion.Changes"/>, so typing and the write path cannot drift apart again. The span is
    /// dropped altogether when the deletion takes the run with its line (<see cref="Takes"/>).
    /// </remarks>
    public override IEnumerable<ISegment> GetDeletableSegments(ISegment segment)
    {
        if (wholeDocumentReadOnly())
            return [];

        var result = new List<ISegment>();
        var cursor = segment.Offset;
        var end = segment.EndOffset;
        foreach (var span in ProtectedSpans(segment))
        {
            if (span.End <= cursor) continue;
            if (span.Start >= end) break;
            if (span.Start > cursor)
                result.Add(new SimpleSegment(cursor, span.Start - cursor));
            cursor = Math.Max(cursor, span.End);
        }
        if (cursor < end)
            result.Add(new SimpleSegment(cursor, end - cursor));
        return result;
    }

    /// <summary>The header first, then each member's run, in the order of the text.</summary>
    private IEnumerable<(int Start, int End)> ProtectedSpans(ISegment segment)
    {
        // The header is the one region with no anchor: it hangs from nothing above it, and it is above every run.
        foreach (var region in regions())
        {
            if (region.Anchor is null)
                yield return (region.Start, region.End);
        }
        foreach (var run in runs())
        {
            if (!run.DescribesMember || Takes(run, segment.Offset, segment.EndOffset))
                continue;
            yield return (run.Region.EditStart, run.Region.End);
        }
    }

    /// <summary>
    /// True when a deletion of <c>[start, end)</c> takes <paramref name="run"/> with the line it describes: it
    /// removes all of that line's text and all of the run's (task 4.5a).
    /// </summary>
    /// <remarks>
    /// <para>
    /// To the run's last line's text and not its terminator, which is what <see cref="WidenDeletion"/> widens a
    /// deletion of the line's text alone to. That terminator is left as the end of the line the deletion empties,
    /// which is the developer's line break and was never the run's: folded, the declaration's row ends with it.
    /// </para>
    /// <para>
    /// <b>A replacement is asked the same question</b>, because the library asks this provider what typing and a paste
    /// may delete exactly as it asks for a deletion. So text typed over the line and the run's text, the run expanded,
    /// replaces both, as 3.7 already let it once the selection reached the run's last terminator (#273 task 4.5a
    /// review). A replacement of the line's text alone takes nothing: the run stays, for whatever the new line declares.
    /// </para>
    /// </remarks>
    internal bool Takes(MemberRun run, int start, int end) =>
        run.DescribesMember && start <= TextStart(run) && end >= run.LastContentEnd;

    /// <summary>
    /// What a deletion of <paramref name="length"/> characters at <paramref name="offset"/> removes in this code
    /// window: the same span, reaching on over the attribute run of every line whose whole text it removes, so that
    /// the run goes with its line (task 4.5a). The span itself when it removes no such line.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>For the routes that delete, and never for one that replaces.</b> Delete, Backspace, Cut and Ctrl+D ask
    /// this before the library asks the provider. Typing or pasting over a selected declaration does not: that is
    /// a replacement, the run stays, and it describes whatever the new line declares, which is what VB6 does with
    /// it (IDE probe C8).
    /// </para>
    /// <para>
    /// <b>"The whole text" is everything but the line's leading and trailing whitespace</b>, from the first line of
    /// a continued declaration to the anchor. A line left holding only whitespace declares nothing, so a run left
    /// under it would be an orphan, and a selection made from the first character after the indent, where Home
    /// puts the caret, would leave exactly that.
    /// </para>
    /// <para>
    /// <b>How far it reaches depends on whether the line's break was asked for.</b> A deletion ending at or before
    /// the anchor deletes the line's text and leaves its row: it reaches the end of the run's last line, whose
    /// terminator is left to end the emptied line. One ending past the anchor, as Ctrl+D and a whole-line Cut do,
    /// deletes the line, and reaches past the run's last terminator too. Either way the result is what the same
    /// deletion would leave were the run not there, trailing whitespace aside: one ending at the end of the line's
    /// text also takes the spaces or tabs between there and the anchor, which with no run there it would leave on the
    /// row (#273 task 4.5a review).
    /// </para>
    /// </remarks>
    internal (int Offset, int Length) WidenDeletion(int offset, int length)
    {
        if (length <= 0 || wholeDocumentReadOnly())
            return (offset, length);

        var end = offset + length;
        foreach (var run in runs())
        {
            // Only saves work: this run's line, and every later one's, starts at or after the deletion's end, and a
            // described line has text, so the deletion cannot reach the end of it, which the test below wants
            // (equivalent under mutation).
            if (run.DescribedStart >= end)
                break;
            if (!run.DescribesMember || offset > TextStart(run) || end < TextEnd(run))
                continue;
            // A deletion ending at or before the anchor ends before the run, so the run's last line's text end is
            // always the further; one past the anchor may reach beyond the run, and keeps its own end then.
            end = end <= run.Anchor
                ? run.LastContentEnd
                : Math.Max(end, run.Region.End);
        }
        return (offset, end - offset);
    }

    /// <summary>
    /// True when a deletion of <paramref name="length"/> characters at <paramref name="offset"/>, widened as the
    /// window widens it (<see cref="WidenDeletion"/>), would still change a read-only region: the header, or a run
    /// it does not take whole with its line. The question automation's <c>press_key</c> asks of a key that deletes,
    /// so that it refuses what a person's key could not do and lets through what it does (task 4.5a).
    /// </summary>
    internal bool DeletionChangesARegion(int offset, int length)
    {
        var (start, widened) = WidenDeletion(offset, length);
        foreach (var region in regions())
        {
            if (region.Anchor is null && region.Changes(start, widened))
                return true;
        }
        foreach (var run in runs())
        {
            if (run.DescribesMember && !Takes(run, start, start + widened) && run.Region.Changes(start, widened))
                return true;
        }
        return false;
    }

    /// <summary>
    /// The member's run that hangs from the line <paramref name="offset"/> ends: the one whose anchor is at
    /// <paramref name="offset"/>, or after it with only spaces and tabs between. Null when there is none.
    /// </summary>
    /// <remarks>
    /// The whitespace is let in because Enter treats a caret before trailing whitespace as the end of the line,
    /// and a line break put there would leave the run hanging from a line of nothing but that whitespace, which
    /// declares nothing. A trailing comment is not let in, so a line break before one still leaves the run under the
    /// comment; which way to place it is an open question in the design record (#273 task 4.5 review).
    /// </remarks>
    internal MemberRun? RunHangingFrom(int offset)
    {
        foreach (var run in runs())
        {
            if (run.Anchor < offset)
                continue;
            // Only saves work: a later run's line starts past offset, so the line break before it stands between
            // offset and its anchor, and the blanks test below refuses it (equivalent under mutation).
            if (run.DescribedStart > offset)
                break;
            if (run.DescribesMember && OnlyBlanks(offset, run.Anchor))
                return run;
        }
        return null;
    }

    /// <summary>
    /// Where text holding a line break goes when it would replace <c>[offset, offset + length)</c> and so land at
    /// the end of a line a member's run describes (task 4.5): the text before its first line break in place of
    /// that span, and the rest after the run, so the run stays under its line. Null when the text has no line
    /// break, or the span ends anywhere else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>As if the run were not there.</b> Folded, the run is part of its declaration's row, and the box marks its
    /// place. Whatever the text would have put on the next row it now puts on the row after the run, and what it
    /// would have left on the declaration's row stays there. So Enter's line break makes a line of its own after
    /// the run, and a paste of several lines joins its first to the declaration, as it would without one. An insertion
    /// whose nonblank first line carries an Attribute line instead goes whole after the run (#1041), and a replacement
    /// of the whole declaration keeps the run under the first compatible logical declaration it pastes. Trailing
    /// whitespace aside: spaces or tabs between the span's end and the anchor stay on the declaration's row, where
    /// with no run there the line break would carry them to the next (#273 task 4.5 review).
    /// </para>
    /// <para>
    /// <b>In the document's newline</b>: every line break in the text is written as the described line's own
    /// terminator, as a paste is written by the library. A run is preceded by a line break, so there is always one
    /// to copy.
    /// </para>
    /// <para>
    /// <b>Only a span the provider would replace whole.</b> One reaching into the header or into another run is
    /// carved by the library, and left to it: there the line break would be put wherever the carving leaves it, as
    /// it always was, and no line in this file could be placed by a rule as plain as this one.
    /// </para>
    /// </remarks>
    internal LineBreakPastRun? PlaceLineBreak(int offset, int length, string text)
    {
        if (wholeDocumentReadOnly() || text.AsSpan().IndexOfAny('\r', '\n') < 0)
            return null;
        if (RunHangingFrom(offset + length) is not { } run)
            return null;
        if (length > 0
                ? GetDeletableSegments(new SimpleSegment(offset, length)).ToList() is not [var whole]
                  || whole.Offset != offset || whole.Length != length
                : !CanInsert(offset))
            return null;

        var anchorLine = document.GetLineByOffset(run.Anchor);
        var newline = document.GetText(anchorLine.EndOffset, anchorLine.DelimiterLength);
        var normalized = TextUtilities.NormalizeNewLines(text, newline);
        var lineBreak = normalized.AsSpan().IndexOfAny('\r', '\n');
        var nextLine = normalized.AsSpan(lineBreak + newline.Length);
        var nextBreak = nextLine.IndexOfAny('\r', '\n');
        if (nextBreak >= 0)
            nextLine = nextLine[..nextBreak];
        nextLine = nextLine.TrimStart();
        if (length == 0 && !normalized.AsSpan(0, lineBreak).Trim().IsEmpty
            && nextLine.StartsWith("Attribute", StringComparison.OrdinalIgnoreCase)
            && (nextLine.Length == "Attribute".Length
                || !(char.IsLetterOrDigit(nextLine["Attribute".Length]) || nextLine["Attribute".Length] == '_')))
        {
            // Keep a pasted declaration with its own run, rather than merging it into this member's (#1041).
            var pasted = normalized.EndsWith(newline, StringComparison.Ordinal) ? normalized[..^newline.Length] : normalized;
            return new LineBreakPastRun(run, offset, length, string.Empty, run.LastContentEnd, newline + pasted);
        }
        if (offset <= TextStart(run))
        {
            // A whole declaration may be preceded by comments or blank lines (#1039). Keep its run under the
            // first logical line it can describe, using the same continuation rule as the member reader.
            var pasted = new TextDocument(normalized);
            var start = 0;
            foreach (var line in pasted.Lines)
            {
                var content = pasted.GetText(line).AsSpan().TrimEnd();
                if (line.NextLine is not null && content.Length > 1 && content[^1] == '_' && char.IsWhiteSpace(content[^2]))
                    continue;
                var declared = ReadOnlyRegions.NamesDeclaredOn(normalized, start, line.EndOffset);
                var compatible = run.Kind == MemberRunKind.Procedure
                    ? declared.Any(d => d.Kind == DeclarationKind.Procedure)
                    : run.Lines.All(a => declared.Any(d => d.Kind is DeclarationKind.Variable or DeclarationKind.Constant
                        && string.Equals(d.Name, a.Qualifier, StringComparison.OrdinalIgnoreCase)));
                if (compatible)
                {
                    lineBreak = line.EndOffset;
                    break;
                }
                start = line.Offset + line.TotalLength;
            }
        }
        return new LineBreakPastRun(run, offset, length, normalized[..lineBreak], run.LastContentEnd, normalized[lineBreak..]);
    }

    /// <summary>The first character of <paramref name="run"/>'s described line that is not a space or a tab.</summary>
    private int TextStart(MemberRun run)
    {
        var at = run.DescribedStart;
        while (at < run.Anchor && document.GetCharAt(at) is ' ' or '\t')
            at++;
        return at;
    }

    /// <summary>Just past the last character of <paramref name="run"/>'s described line that is not a space or a tab.</summary>
    private int TextEnd(MemberRun run)
    {
        var at = run.Anchor;
        while (at > run.DescribedStart && document.GetCharAt(at - 1) is ' ' or '\t')
            at--;
        return at;
    }

    private bool OnlyBlanks(int from, int to)
    {
        for (var i = from; i < to; i++)
        {
            if (document.GetCharAt(i) is not (' ' or '\t'))
                return false;
        }
        return true;
    }
}

/// <summary>
/// Where <see cref="CodeWindowReadOnlySections.PlaceLineBreak"/> puts text that would have landed at the end of a
/// line a member's run describes (#273 task 4.5).
/// </summary>
/// <param name="Run">The run, as the text stood.</param>
/// <param name="Offset">The start of the span the text replaces.</param>
/// <param name="Length">The length of the span the text replaces.</param>
/// <param name="Head">What replaces that span: through the first compatible declaration for a whole-line replacement,
/// otherwise up to the first line break, perhaps nothing.</param>
/// <param name="TailAt">Where the rest goes, in the text as it stood: the end of the run's last line's text.</param>
/// <param name="Tail">The rest, including its leading line break; empty when the declaration ends the text.</param>
internal sealed record LineBreakPastRun(MemberRun Run, int Offset, int Length, string Head, int TailAt, string Tail)
{
    /// <summary>Where the tail starts once both parts are written.</summary>
    public int TailStart => TailAt - Length + Head.Length;

    /// <summary>Where the caret ends: past the tail, or past the head when no tail was pasted.</summary>
    public int End => Tail.Length == 0 ? Offset + Head.Length : TailStart + Tail.Length;
}
