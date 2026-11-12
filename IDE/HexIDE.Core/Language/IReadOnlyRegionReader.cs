namespace HexIDE.Language;

/// <summary>Where a code window's buffer holds text only the IDE may change, for the window to grey, fold and refuse to write.</summary>
/// <remarks>
/// <para>
/// <b>What makes a span read-only is the file format's business; what happens to it isn't.</b> The window reads only a
/// region's positions, so it needs no knowledge of what the text says.
/// </para>
/// <para>
/// <b>Answered synchronously, as a keystroke waits.</b> The code window asks once per version of its buffer and keeps the
/// answer, but typing can't proceed until it has one, and every read covers the whole buffer.
/// </para>
/// <para>
/// <b>A format whose regions never hang from a line implements only <see cref="Regions"/>.</b> The rest has defaults
/// for that case, and the window asks the three questions only about a region <see cref="HangingRegions"/> returned.
/// </para>
/// </remarks>
public interface IReadOnlyRegionReader
{
    /// <summary>The read-only regions of <paramref name="text"/>, in order, a header first when the text has one.</summary>
    /// <param name="text">The whole buffer.</param>
    /// <param name="prefixLength">The length of the header the buffer was composed with, or 0 when nothing was split off.</param>
    IReadOnlyList<TextRegion> Regions(string text, int prefixLength);

    /// <summary>
    /// Reads regions with the format's prefix policy. When <paramref name="prefixIsEntireHeader"/> is true,
    /// a reader that recognises an implicit header may not extend it into the developer's code.
    /// Readers with explicit regions keep their own answer by default.
    /// </summary>
    IReadOnlyList<TextRegion> Regions(string text, int prefixLength, bool prefixIsEntireHeader) =>
        Regions(text, prefixLength);

    /// <summary>
    /// The regions of <paramref name="text"/> that hang from the line above them, in order. Each one's
    /// <see cref="HangingRegion.Region"/> is also one of <see cref="Regions"/>.
    /// </summary>
    /// <param name="text">The whole buffer.</param>
    /// <param name="prefixLength">The length of the header the buffer was composed with, or 0 when nothing was split off.</param>
    IReadOnlyList<HangingRegion> HangingRegions(string text, int prefixLength) => [];

    /// <summary>
    /// Where a comment starts on the line <paramref name="region"/> hangs from, or its <see cref="HangingRegion.Anchor"/>
    /// when that line has none. A line break typed before the comment takes the comment's text past the region with it.
    /// </summary>
    /// <param name="text">The whole buffer, as <paramref name="region"/> was read from it.</param>
    /// <param name="region">One of the regions <see cref="HangingRegions"/> returned for <paramref name="text"/>.</param>
    int CommentStart(string text, HangingRegion region) => region.Anchor;

    /// <summary>
    /// True when <paramref name="line"/>, a line of text being inserted, would begin a hanging region of its own. Text
    /// inserted at a region's anchor whose second line does that goes whole past the region, so that it keeps its own.
    /// </summary>
    bool StartsHangingRegion(ReadOnlySpan<char> line) => false;

    /// <summary>
    /// Where, in <paramref name="inserted"/>, the first line ends that <paramref name="region"/> could hang from, when the
    /// text replaces the whole of the line it hangs from now; null when no line of it could. The region stays under that
    /// line, and the text before it goes above the region.
    /// </summary>
    /// <param name="text">The whole buffer, as <paramref name="region"/> was read from it.</param>
    /// <param name="region">One of the regions <see cref="HangingRegions"/> returned for <paramref name="text"/>.</param>
    /// <param name="inserted">The text being inserted, its line breaks already the buffer's own.</param>
    /// <returns>The end of that line's text, before its terminator, as an offset into <paramref name="inserted"/>.</returns>
    int? LineToHangFrom(string text, HangingRegion region, string inserted) => null;
}
