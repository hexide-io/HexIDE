using System;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Runtime.BuiltinControls;

internal static class SimpleComboHeights
{
    // VB6 SP6 at 96 dpi, native and p-code: docs/vb6-fidelity-oracle/a-simple-combos-height-by-font.md (#1655).
    private static readonly (string Font, double Size, int Edit, int Row)[] Metrics =
    [
        ("MS Sans Serif", 8.25, 21, 13), ("MS Sans Serif", 9.75, 24, 16),
        ("Arial", 8.25, 22, 14), ("Arial", 12, 26, 18), ("Tahoma", 8.25, 21, 13),
        ("Courier New", 9.75, 24, 16), ("Times New Roman", 14.25, 29, 21),
    ];

    internal static (int Edit, int Row)? Find(VBFont font)
    {
        if (font.Weight != VBFont.NormalWeight || font.Italic)
            return null;
        foreach (var (name, size, edit, row) in Metrics)
            if (string.Equals(font.FontFamilyName, name, StringComparison.OrdinalIgnoreCase) && font.Size == size)
                return (edit, row);
        return null;
    }

    internal static double Snap(double pixels, (int Edit, int Row) metrics)
    {
        var rows = Math.Floor((pixels - metrics.Edit - 4) / metrics.Row);
        return rows <= 0 ? metrics.Edit : metrics.Edit + 4 + metrics.Row * rows;
    }
}
