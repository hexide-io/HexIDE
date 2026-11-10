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
/// </remarks>
public interface IReadOnlyRegionReader
{
    /// <summary>The read-only regions of <paramref name="text"/>, in order, a header first when the text has one.</summary>
    /// <param name="text">The whole buffer.</param>
    /// <param name="prefixLength">The length of the header the buffer was composed with, or 0 when nothing was split off.</param>
    IReadOnlyList<TextRegion> Regions(string text, int prefixLength);
}
