using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HexIDE.Desktop;

/// <summary>
/// The mark that heads <c>--help</c>: the hexagon, and the six bonds that make the "hex" a molecule rather
/// than a shape. In colour where the console can show it, in ASCII everywhere else.
/// </summary>
/// <remarks>
/// Both forms are <see cref="Rows"/> rows of exactly <see cref="Width"/> visible cells, so the help text
/// composes one way and only the left column changes. The colour form is <c>tools/hexlogo/hexide-logo-24.ans</c>,
/// embedded at build; regenerate it with the tool beside it rather than editing the bytes.
/// </remarks>
internal static class HelpMark
{
    public const int Width = 24;
    public const int Rows = 12;

    private const string ColourResource = "hexide-logo-24.ans";

    /// <summary>The colour mark: half-block glyphs carrying 24-bit SGR colour, transparent around the hexagon.</summary>
    public static IReadOnlyList<string> Colour()
    {
        using var stream = typeof(HelpMark).Assembly.GetManifestResourceStream(ColourResource)
            ?? throw new InvalidOperationException($"{ColourResource} is not embedded in this build");
        using var reader = new StreamReader(stream);
        var rows = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return rows.Select(row => row.TrimEnd('\r')).ToArray();
    }

    /// <summary>The monochrome mark, padded to the same box as the colour one.</summary>
    public static IReadOnlyList<string> Mono()
    {
        string[] art =
        [
            @"       _-----------_    ",
            @"     /       o       \  ",
            @"    /   o    |    o   \ ",
            @"   |      \  |  /      |",
            @"   |       \ | /       |",
            @"   |       / | \       |",
            @"   |      /  |  \      |",
            @"    \   o    |    o   / ",
            @"     \_______o_______/  ",
        ];
        var blank = new string(' ', Width);
        var above = (Rows - art.Length) / 2;
        return
        [
            .. Enumerable.Repeat(blank, above),
            .. art,
            .. Enumerable.Repeat(blank, Rows - art.Length - above),
        ];
    }
}
