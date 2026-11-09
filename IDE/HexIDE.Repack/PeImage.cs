using System.Buffers.Binary;
using System.Reflection.PortableExecutable;

namespace HexIDE.Repack;

/// <summary>One entry of a Windows executable's resource table, by number. Names are not used by anything here.</summary>
internal sealed record NativeResource(int Type, int Name, int Language, byte[] Data);

/// <summary>The parts of a Windows executable this tool needs to read, and the one it has to remove.</summary>
internal static class PeImage
{
    public const int TypeIcon = 3;
    public const int TypeGroupIcon = 14;
    public const int TypeVersion = 16;

    private const int SecurityDirectoryIndex = 4;
    private const int DirectoryEntrySize = 8;
    private const int Pe32DirectoriesOffset = 96;
    private const int Pe32PlusDirectoriesOffset = 112;

    public static bool HasSignature(string path)
    {
        using var file = File.OpenRead(path);
        using var image = new PEReader(file);
        return image.PEHeaders.PEHeader!.CertificateTableDirectory.Size != 0;
    }

    /// <summary>
    /// Takes the Authenticode signature off the file, returning whether there was one. It has to go before the
    /// resources change, because a signature left in place would describe bytes that no longer exist.
    /// </summary>
    public static bool RemoveSignature(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        int tableOffset, tableSize, directoryEntry;
        using (var image = new PEReader(file, PEStreamOptions.LeaveOpen))
        {
            var header = image.PEHeaders.PEHeader!;
            tableOffset = header.CertificateTableDirectory.RelativeVirtualAddress;
            tableSize = header.CertificateTableDirectory.Size;
            var directories = header.Magic == PEMagic.PE32Plus ? Pe32PlusDirectoriesOffset : Pe32DirectoriesOffset;
            directoryEntry = image.PEHeaders.PEHeaderStartOffset + directories + SecurityDirectoryIndex * DirectoryEntrySize;
        }

        if (tableSize == 0)
            return false;

        // Unlike every other directory this one holds a file offset, and the table is padded to eight bytes. Anything
        // after it would be cut off by the truncation, and isn't something this tool knows how to keep.
        var tableEnd = (long)tableOffset + tableSize;
        if (tableEnd > file.Length || (tableEnd + 7) / 8 * 8 < file.Length)
            throw new RepackException($"{path} has data after its signature, so the signature can't be removed safely.");

        file.Position = directoryEntry;
        file.Write(new byte[DirectoryEntrySize]);
        file.SetLength(tableOffset);
        return true;
    }

    public static List<NativeResource> ReadResources(string path)
    {
        using var file = File.OpenRead(path);
        using var image = new PEReader(file, PEStreamOptions.PrefetchEntireImage);

        var result = new List<NativeResource>();
        var root = image.PEHeaders.PEHeader!.ResourceTableDirectory;
        if (root.RelativeVirtualAddress == 0)
            return result;

        var table = image.GetSectionData(root.RelativeVirtualAddress).GetContent().AsSpan().ToArray();
        foreach (var (type, typeEntries) in Entries(table, 0))
        {
            foreach (var (name, nameEntries) in Entries(table, typeEntries))
            {
                foreach (var (language, leaf) in Entries(table, nameEntries))
                {
                    var dataRva = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(leaf));
                    var size = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(leaf + 4));
                    var data = image.GetSectionData(dataRva).GetContent(0, size).AsSpan().ToArray();
                    result.Add(new NativeResource(type, name, language, data));
                }
            }
        }

        return result;
    }

    // A resource directory: two counts, then eight-byte entries. The high bit of an entry's name marks a string name,
    // which this tool has no use for, and the high bit of its offset marks a subdirectory.
    private static IEnumerable<(int Id, int Offset)> Entries(byte[] table, int directory)
    {
        int named = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(directory + 12));
        int numbered = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(directory + 14));
        for (var i = 0; i < named + numbered; i++)
        {
            var entry = directory + 16 + i * 8;
            var id = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(entry));
            if (id >= 0)
                yield return (id, BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(entry + 4)) & int.MaxValue);
        }
    }
}
