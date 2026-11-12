using System.Collections.Generic;
using System.Linq;
using HexIDE.Language;

namespace HexIDE.Runtime.Serialization;

/// <summary>
/// A document's text as its code window numbers it: the whole file, header included, so line 1 is the file's
/// first line (hexide-io/HexIDE#273 task 3.16).
/// </summary>
/// <param name="Text">The whole file: an open editor's buffer, or the model's composition of it.</param>
/// <param name="PrefixLength">
/// The length of the prefix the text was composed with.
/// </param>
/// <param name="Regions">The reader's read-only regions for this text and prefix.</param>
/// <remarks>
/// Every line number the IDE takes or gives for a code window counts in this text: breakpoints, bookmarks, the
/// current statement, the Call Stack, and the automation and add-in surfaces. Before the buffer held the whole
/// file, the range checks behind <c>set_breakpoints</c> and the sidecar's load counted the model's code
/// section, which is shorter by the header — so a mark near the end of a form was refused, or dropped at load,
/// although the code window showed it on a line that exists.
/// </remarks>
public readonly record struct CodeWindowText(string Text, int PrefixLength, IReadOnlyList<TextRegion> Regions)
{
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
    public int HeaderLineCount
    {
        get
        {
            var end = LeadingRegion?.End ?? 0;
            var lines = Text[..end].Count(c => c == '\n');
            return end > 0 && Text[end - 1] != '\n' ? lines + 1 : lines;
        }
    }

    /// <summary>The region at the start of the text that hangs from nothing, when there is one.</summary>
    public TextRegion? LeadingRegion => Regions.Where(region => region.Start == 0 && region.Anchor is null)
        .Cast<TextRegion?>().FirstOrDefault();

    /// <summary>
    /// True when the 1-based <paramref name="line"/> is in a read-only region, where no breakpoint or bookmark
    /// may be set: such a line never executes, and a folded header is one visible line standing for many.
    /// </summary>
    public bool IsReadOnlyLine(int line) => ReadOnlyRegions.IsReadOnlyLine(Text, Regions, line);
}
