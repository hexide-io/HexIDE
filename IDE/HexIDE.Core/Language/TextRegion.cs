namespace HexIDE.Language;

/// <summary>
/// A span of a code window's buffer that only the IDE may change: <c>[Start, End)</c>, whole lines, with
/// <see cref="End"/> just past the last line's terminator.
/// </summary>
/// <param name="Start">The offset of the region's first character, which is always the start of a line.</param>
/// <param name="End">The offset just past the region's last line, terminator included.</param>
/// <param name="OpenEnded">
/// True when the region's last line is the last line of the buffer and has no terminator. Text inserted at
/// <see cref="End"/> would then join that line rather than start a new one, so it counts as inside.
/// </param>
/// <param name="Anchor">
/// For a region that hangs from the line above it, where that line ends: the start of its terminator. Null for a
/// region that hangs from nothing, as a file's header does. See <see cref="Changes"/>.
/// </param>
public readonly record struct TextRegion(int Start, int End, bool OpenEnded = false, int? Anchor = null)
{
    /// <summary>
    /// True when <paramref name="length"/> characters at <paramref name="offset"/> lie in this region, or an
    /// insertion there would land in it.
    /// </summary>
    /// <remarks>
    /// An insertion (<paramref name="length"/> 0) at the region's first character counts as inside, because
    /// it pushes the region away from the line above it: for a header that is text above the top of the file,
    /// and for a region that hangs from a line it separates the two. An insertion at <see cref="End"/> is the
    /// start of the next line and does not count, unless the region is <see cref="OpenEnded"/>.
    /// <para>
    /// This is membership: whether a line or a span is <em>in</em> the region. Whether an <em>edit</em> may
    /// be made is <see cref="Changes"/>, which is stricter.
    /// </para>
    /// </remarks>
    public bool Touches(int offset, int length) =>
        length > 0
            ? offset < End && offset + length > Start
            : offset >= Start && (offset < End || (OpenEnded && offset == End));

    /// <summary>Where an edit starts to change this region: <see cref="Anchor"/>, or <see cref="Start"/>.</summary>
    public int EditStart => Anchor ?? Start;

    /// <summary>
    /// True when replacing <paramref name="length"/> characters at <paramref name="offset"/> would change
    /// this region: <see cref="Touches"/>, and also any replacement that removes the line break between the
    /// region and the line it hangs from.
    /// </summary>
    /// <remarks>
    /// That line break is outside the region, because the region is whole lines and the break ends the line above.
    /// But removing it joins the region's first line onto the line it hangs from, and the region then hangs from
    /// nothing. Typing is refused it by the code window's section provider (#273 task 3.7), which starts a hanging
    /// region's protected span at the same <see cref="EditStart"/>. Every other writer asks this (task 3.11, found
    /// by review: a Replace ending in a line break, with the same text and no line break, made exactly that join).
    /// <para>
    /// An insertion is unchanged: text typed at the end of the line above lands before its line break and leaves
    /// the region where it is.
    /// </para>
    /// </remarks>
    public bool Changes(int offset, int length) =>
        length > 0
            ? offset < End && offset + length > EditStart
            : Touches(offset, 0);
}
