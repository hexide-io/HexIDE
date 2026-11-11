using System;
using System.Collections.Generic;
using HexIDE.Language;

namespace HexIDE.Runtime.Serialization;

/// <summary>
/// A VB6 file's read-only regions: its header and every member's attribute run (<see cref="ReadOnlyRegions.Of"/>). A run
/// that describes a member hangs from that member's declaration; an orphaned one is ordinary text, and never reaches
/// the code window.
/// </summary>
public sealed class Vb6ReadOnlyRegionReader : IReadOnlyRegionReader
{
    /// <inheritdoc/>
    public IReadOnlyList<TextRegion> Regions(string text, int prefixLength) => ReadOnlyRegions.Of(text, prefixLength);

    /// <inheritdoc/>
    /// <remarks>
    /// Each run that describes a member (<see cref="ReadOnlyRegions.MemberRuns"/>), keyed by its
    /// <see cref="MemberRunKey"/> and named by the member's declaration as it spells it.
    /// </remarks>
    public IReadOnlyList<HangingRegion> HangingRegions(string text, int prefixLength)
    {
        var hanging = new List<HangingRegion>();
        foreach (var run in ReadOnlyRegions.MemberRuns(text, prefixLength))
        {
            // A run has a member and a key exactly when it describes one; an orphan has neither.
            if (run is { Member: { } member, Key: { } key })
                hanging.Add(new HangingRegion(run.Region, run.DescribedStart, run.Anchor, run.LastContentEnd,
                    new HangingRegionKey(key.Member, key.Ordinal), member.Name));
        }
        return hanging;
    }

    /// <inheritdoc/>
    /// <remarks>A <c>'</c> or a <c>Rem</c> on the member's declaration (<see cref="ReadOnlyRegions.CommentStartOn"/>).</remarks>
    public int CommentStart(string text, HangingRegion region) =>
        ReadOnlyRegions.CommentStartOn(text, region.LineStart, region.Anchor);

    /// <inheritdoc/>
    /// <remarks>An attribute line: one whose first word is <c>Attribute</c> (#1041).</remarks>
    public bool StartsHangingRegion(ReadOnlySpan<char> line) => ReadOnlyRegions.IsAttributeLine(line);

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The first logical line of <paramref name="inserted"/> that declares what the run could describe there (#1039): a
    /// procedure, for a run of procedure attributes, and for a run of variable attributes every variable or constant its
    /// lines name. A pasted declaration may be preceded by comments or blank lines, and the run stays under the
    /// declaration rather than under the first of them.
    /// </para>
    /// <para>
    /// Logical lines are joined by the reader's own continuation rule, an underscore after whitespace. A last line with a
    /// terminator is followed by an empty one, as the code window counts lines, so a declaration whose last physical line
    /// continues into the text's final line break is still read whole.
    /// </para>
    /// </remarks>
    public int? LineToHangFrom(string text, HangingRegion region, string inserted)
    {
        var attributes = ReadOnlyRegions.AttributeLinesIn(text, region.Region.Start, region.LastContentEnd);
        // A run that describes a member is all procedure attributes or all variable ones.
        var procedure = attributes.TrueForAll(a => a.Kind == AttributeLineKind.Procedure);

        var start = 0;
        var pos = 0;
        while (true)
        {
            var (end, next) = SourceLines.LineAt(inserted, pos);
            var terminated = next > end;
            if (!terminated || !ReadOnlyRegions.EndsWithContinuation(inserted.AsSpan(pos, end - pos)))
            {
                if (CouldDescribe(ReadOnlyRegions.NamesDeclaredOn(inserted, start, end)))
                    return end;
                start = next;
            }
            if (!terminated)
                return null;
            pos = next;
        }

        bool CouldDescribe(IReadOnlyList<DeclaredName> declared)
        {
            if (procedure)
            {
                foreach (var d in declared)
                {
                    if (d.Kind == DeclarationKind.Procedure)
                        return true;
                }
                return false;
            }
            foreach (var a in attributes)
            {
                var named = false;
                foreach (var d in declared)
                {
                    if (d.Kind is DeclarationKind.Variable or DeclarationKind.Constant
                        && string.Equals(d.Name, a.Qualifier, StringComparison.OrdinalIgnoreCase))
                        named = true;
                }
                if (!named)
                    return false;
            }
            return true;
        }
    }
}
