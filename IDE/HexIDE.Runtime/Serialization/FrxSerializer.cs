using System.Collections.Generic;
using System.IO;

namespace HexIDE.Runtime.Serialization;

/// <summary>
/// Writes the VB6 .frx binary resource file.
///
/// Records go out <b>verbatim</b>, one after another, and the offset of each is simply how far into the
/// file it landed. That is the whole format as far as this needs to know it.
///
/// The writer used to add a four-byte length prefix to each blob, which meant it could only ever produce
/// the one record shape it knew about. An .frx holds more than one: <c>List</c> and <c>ItemData</c> are
/// framed as a two-byte count, and a two-byte record cannot carry a four-byte length. Keeping the framing
/// on the record — where <see cref="FrxDeserializer"/> read it — lets a companion round-trip byte for byte
/// without either end understanding what is inside.
/// </summary>
public static class FrxSerializer
{
    /// <summary>
    /// Concatenates the records and returns the file plus each record's offset, keyed by reference so two
    /// identical records still get their own entries.
    /// </summary>
    public static (byte[] frxContent, Dictionary<byte[], int> offsets) Write(IEnumerable<byte[]> records)
    {
        var ms = new MemoryStream();
        var offsets = new Dictionary<byte[], int>(ReferenceEqualityComparer.Instance);
        foreach (var record in records)
        {
            offsets[record] = (int)ms.Position;
            ms.Write(record, 0, record.Length);
        }
        return (ms.ToArray(), offsets);
    }
}
