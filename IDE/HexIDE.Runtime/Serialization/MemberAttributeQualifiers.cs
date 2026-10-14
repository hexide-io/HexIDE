using System;
using System.Collections.Generic;
using System.Text;

namespace HexIDE.Runtime.Serialization;

/// <summary>
/// The qualifiers a save writes into a member's attribute run: the member's name, as its declaration spells it
/// (hexide-io/HexIDE#273 task 4.4).
/// </summary>
/// <remarks>
/// <para>
/// <b>VB6 rewrites these every time it writes a module</b>: at a save of a module it holds as changed, and at an
/// explicit Save or Save As of one it does not, though its Save Project leaves such a module unwritten. It keeps a
/// member's attributes with the member, not with any text, and writes each run afresh, qualified with the member's
/// name in its declared casing: after a name retyped on the declaration, after Replace All, after the whole
/// declaration retyped over a selection, and for a loaded run whose qualifier names nothing, another member, or the
/// member in another case (oracle, "Member attributes": IDE probes C2, C3, C8, V1, B's <c>NamesNothing</c>,
/// <c>NamesOther</c> and <c>LowerCase</c>, and the third session's Q2 and Q3). So a rename by any route ends with
/// the qualifiers VB6 would write, and nothing has to fix them at the moment of the edit. Which writes rewrite is
/// the save's to decide (<c>ProjectService</c>).
/// </para>
/// <para>
/// <b>Two kinds of run are rewritten, and only two</b>, read off <see cref="MemberRun.Kind"/> rather than the
/// text: a <see cref="MemberRunKind.Procedure"/> run, whose qualifiers VB6 never consults, and a
/// <see cref="MemberRunKind.RenamedVariable"/> run, the one a name retyped on a one-name line leaves, which VB6
/// carries to the new name (V1). A <see cref="MemberRunKind.Variable"/> run already names its variables and is
/// left as written, its case included, since VB6's re-casing of a <c>VB_Var</c> qualifier was not measured. An
/// orphan is inert text and is never touched.
/// </para>
/// <para>
/// <b>The renamed kind is a lexical guess, and so this inherits its weaknesses</b> (design.md, "What the module
/// declares" under "Settled while building 4.1"). A <c>VB_Var</c> line naming a parameter, a label, an
/// <c>Enum</c>'s or a <c>Type</c>'s name, or a name declared nowhere at all, under a one-name line, reads as a
/// rename's leftover, and is rewritten to that line's name where VB6 would bind it elsewhere or to nothing. The last
/// is measured: loaded from a file, <c>Attribute Count.VB_VarDescription</c> under <c>Public Tally As Long</c>, with
/// <c>Count</c> declared nowhere, is dropped by VB6's next write of the module, where the same text left by
/// retyping <c>Count</c> in VB6's code window is carried to <c>Tally</c> (V1). Each case needs a line VB6 itself
/// does not write where it stands, which is why it is pinned by tests rather than handled: text alone cannot tell
/// it from the run a rename by retyping leaves.
/// </para>
/// </remarks>
public static class MemberAttributeQualifiers
{
    /// <summary>
    /// The replacements that make every procedure's run, and every run a rename of a one-name variable line left,
    /// name its member as its declaration spells it: in order of offset, and empty when every such run already
    /// does.
    /// </summary>
    /// <param name="text">The whole file.</param>
    /// <param name="prefixLength">
    /// The length of the header the text was composed with, or 0 when the model split nothing off; see
    /// <see cref="ReadOnlyRegions.MemberRuns"/>.
    /// </param>
    /// <remarks>
    /// Compared ordinally, so a qualifier in another case is rewritten to the declaration's, as VB6 re-cases a
    /// procedure's (<c>LowerCase</c>). Each replacement is exactly one line's qualifier, at its
    /// <see cref="AttributeLine.QualifierOffset"/>, which may be on a physical line after the line's first.
    /// </remarks>
    public static IReadOnlyList<TextChange> Rewrites(string text, int prefixLength)
    {
        var rewrites = new List<TextChange>();
        foreach (var run in ReadOnlyRegions.MemberRuns(text, prefixLength))
        {
            if (run.Kind is not (MemberRunKind.Procedure or MemberRunKind.RenamedVariable) || run.Member is not { } member)
                continue;
            foreach (var line in run.Lines)
            {
                if (line.Qualifier is { } qualifier && !string.Equals(qualifier, member.Name, StringComparison.Ordinal))
                    rewrites.Add(new TextChange(line.QualifierOffset, qualifier.Length, member.Name));
            }
        }
        return rewrites;
    }

    /// <summary><paramref name="text"/> with <paramref name="rewrites"/> made, which must be in order of offset.</summary>
    public static string Apply(string text, IReadOnlyList<TextChange> rewrites)
    {
        if (rewrites.Count == 0)
            return text;
        var builder = new StringBuilder(text);
        for (var i = rewrites.Count - 1; i >= 0; i--)
            builder.Remove(rewrites[i].Offset, rewrites[i].Length).Insert(rewrites[i].Offset, rewrites[i].Text);
        return builder.ToString();
    }

    /// <summary>
    /// <paramref name="body"/>, the code a document carries after <paramref name="prefix"/>, with its runs'
    /// qualifiers rewritten; the same instance when nothing changes.
    /// </summary>
    /// <remarks>
    /// Read over the whole file, prefix and body together, and never the body alone: the renamed kind is decided
    /// against every name the module writes, a form's controls in its designer block included, so a stale name
    /// that is a control's would read as a rename's leftover in the body alone and be rewritten.
    /// </remarks>
    public static string Rewrite(string prefix, string body)
    {
        var whole = prefix + body;
        var rewrites = Rewrites(whole, prefix.Length);
        return rewrites.Count == 0 ? body : Apply(whole, rewrites)[prefix.Length..];
    }
}
