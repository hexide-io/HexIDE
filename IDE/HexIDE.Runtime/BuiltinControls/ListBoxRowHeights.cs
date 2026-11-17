using System;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Runtime.BuiltinControls;

internal static class ListBoxRowHeights
{
    // Inferred from measured Controls.Add heights: docs/vb6-fidelity-oracle/controls-add-and-controls-remove.md.
    private static readonly (string Font, double Size, int Row)[] Rows =
    [
        ("MS Sans Serif", 8.25, 13), ("MS Sans Serif", 9.75, 16), ("MS Sans Serif", 12, 20), ("MS Sans Serif", 13.5, 24),
        ("Microsoft Sans Serif", 8.25, 13), ("Tahoma", 8.25, 13), ("Tahoma", 9.75, 16), ("Verdana", 8.25, 13),
        ("Arial", 8.25, 14), ("Arial", 9.75, 16), ("Arial", 12, 18), ("Times New Roman", 12, 19), ("Courier New", 9.75, 16),
        ("Segoe UI", 9, 15),
    ];

    internal static int? Find(VBFont? font)
    {
        foreach (var (name, size, row) in Rows)
            if (font is { } f && string.Equals(f.FontFamilyName, name, StringComparison.OrdinalIgnoreCase) && Math.Abs(f.Size - size) < 0.01)
                return row;
        return null;
    }
}
