using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Runtime.Components;

/// <summary>
/// Where a Line sits on its container, which the form designer lays it out by and the running Line is placed by (#5680).
/// </summary>
public static class LineControlBounds
{
    /// <summary>
    /// The rectangle a line covers on its container, in pixels: the segment's pixels and the width of its pen, with a
    /// pixel to spare. The designer selects a line by this rectangle.
    /// </summary>
    public static (double Left, double Top, double Width, double Height) BoundsFor(double x1Twips, double y1Twips, double x2Twips, double y2Twips, int borderWidth)
    {
        var pad = Math.Max(1, borderWidth) + 1;
        int x1 = Pixel(x1Twips), y1 = Pixel(y1Twips), x2 = Pixel(x2Twips), y2 = Pixel(y2Twips);
        return (Math.Min(x1, x2) - pad, Math.Min(y1, y2) - pad,
            Math.Abs(x2 - x1) + 2 * pad + 1, Math.Abs(y2 - y1) + 2 * pad + 1);
    }

    // The pixel nearest a twips position. Twips are whole and a pixel is 15 of them, so no position is halfway.
    private static int Pixel(double twips) => (int)Math.Round(twips / VBScaleModeExtensions.PixelToTwips);
}
