namespace HexIDE.Language;

/// <summary>
/// A read-only region that belongs to the line above it: it folds into that line, goes with it when the whole line
/// is deleted, and stays under it when a line break is typed at its end. The code window reads it as positions,
/// a key and a name, and nothing about what its text says.
/// </summary>
/// <param name="Region">
/// The region's whole lines. Its <see cref="TextRegion.Anchor"/> is <paramref name="Anchor"/>, and it is one of the
/// regions <see cref="IReadOnlyRegionReader.Regions"/> returns for the same text.
/// </param>
/// <param name="LineStart">
/// Where the line it hangs from starts: the first physical line of that logical line, so a line continued over
/// several physical lines counts as one.
/// </param>
/// <param name="Anchor">
/// Where the line it hangs from ends: the end of the text of the physical line directly above the region, before
/// its terminator. A fold of the region starts here.
/// </param>
/// <param name="LastContentEnd">The end of the text of the region's last line, before its terminator. A fold ends here.</param>
/// <param name="Key">Which region this is, in a form that outlives its offsets, for state kept across an edit.</param>
/// <param name="Name">What the region belongs to, as a message names it: the name the line above declares, say.</param>
public sealed record HangingRegion(
    TextRegion Region,
    int LineStart,
    int Anchor,
    int LastContentEnd,
    HangingRegionKey Key,
    string Name);

/// <summary>
/// A <see cref="HangingRegion"/>'s identity across edits: a name, and its ordinal among the regions of that name,
/// counted from the top of the text. The format chooses both, and the code window only compares and keeps them.
/// </summary>
/// <remarks>
/// A name alone is not enough when two lines of one name may each carry a region, as a property's two accessors
/// can. An edit that renames the line, or adds a region of the same name above it, gives it a new key.
/// </remarks>
public readonly record struct HangingRegionKey(string Name, int Ordinal);
