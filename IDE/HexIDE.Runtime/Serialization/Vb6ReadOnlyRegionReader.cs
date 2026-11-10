using System.Collections.Generic;
using HexIDE.Language;

namespace HexIDE.Runtime.Serialization;

/// <summary>A VB6 file's read-only regions: its header and every member's attribute run (<see cref="ReadOnlyRegions.Of"/>).</summary>
public sealed class Vb6ReadOnlyRegionReader : IReadOnlyRegionReader
{
    /// <inheritdoc/>
    public IReadOnlyList<TextRegion> Regions(string text, int prefixLength) => ReadOnlyRegions.Of(text, prefixLength);
}
