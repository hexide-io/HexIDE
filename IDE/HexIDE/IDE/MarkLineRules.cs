using HexIDE.Runtime.Serialization;

namespace HexIDE.IDE;

/// <summary>
/// Which lines of a document may carry a breakpoint, a bookmark, or the target of Run To Cursor and Set Next
/// Statement, for a caller that names lines by number rather than by clicking one.
/// </summary>
/// <remarks>
/// The automation tools are that caller. They are compiled into an executable nothing else references, so the
/// rule lives here, where a test can reach it, and the tools only relay its answer.
/// </remarks>
public static class MarkLineRules
{
    /// <summary>
    /// Why <paramref name="lines"/> cannot be marked in a document, or null when every one can.
    /// </summary>
    /// <param name="text">The document as its code window numbers it.</param>
    /// <param name="display">The document's name as a reply shows it.</param>
    /// <param name="lines">The lines asked for, numbered from <paramref name="first"/>.</param>
    /// <param name="held">
    /// The lines already marked, in the same numbering. A read-only line among them is let through, so that a
    /// caller who reads a document's marks and writes the same set back is never refused for it.
    /// </param>
    /// <param name="first">The number of the file's first line: 0 for bookmarks, 1 for everything else.</param>
    /// <param name="what">What a line is being asked for, in the singular: "breakpoint", "bookmark", "line".</param>
    /// <remarks>
    /// <para>
    /// <b>A line the document does not have</b> was stored, read back as held, and then shown nowhere and hit
    /// never: set_breakpoints("Module1", [-1, 0, 99]) on a two-line module answered that it now had breakpoints
    /// on -1, 0 and 99 (#570). The whole call is refused, rather than the good lines kept, because a caller who
    /// got one line wrong has probably got the numbering wrong, and half a set of marks is harder to notice than
    /// none.
    /// </para>
    /// <para>
    /// <b>A line in a read-only region</b> is refused in the same way (#273 task 3.12). The header and a
    /// member's attribute lines never execute, so a breakpoint there is never hit and Run To Cursor would run to
    /// the end. The reply says where the code starts, because a caller who counted lines from the first line of
    /// code, as this surface did before the code window held the whole file, is wrong by exactly the header.
    /// </para>
    /// </remarks>
    public static string? Refusal(
        CodeWindowText text, string display, IReadOnlyCollection<int> lines, IReadOnlyCollection<int> held,
        int first, string what)
    {
        var lineCount = text.LineCount;
        var last = first + lineCount - 1;

        var outside = lines.Where(l => l < first || l > last).Distinct().Order().ToList();
        if (outside.Count > 0)
            return $"{Listed(outside)} {IsAre(outside)} not a line of {display}, which has {lineCount} " +
                   $"line{(lineCount == 1 ? "" : "s")}: {what}s are numbered {first}..{last}" +
                   $"{(first == 0 ? ", counting from 0" : "")}, from the top of the file. Nothing was changed.";

        var readOnly = lines.Where(l => !held.Contains(l) && text.IsReadOnlyLine(l - first + 1))
            .Distinct().Order().ToList();
        if (readOnly.Count == 0)
            return null;

        var header = text.HeaderLineCount;
        var where = header == 0
            ? "the Attribute lines that describe a member"
            : $"its header ({first}..{first + header - 1}) or the Attribute lines that describe a member";
        // "line" is what Run To Cursor and Set Next Statement ask for, and "no line can go there" says nothing.
        var consequence = what == "line" ? "execution can never stop there" : $"no {what} can go there";
        return $"{Listed(readOnly)} {IsAre(readOnly)} in a read-only part of {display}: {where}. Neither ever " +
               $"runs, so {consequence}" +
               $"{(header == 0 ? "" : $"; its code starts at {first + header}, counting from the top of the file")}. " +
               "Nothing was changed.";
    }

    /// <summary>
    /// The <paramref name="lines"/> that are lines of the document, and those that are not.
    /// </summary>
    /// <remarks>
    /// Range only, and for the sidecar's load, which asks it twice for a sidecar written before the code window
    /// held the whole file: once over a document's code, in the numbering those lines were written in, before the
    /// migration moves them (#273 task 3.17), and once over the whole file for lines already counted that way. The
    /// read-only test is the load's own, made after the move: made in the old numbering it would judge a form's
    /// fifth line of code by the designer block's fifth line.
    /// </remarks>
    public static (IReadOnlyList<int> Kept, IReadOnlyList<int> Dropped) Within(
        CodeWindowText text, IReadOnlyCollection<int> lines, int first)
    {
        var last = first + text.LineCount - 1;
        var kept = new List<int>();
        var dropped = new List<int>();
        foreach (var line in lines)
            (line >= first && line <= last ? kept : dropped).Add(line);
        return (kept, dropped);
    }

    /// <summary>
    /// Stored bookmarks as a caller numbers lines: from 1, as the gutter and every other tool that takes a line
    /// count (#571).
    /// </summary>
    /// <remarks>
    /// The bookmark store counts from 0, as the sidecar has always written it, and the breakpoint store from 1.
    /// The bookmark tools stated 0 and every other line tool 1, so a caller who had just set a breakpoint, or read
    /// a line off a snapshot, put a bookmark one line low; near the header one number was refused by one tool and
    /// taken by the other. The tools now count from 1 and convert here, at their edge, leaving the store and the
    /// files it writes as they were.
    /// </remarks>
    public static int[] BookmarksShown(IEnumerable<int> stored) => stored.Select(l => l + 1).ToArray();

    /// <summary>A caller's bookmark lines, counted from 1, as the store keeps them. See <see cref="BookmarksShown"/>.</summary>
    public static int[] BookmarksStored(IEnumerable<int> shown) => shown.Select(l => l - 1).ToArray();

    private static string Listed(List<int> lines) => string.Join(", ", lines);

    private static string IsAre(List<int> lines) => lines.Count == 1 ? "is" : "are";
}
