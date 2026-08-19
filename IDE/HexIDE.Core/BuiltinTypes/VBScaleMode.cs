namespace HexIDE.Runtime.BuiltinTypes;

public enum VBScaleMode
{
    User,
    Twip,
    Point,
    Pixel,
    Character,
    Inch,
    Millimiter,
    Centimiter
}

public static class VBScaleModeExtensions
{
    public const int PixelToTwips = 15;

    /// <summary>
    /// How many twips one unit of this scale mode is, horizontally and vertically.
    ///
    /// Two axes rather than one because VB6's <see cref="VBScaleMode.Character"/> unit is not square: a
    /// character cell is 120 twips wide and 240 twips high. Every other mode is the same both ways.
    ///
    /// <see cref="VBScaleMode.User"/> returns 1, but the caller should not be converting at all — a user
    /// scale is a number the developer chose and nothing can derive it. See <c>FormDefinition.Scale</c>.
    ///
    /// Twip, Point and Inch are definitional (1440 twips to the inch, 72 points to the inch), and
    /// Millimiter/Centimiter follow from the inch. Pixel and Twip are the two modes VB6's own Template
    /// tree exercises, and both are verified against it — <c>Colorful Control.ctl</c> declares
    /// <c>ScaleMode = 3 'Pixel</c> with <c>ScaleWidth = 320</c> against <c>ClientWidth = 4800</c>.
    /// Character's 120x240 is VB6's documented cell and has NOT been put to the oracle.
    /// </summary>
    public static (double Horizontal, double Vertical) TwipsPerUnit(this VBScaleMode mode) => mode switch
    {
        VBScaleMode.Point => (20, 20),
        VBScaleMode.Pixel => (PixelToTwips, PixelToTwips),
        VBScaleMode.Character => (120, 240),
        VBScaleMode.Inch => (1440, 1440),
        VBScaleMode.Millimiter => (1440 / 25.4, 1440 / 25.4),
        VBScaleMode.Centimiter => (1440 / 2.54, 1440 / 2.54),
        _ => (1, 1),
    };
}
