using System;
using System.Collections.Generic;
using System.IO;

namespace HexIDE.Runtime.Serialization;

/// <summary>
/// Reads the VB6 .frx binary resource file format.
/// An .frx file is a flat sequence of blobs; each blob is a 4-byte LE length followed by the data.
/// References in .frm files look like: Picture = "Form1.frx":0A00
/// where the hex value is the byte offset of the length field.
/// </summary>
public static class FrxDeserializer
{
    /// <summary>
    /// Parses an .frx file and returns all blobs indexed by their byte offset
    /// (the offset where the 4-byte length prefix begins).
    /// The returned byte arrays do NOT include the 4-byte length prefix.
    /// </summary>
    public static Dictionary<int, byte[]> Read(byte[] content)
    {
        var blobs = new Dictionary<int, byte[]>();
        var pos = 0;
        while (pos + 4 <= content.Length)
        {
            var blobOffset = pos;
            var length = BitConverter.ToInt32(content, pos);
            pos += 4;
            // Subtract instead of `pos + length`: a crafted length near int.MaxValue would overflow the addition to a
            // negative value, sneak past the bound, and allocate ~2 GB. content.Length - pos is >= 0 here (the while
            // guard ensured pos <= content.Length), so this comparison can't overflow.
            if (length < 0 || length > content.Length - pos)
                break;
            var data = new byte[length];
            Array.Copy(content, pos, data, 0, length);
            blobs[blobOffset] = data;
            pos += length;
        }
        return blobs;
    }

    /// <summary>
    /// Parses the hex offset token from a .frm .frx reference value.
    /// Returns the blob bytes for the given offset, or null if not found.
    /// Input example: "Form1.frx":0A00
    /// </summary>
    public static byte[]? TryExtractBlob(string propertyValue, IReadOnlyDictionary<int, byte[]> blobs)
    {
        // Pattern: "filename.frx":HHHH
        var colonIdx = propertyValue.LastIndexOf(':');
        if (colonIdx < 0)
            return null;

        var hexPart = propertyValue[(colonIdx + 1)..].Trim();
        if (!int.TryParse(hexPart, System.Globalization.NumberStyles.HexNumber, null, out var offset))
            return null;

        return blobs.TryGetValue(offset, out var blob) ? blob : null;
    }

    /// <summary>Returns true if the property value looks like a binary resource reference (.frx/.ctx/.pgx).</summary>
    public static bool IsFrxReference(string value)
        => value.Contains(".frx\":", StringComparison.OrdinalIgnoreCase)
        || value.Contains(".ctx\":", StringComparison.OrdinalIgnoreCase)
        || value.Contains(".pgx\":", StringComparison.OrdinalIgnoreCase);
}
