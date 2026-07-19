using System.Collections.Generic;
using System.IO;

namespace HexIDE.Runtime.Serialization;

/// <summary>
/// Writes the VB6 .frx binary resource file format.
/// Blobs are written sequentially: 4-byte LE length followed by the blob bytes.
/// </summary>
public static class FrxSerializer
{
    /// <summary>
    /// Writes the provided blobs sequentially into an .frx byte array.
    /// Returns the binary file content and a mapping from each blob (by reference) to its offset
    /// (the offset of the 4-byte length prefix in the output).
    /// </summary>
    public static (byte[] frxContent, Dictionary<byte[], int> offsets) Write(IEnumerable<byte[]> blobs)
    {
        var ms = new MemoryStream();
        var offsets = new Dictionary<byte[], int>(ReferenceEqualityComparer.Instance);
        foreach (var blob in blobs)
        {
            offsets[blob] = (int)ms.Position;
            var lengthBytes = new byte[4];
            lengthBytes[0] = (byte)(blob.Length & 0xFF);
            lengthBytes[1] = (byte)((blob.Length >> 8) & 0xFF);
            lengthBytes[2] = (byte)((blob.Length >> 16) & 0xFF);
            lengthBytes[3] = (byte)((blob.Length >> 24) & 0xFF);
            ms.Write(lengthBytes, 0, 4);
            ms.Write(blob, 0, blob.Length);
        }
        return (ms.ToArray(), offsets);
    }
}
