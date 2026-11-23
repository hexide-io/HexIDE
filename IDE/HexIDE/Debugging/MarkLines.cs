using System;

namespace HexIDE.Debugging;

internal static class MarkLines
{
    // #1053: reproduces docs/vb6-fidelity-oracle/a-breakpoint-through-edits-above-it.md.
    // Other edits follow the measured row with the same shape; they are not further VB6 measurements.
    // All offsets describe the document before the change. End excludes the line break, Next includes it.
    internal static int? Remap(int line, int offset, int removalLength, int removedBreaks, int insertedBreaks,
        Func<int, (int Begin, int Statement, int End, int Next)> readLine)
    {
        var marked = readLine(line);
        var removedEnd = offset + removalLength;
        if (line > 1 && offset <= readLine(line - 1).End && removedEnd > marked.Begin)
            return null;
        if (offset <= marked.Statement && marked.Next > marked.End && removedEnd >= marked.Next)
            return null;
        return offset <= marked.Statement ? line + insertedBreaks - removedBreaks : line;
    }
}
