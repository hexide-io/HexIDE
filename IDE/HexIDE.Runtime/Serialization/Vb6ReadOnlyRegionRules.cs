using System;
using System.Collections.Generic;
using HexIDE.Language;

namespace HexIDE.Runtime.Serialization;

/// <summary>VB6's rules for writes to read-only regions.</summary>
public sealed class Vb6ReadOnlyRegionRules : IReadOnlyRegionRules
{
    public ContentReplacement Replace(string current, int prefixLength, string incoming) =>
        GuardedContent.Replace(current, prefixLength, incoming);

    public string? RangeEditRefusal(string before, string after, int prefixLength) =>
        GuardedContent.RangeEditRefusal(before, after, prefixLength);

    public string? MemberNamedBy(string text, int prefixLength, TextRegion region) =>
        GuardedContent.MemberNamedBy(text, prefixLength, region);

    public string? DescribeRegions(string text, int prefixLength, IReadOnlyList<TextRegion> regions) =>
        GuardedContent.DescribeRegions(text, prefixLength, regions);

    public bool HeaderDeclares(string text, int prefixLength, string name) =>
        ReadOnlyRegions.DeclaredNames(text, prefixLength).Contains(name);

    /// <summary>
    /// Whether <paramref name="edit"/> changes a qualifier that the rename owns.
    /// </summary>
    /// <param name="text">The whole buffer.</param>
    /// <param name="prefixLength">The length of the header it was composed with; see <see cref="ReadOnlyRegions.Of"/>.</param>
    /// <param name="changes">Every edit of the answer to this document.</param>
    /// <remarks>
    /// <para>
    /// <b>A qualifier edit is let through when it replaces exactly one attribute line's qualifier, in a run that
    /// describes a member, and the same answer renames that member</b>: it replaces the name on the declaration
    /// above the run with the same new text. For a procedure's run the member is the procedure, whatever the
    /// qualifier said, because VB6 gives the run to the procedure by position (design.md, "Renames (4.4)"). For a
    /// variable run it is the declarator the line's qualifier names, since VB6 binds that line by name; and for a
    /// run a rename by retyping left, it is the one name its line declares.
    /// </para>
    /// <para>
    /// <b>Variables are let through too, which the design paragraph does not say.</b> It names a run describing a
    /// procedure, and "every other edit to a run" refused. Read that way, renaming a described variable from the
    /// bundled server, which sends <c>Attribute Count.VB_VarDescription</c>'s qualifier edit with the
    /// declaration's, would be refused as a whole where it was applied before, though the run still names a name
    /// its line declares afterwards and so still describes it.
    /// </para>
    /// <para>
    /// <b>"Renames the member" is read off the answer, not off a name.</b> The rule this replaced let a qualifier
    /// through whenever it spelled the old name, so renaming <c>Total</c> also rewrote a stale
    /// <c>Attribute Total.</c> line under <c>Halt</c>, a run that is <c>Halt</c>'s. That edit is now refused, and
    /// with it the rename, which is the policy for every edit to a run that the rename does not own. The bundled
    /// server no longer sends it.
    /// </para>
    /// </remarks>
    public bool RenameMayWrite(string text, int prefixLength, IReadOnlyList<TextChange> changes, TextChange edit) =>
        IsQualifierTheRenameOwns(ReadOnlyRegions.MemberRuns(text, prefixLength), changes, edit);

    /// <summary>Whether <paramref name="change"/> is a qualifier of a member that <paramref name="changes"/> renames.</summary>
    private static bool IsQualifierTheRenameOwns(IReadOnlyList<MemberRun> runs, IReadOnlyList<TextChange> changes, TextChange change)
    {
        foreach (var run in runs)
        {
            // DescribesMember is defensive. Only an edit that changes a region is asked about, and an orphan is no
            // region and overlaps no run, so an edit starting in one reaches a region only past its own line, and is
            // then none of its qualifiers: the answer is false either way.
            if (!run.DescribesMember || change.Offset < run.Region.Start || change.Offset >= run.Region.End)
                continue;
            foreach (var line in run.Lines)
            {
                if (line.Qualifier is not { } qualifier || line.QualifierOffset != change.Offset
                    || qualifier.Length != change.Length)
                    continue;

                DeclaredName? member = run.Member;
                if (run.Kind == MemberRunKind.Variable)
                {
                    member = null;
                    foreach (var declared in run.Declared)
                    {
                        if (declared.Kind != DeclarationKind.Procedure
                            && string.Equals(declared.Name, qualifier, StringComparison.OrdinalIgnoreCase))
                            member = declared;
                    }
                }
                if (member is not { } renamed)
                    return false;
                foreach (var other in changes)
                {
                    if (other.Offset == renamed.Offset && other.Length == renamed.Name.Length
                        && string.Equals(other.Text, change.Text, StringComparison.Ordinal))
                        return true;
                }
                return false;
            }
            return false;
        }
        return false;
    }

}
