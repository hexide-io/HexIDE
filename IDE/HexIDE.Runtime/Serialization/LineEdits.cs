using System;
using System.Collections.Generic;
using System.Text;

namespace HexIDE.Runtime.Serialization;

/// <summary>A replacement of <see cref="Length"/> characters at <see cref="Offset"/> with <see cref="Text"/>.</summary>
public readonly record struct TextChange(int Offset, int Length, string Text);

/// <summary>What a reduction kept and what it had to leave out.</summary>
/// <param name="Changes">The changes to apply, in ascending order of offset and not overlapping.</param>
/// <param name="DroppedLines">
/// How many lines of the original text a dropped change would have rewritten. Zero when nothing touched a
/// read-only region.
/// </param>
public readonly record struct Reduction(IReadOnlyList<TextChange> Changes, int DroppedLines);

/// <summary>
/// Turns a rewrite of a buffer into the changes to the lines it actually alters, less any that would change a
/// read-only region (hexide-io/HexIDE#273 phase 3, the formatting row of the design record's writer policy).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a rewrite has to be taken apart.</b> A language server is free to answer formatting with a single
/// edit replacing the whole document, and the bundled one does. That edit re-indents every line of a designer
/// block to column zero. Applied as sent, it rewrites the header, and because a code window splits its body
/// off by the header's length, it also cuts into the code (measured: Format on Save lost a form's whole
/// attribute block and sixteen lines below it). The policy is that the edit is reduced to the lines it
/// changes, and the changes inside a read-only region are dropped.
/// </para>
/// <para>
/// <b>Lines are compared by their text, without their terminators.</b> A server may answer in <c>\n</c> for
/// a buffer in <c>\r\n</c>. That is not a change to any line, and treating it as one would rewrite the whole
/// document, and the header with it. The text written back uses the buffer's own terminator.
/// </para>
/// </remarks>
public static class LineEdits
{
    /// <summary>
    /// Past this many differing lines the diff stops looking for the smallest alignment, and treats what is
    /// left between the unchanged top and bottom as one change. That change is then dropped whole if it
    /// touches a region, which keeps the header safe and at worst leaves a document unformatted.
    /// </summary>
    private const int MaxEditDistance = 2000;

    /// <summary>
    /// The changes that turn <paramref name="before"/> into <paramref name="after"/>, line by line, less any
    /// change that would alter one of <paramref name="regions"/>.
    /// </summary>
    public static Reduction Reduce(string before, string after, IReadOnlyList<TextRegion> regions)
    {
        var old = Split(before);
        var @new = Split(after);
        var newline = before.Contains("\r\n", StringComparison.Ordinal) ? "\r\n"
            : before.Contains('\n') ? "\n"
            : after.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        var kept = new List<TextChange>();
        var dropped = 0;
        foreach (var (a, b, c, d) in Hunks(old, @new))
        {
            var change = ToChange(before, old, @new, a, b, c, d, newline);
            // A hunk is whole lines. One whose replacement ends with a line break leaves a line break where
            // the hunk ends, so a member run starting there still hangs from a line: only membership decides.
            // One that ends without one, which is what deleting lines is, removes the break a run hangs from.
            var keepsItsLineBreak = change.Text.EndsWith('\n');
            if (keepsItsLineBreak
                    ? !ReadOnlyRegions.Touches(regions, change.Offset, change.Length)
                    : !ReadOnlyRegions.Changes(regions, change.Offset, change.Length))
            {
                kept.Add(change);
                continue;
            }

            // A run that pairs old and new lines one for one -- which is what re-indenting is -- can be taken
            // line by line, so the code beside a region is still formatted and only the region's own lines are
            // left alone. Only a run that adds or removes lines has to go whole.
            if (b - a == d - c)
            {
                for (var i = 0; i < b - a; i++)
                {
                    var line = old[a + i];
                    var replacement = @new[c + i];
                    // Membership, not Changes: the line is rewritten with its own terminator put back, so a
                    // declaration line above an attribute run is re-indented without joining the run to it.
                    if (ReadOnlyRegions.Touches(regions, line.Start, line.Next - line.Start))
                    {
                        dropped++;
                        continue;
                    }
                    if (line.Key == replacement.Key)
                        continue;
                    // The line keeps its own terminator; only a last line gaining or losing one changes it.
                    var terminator = !replacement.Terminated ? ""
                        : line.Terminated ? before[(line.Start + line.Content.Length)..line.Next]
                        : newline;
                    kept.Add(new TextChange(line.Start, line.Next - line.Start, replacement.Content + terminator));
                }
                continue;
            }

            dropped += Math.Max(b - a, 1);
        }
        return new Reduction(kept, dropped);
    }

    /// <summary>
    /// <paramref name="text"/> with <paramref name="changes"/> applied. They must be in ascending order and
    /// must not overlap, as <see cref="Reduce"/> returns them.
    /// </summary>
    public static string Apply(string text, IReadOnlyList<TextChange> changes)
    {
        var builder = new StringBuilder(text);
        for (var i = changes.Count - 1; i >= 0; i--)
        {
            var change = changes[i];
            builder.Remove(change.Offset, change.Length).Insert(change.Offset, change.Text);
        }
        return builder.ToString();
    }

    /// <summary>One line: its text without terminator, and where it sits in the original string.</summary>
    private readonly record struct Line(string Content, int Start, int Next, bool Terminated)
    {
        /// <summary>
        /// What two lines are compared by: the text, and, for the one line that can lack a terminator (the
        /// last), whether it has one. That makes a dropped or added final newline a change to the last line,
        /// while a line ending that is merely <c>\n</c> rather than <c>\r\n</c> stays no change at all.
        /// </summary>
        public string Key => Terminated ? Content : Content + "\0";
    }

