using System.Collections.Generic;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Serialization;

/// <summary>
/// A document's text as its code window numbers it: the whole file, header included, so line 1 is the file's
/// first line (hexide-io/HexIDE#273 task 3.16).
/// </summary>
/// <param name="Text">The whole file: an open editor's buffer, or the model's composition of it.</param>
/// <param name="PrefixLength">
/// The length of the header the text was composed with, which is how its read-only regions are found. See
/// <see cref="ReadOnlyRegions.Of"/>.
/// </param>
/// <remarks>
/// Every line number the IDE takes or gives for a code window counts in this text: breakpoints, bookmarks, the
/// current statement, the Call Stack, and the automation and add-in surfaces. Before the buffer held the whole
/// file, the range checks behind <c>set_breakpoints</c> and the sidecar's load counted the model's code
/// section, which is shorter by the header — so a mark near the end of a form was refused, or dropped at load,
/// although the code window showed it on a line that exists.
/// </remarks>
public readonly record struct CodeWindowText(string Text, int PrefixLength)
{
    /// <summary>The document's whole file as the model composes it, for a document with no code window open.</summary>
    /// <remarks>
    /// An open window's buffer may hold edits the model has not been given yet, and its line numbers are the
    /// ones the developer sees, so a caller that has the window asks it instead.
    /// </remarks>
    public static CodeWindowText Of(DocumentIdentity document) =>
        document.Module is { } module
            ? new CodeWindowText(FormCodeText.WholeFile(module), FormCodeText.Prefix(module).Length)
            : new CodeWindowText(FormCodeText.WholeFile(document.Form!), FormCodeText.Prefix(document.Form!).Length);

    /// <summary>
    /// How many lines the code window shows. A final line break starts one more line, as an editor shows it.
    /// </summary>
    /// <remarks>Counted by <c>'\n'</c>, never the host's newline, so CRLF and LF text count alike on every host.</remarks>
    public int LineCount
    {
        get
        {
            var count = 1;
            foreach (var c in Text)
            {
                if (c == '\n')
                    count++;
            }
            return count;
        }
    }

    /// <summary>The number of lines the header occupies, which is also the line before the code starts.</summary>
    public int HeaderLineCount => ReadOnlyRegions.HeaderLineCount(Text, PrefixLength);

    /// <summary>The read-only regions of the text: its header and every member's attribute run.</summary>
    public IReadOnlyList<TextRegion> Regions => ReadOnlyRegions.Of(Text, PrefixLength);

    /// <summary>
    /// True when the 1-based <paramref name="line"/> is in a read-only region, where no breakpoint or bookmark
    /// may be set: such a line never executes, and a folded header is one visible line standing for many.
    /// </summary>
    public bool IsReadOnlyLine(int line) => ReadOnlyRegions.IsReadOnlyLine(Text, Regions, line);
}
