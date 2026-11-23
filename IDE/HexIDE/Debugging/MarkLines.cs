using System;

namespace HexIDE.Debugging;

internal static class MarkLines
{
    // #1053: reproduces docs/vb6-fidelity-oracle/a-breakpoint-through-edits-above-it.md.
    // Other edits follow the measured row with the same shape; they are not further VB6 measurements.
    // All offsets describe the document before the change. End excludes the line break, Next includes it.
    internal static int? Remap(int line, int offset, int removalLength, int removedBreaks, int insertedBreaks,
        Func<int, (int Begin, int Statement, int End, int Next)> readLine) =>
        Remap(line, offset, removalLength, removedBreaks, insertedBreaks, readLine, bookmark: false);

    internal static int? RemapBookmark(int line, int offset, int removalLength, int removedBreaks, int insertedBreaks,
        Func<int, (int Begin, int Statement, int End, int Next)> readLine) =>
        Remap(line, offset, removalLength, removedBreaks, insertedBreaks, readLine, bookmark: true);

    private static int? Remap(int line, int offset, int removalLength, int removedBreaks, int insertedBreaks,
        Func<int, (int Begin, int Statement, int End, int Next)> readLine, bool bookmark)
    {
        var marked = readLine(line);
        var removedEnd = offset + removalLength;
        if (line > 1 && offset <= readLine(line - 1).End && removedEnd > marked.Begin)
        {
            // #5334: In the measured join, VB6 kept the bookmark with the surviving statement.
            return bookmark && removedEnd <= marked.Statement ? line + insertedBreaks - removedBreaks : null;
        }
        if (offset <= marked.Statement && marked.Next > marked.End && removedEnd >= marked.Next)
            return null;
        return offset <= marked.Statement ? line + insertedBreaks - removedBreaks : line;
    }
}
