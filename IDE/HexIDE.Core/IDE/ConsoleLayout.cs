using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HexIDE.IDE;

/// <summary>
/// Lays a mark and a block of text out for a console: the mark to the left of the text when the console is
/// wide enough, above it when it is not.
/// </summary>
/// <remarks>
/// <para>
/// Lines of the mark may carry SGR colour sequences (<c>ESC [ … m</c>), which occupy bytes and no cells.
/// Every width here is a <em>visible</em> width, measured with those stripped, so a colour mark and a
/// monochrome one compose identically as long as their rows are padded to the same width.
/// </para>
/// <para>
/// Which mark and which layout are two independent questions. Colour depends on what the console can
/// show; the layout depends only on how wide it is, and a padded 24-column monochrome mark beside 78
/// columns of text wraps in an 80-column window exactly as a colour one does. Wrapping is not "tight": a
/// wrapped row pushes the next mark row down a line, so the mark arrives sliced into bands with text
/// between them.
/// </para>
/// </remarks>
public static partial class ConsoleLayout
{
    /// <summary>The space between the mark and the text when they share rows.</summary>
    public const string Gutter = "  ";

    /// <summary>Cells a line occupies on screen: its length with any SGR sequences removed.</summary>
    public static int VisibleWidth(string line) => Sgr().Replace(line, "").Length;

    /// <summary>
    /// Whether every line of <paramref name="text"/> fits beside a mark <paramref name="markWidth"/> cells
    /// wide in a console <paramref name="consoleWidth"/> cells wide, without wrapping.
    /// </summary>
    public static bool FitsBeside(int consoleWidth, int markWidth, IEnumerable<string> text)
    {
        var needed = markWidth + Gutter.Length + text.Select(VisibleWidth).DefaultIfEmpty(0).Max();
        return consoleWidth >= needed;
    }

    /// <summary>
    /// The mark on the left, <paramref name="text"/> to its right, one output row per row of whichever is
    /// taller. Rows the mark does not reach are indented by its width so the text column stays straight;
    /// rows the text does not reach end with the mark.
    /// </summary>
    /// <param name="mark">Rows of the mark, each exactly <paramref name="markWidth"/> visible cells.</param>
    public static IReadOnlyList<string> Beside(IReadOnlyList<string> mark, IReadOnlyList<string> text, int markWidth)
    {
        var rows = Math.Max(mark.Count, text.Count);
        var blank = new string(' ', markWidth);
        var lines = new List<string>(rows);
        for (var i = 0; i < rows; i++)
        {
            var left = i < mark.Count ? mark[i] : blank;
            var right = i < text.Count ? text[i] : "";
            lines.Add(right.Length == 0 ? left.TrimEnd() : left + Gutter + right);
        }

        return lines;
    }

    /// <summary>
    /// The mark, one blank row, then the text. Blank rows the mark or the text carry for the beside
    /// layout's sake are not stacked on top of that one.
    /// </summary>
    public static IReadOnlyList<string> Stacked(IReadOnlyList<string> mark, IReadOnlyList<string> text)
    {
        var markRows = mark.Select(row => row.TrimEnd()).ToList();
        while (markRows.Count > 0 && markRows[^1].Length == 0) markRows.RemoveAt(markRows.Count - 1);

        var textRows = text.SkipWhile(row => row.Length == 0);
        return [.. markRows, "", .. textRows];
    }

    [GeneratedRegex("\x1b\\[[0-9;]*m")]
    private static partial Regex Sgr();
}
