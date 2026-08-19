using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HexIDE.Runtime.Serialization;

/// <summary>
/// Reads the VB6 .frx binary resource file — the companion holding the bytes a designer file's properties
/// point at. References in a .frm look like <c>Picture = "Form1.frx":0A00</c>, where the hex value is the
/// byte offset the record starts at.
///
/// <b>Records are returned verbatim, framing included.</b> That is deliberate, and it is what lets the
/// writer put them back byte for byte without understanding any of them: an .frx holds several record
/// shapes, and HexIDE only knows one of them.
///
/// <b>The .frm partitions the companion.</b> Records are contiguous, so a record runs from its own cited
/// offset to the next one, and the last runs to the end of the file. That is the whole parsing rule, and
/// it needs no knowledge of framing at all — which matters, because the framing is not uniform:
///
/// <code>
///   ODBC Log In.frx, all sixteen bytes of it:
///   08 00 00 00  6c 74 00 00  00 00 00 00 | 00 00 | 00 00
///   └─ Icon @0x0000, 12 bytes ──────────┘ │ └ @0x0C │ └ @0x0E
///                                          ItemData   List
/// </code>
///
/// A two-byte record cannot carry a four-byte length. <c>List</c> and <c>ItemData</c> are framed as a
/// two-byte count instead, and walking the file as a flat sequence of length-prefixed blobs — which is what
/// this did until 2026-08-19 — reads the first, mistakes the ItemData count for a length of zero, and never
/// finds the List record at all.
/// </summary>
public static class FrxDeserializer
{
    /// <summary>
    /// Reads a companion using the offsets its designer file cites — the reliable parse.
    ///
    /// <paramref name="citingSource"/> is the .frm/.ctl text. Only its <c>"name.frx":HHHH</c> tokens are
    /// read, so it does not need to be parsed first.
    /// </summary>
    public static Dictionary<int, byte[]> Read(byte[] content, string citingSource)
    {
        var offsets = CitedOffsets(citingSource)
            .Where(o => o >= 0 && o < content.Length)
            .Distinct()
            .OrderBy(o => o)
            .ToList();

        // Nothing cites it — fall back to walking, which is all that is left to go on. A companion with no
        // references is already a defect elsewhere, and guessing badly here would hide it.
        if (offsets.Count == 0) return Read(content);

        var records = new Dictionary<int, byte[]>();
        for (var i = 0; i < offsets.Count; i++)
        {
            var start = offsets[i];
            var end = i + 1 < offsets.Count ? offsets[i + 1] : content.Length;
            if (end <= start) continue;
            records[start] = content[start..end];
        }
        return records;
    }

    /// <summary>
    /// Reads a companion with no designer file to go on, by walking it as a flat sequence of four-byte
    /// length-prefixed records.
    ///
    /// This is a fallback and it is known to be wrong for any companion holding a <c>List</c> or
    /// <c>ItemData</c> record. Prefer the overload that takes the citing source.
    /// </summary>
    public static Dictionary<int, byte[]> Read(byte[] content)
    {
        var blobs = new Dictionary<int, byte[]>();
        var pos = 0;
        while (pos + 4 <= content.Length)
        {
            var blobOffset = pos;
            var length = BitConverter.ToInt32(content, pos);
            // Subtract instead of `pos + 4 + length`: a crafted length near int.MaxValue would overflow the
            // addition to a negative value, sneak past the bound, and allocate ~2 GB.
            if (length < 0 || length > content.Length - pos - 4)
                break;
            blobs[blobOffset] = content[blobOffset..(blobOffset + 4 + length)];
            pos = blobOffset + 4 + length;
        }
        return blobs;
    }

    /// <summary>Every <c>"name.frx":HHHH</c> offset a designer file cites, in the order they appear.</summary>
    public static IEnumerable<int> CitedOffsets(string citingSource)
    {
        foreach (var line in citingSource.Split('\n'))
        {
            if (!IsFrxReference(line)) continue;
            var colon = line.LastIndexOf(':');
            if (colon < 0) continue;
            var hex = line[(colon + 1)..].Trim();
            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var offset))
                yield return offset;
        }
    }

    /// <summary>
    /// Parses the hex offset from a .frm reference and returns that record, or null when the companion has
    /// nothing at that offset. Input example: <c>"Form1.frx":0A00</c>
    /// </summary>
    public static byte[]? TryExtractBlob(string propertyValue, IReadOnlyDictionary<int, byte[]> blobs)
    {
        var colonIdx = propertyValue.LastIndexOf(':');
        if (colonIdx < 0)
            return null;
        var hexPart = propertyValue[(colonIdx + 1)..].Trim();
        if (!int.TryParse(hexPart, System.Globalization.NumberStyles.HexNumber, null, out var offset))
            return null;
        return blobs.TryGetValue(offset, out var blob) ? blob : null;
    }

    /// <summary>
    /// Where the payload starts inside a record, for a caller that wants to look at the content rather than
    /// move the bytes around.
    ///
    /// Self-validating rather than property-driven: a four-byte length prefix is only believed when it
    /// exactly accounts for the rest of the record. That is true of every picture record in VB6's own
    /// Template tree and false of every <c>List</c>/<c>ItemData</c> record, so no table of property names
    /// is needed — and a record shape nobody has seen yet is treated as opaque, which is the safe default.
    /// </summary>
    public static int PayloadOffset(byte[] record)
    {
        if (record.Length < 4) return 0;
        var declared = BitConverter.ToInt32(record, 0);
        return declared == record.Length - 4 ? 4 : 0;
    }

    /// <summary>Returns true if the property value looks like a binary resource reference (.frx/.ctx/.pgx).</summary>
    public static bool IsFrxReference(string value)
        => value.Contains(".frx\":", StringComparison.OrdinalIgnoreCase)
        || value.Contains(".ctx\":", StringComparison.OrdinalIgnoreCase)
        || value.Contains(".pgx\":", StringComparison.OrdinalIgnoreCase);
}
