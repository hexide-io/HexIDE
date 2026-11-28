using System;

namespace HexIDE.Runtime.BuiltinTypes;

public static class VBFontSizing
{
    public static int RoundToPixels(double size) =>
        (int)Math.Round((decimal)size * 4m / 3m, MidpointRounding.AwayFromZero);

    /// <summary>The design-time dialog's measured 96-dpi rounding, without bitmap-font substitution (#4153).</summary>
    public static double SnapDialogSize(double size) => Math.Max(2, RoundToPixels(size)) * 0.75;
}