    private static List<Line> Split(string text)
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
            lines.Add(new Line(text[pos..end], pos, next, newline >= 0));
            pos = next;
        }
        return lines;
    }

    /// <summary>
    /// The replacement of old lines <c>[a, b)</c> by new lines <c>[c, d)</c>, as a change to the original text.
    /// </summary>
    private static TextChange ToChange(string before, List<Line> old, List<Line> @new, int a, int b, int c, int d, string newline)
    {
        var text = new StringBuilder();
        if (b > a)
        {
            // Replace whole lines, terminators included, and give the new lines the buffer's terminator. Only
            // the document's last line can lack one, and it follows the rewrite, because comparing by Key made a
            // changed final newline part of the change.
            var start = old[a].Start;
            var end = old[b - 1].Next;
            for (var k = c; k < d; k++)
            {
                text.Append(@new[k].Content);
                if (k < d - 1 || @new[k].Terminated)
                    text.Append(newline);
            }
            return new TextChange(start, end - start, text.ToString());
        }

        // A pure insertion goes in front of old line a. At the very end of a text with no final newline, the
        // new lines have to start on a line of their own.
        if (a < old.Count)
        {
            for (var k = c; k < d; k++)
                text.Append(@new[k].Content).Append(newline);
            return new TextChange(old[a].Start, 0, text.ToString());
        }

        var needsBreak = old.Count > 0 && !old[^1].Terminated;
        if (needsBreak)
            text.Append(newline);
        for (var k = c; k < d; k++)
        {
            text.Append(@new[k].Content);
            if (k < d - 1 || @new[k].Terminated)
                text.Append(newline);
        }
        return new TextChange(before.Length, 0, text.ToString());
    }

    /// <summary>
    /// The runs where the two line lists differ, as <c>(a, b, c, d)</c>: old lines <c>[a, b)</c> became new
    /// lines <c>[c, d)</c>. In ascending order.
    /// </summary>
    private static List<(int A, int B, int C, int D)> Hunks(List<Line> old, List<Line> @new)
    {
        var top = 0;
        while (top < old.Count && top < @new.Count && old[top].Key == @new[top].Key)
            top++;
        var bottom = 0;
        while (bottom < old.Count - top && bottom < @new.Count - top
               && old[old.Count - 1 - bottom].Key == @new[@new.Count - 1 - bottom].Key)
            bottom++;

        var n = old.Count - top - bottom;
        var m = @new.Count - top - bottom;
        var hunks = new List<(int, int, int, int)>();
        if (n == 0 && m == 0)
            return hunks;

        var script = Diff(old, top, n, @new, top, m);
        if (script is null)
        {
            hunks.Add((top, top + n, top, top + m));
            return hunks;
        }

        // Walk the alignment, collecting each maximal run of non-matching lines.
        int i = 0, j = 0;
        foreach (var (x, y) in script)
        {
            if (x > i || y > j)
                hunks.Add((top + i, top + x, top + j, top + y));
            i = x + 1;
            j = y + 1;
        }
        if (i < n || j < m)
            hunks.Add((top + i, top + n, top + j, top + m));
        return hunks;
    }

    /// <summary>
    /// The matching line pairs of a shortest edit script between the two ranges (Myers, 1986), as indices
    /// relative to each range's start. Null when the ranges differ by more than <see cref="MaxEditDistance"/>.
    /// </summary>
    private static List<(int X, int Y)>? Diff(List<Line> a, int aStart, int n, List<Line> b, int bStart, int m)
    {
        var max = Math.Min(n + m, MaxEditDistance);
        var offset = max;
        var v = new int[2 * max + 2];

        // Each round's starting state, kept only over the diagonals that round can read: k-1 and k+1 for k in
        // [-d, d], and never k-1 at k = -d. That is (d+1)^2 integers in all rather than a full array per round.
        var trace = new List<int[]>();

        for (var d = 0; d <= max; d++)
        {
            trace.Add(v[(offset - d)..(offset + d + 2)]);
            for (var k = -d; k <= d; k += 2)
            {
                var x = k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1])
                    ? v[offset + k + 1]
                    : v[offset + k - 1] + 1;
                var y = x - k;
                while (x < n && y < m && a[aStart + x].Key == b[bStart + y].Key)
                {
                    x++;
                    y++;
                }
                v[offset + k] = x;
                if (x >= n && y >= m)
                    return Backtrack(trace, d, n, m);
            }
        }
        return null;
    }

    private static List<(int X, int Y)> Backtrack(List<int[]> trace, int depth, int n, int m)
    {
        var matches = new List<(int, int)>();
        int x = n, y = m;
        for (var d = depth; d > 0; d--)
        {
            // trace[d] holds diagonals [-d, d+1] of the state round d started from.
            var v = trace[d];
            int At(int diagonal) => v[diagonal + d];

            var k = x - y;
            var prevK = k == -d || (k != d && At(k - 1) < At(k + 1)) ? k + 1 : k - 1;
            var prevX = At(prevK);
            var prevY = prevX - prevK;
            while (x > prevX && y > prevY)
            {
                x--;
                y--;
                matches.Add((x, y));
            }
            x = prevX;
            y = prevY;
        }
        while (x > 0 && y > 0)
        {
            x--;
            y--;
            matches.Add((x, y));
        }
        matches.Reverse();
        return matches;
    }
}
