using System.Buffers.Binary;
using System.Text;

namespace HexIDE.Office;

/// <summary>Read-only MS-CFB reader over a seekable stream. Does not own the stream unless requested.</summary>
public sealed class CompoundFile : IDisposable
{
    private const uint Free = 0xFFFFFFFF;
    private const uint End = 0xFFFFFFFE;
    private const uint FatSector = 0xFFFFFFFD;
    private const uint DifatSector = 0xFFFFFFFC;
    internal static ReadOnlySpan<byte> Signature => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    private readonly Stream source;
    private readonly bool leaveOpen;
    private readonly object gate = new();
    private readonly int sectorSize;
    private readonly long sectorCount;
    private readonly List<uint> fat = [];
    private readonly List<uint> miniFat;
    private readonly List<uint> miniStream;
    private readonly byte[] fatBuffer;
    private uint cachedFat = Free;
    private bool disposed;

    public CompoundEntry Root { get; }

    public static CompoundFile Open(Stream source, bool leaveOpen = true) =>
        OfficeFileException.Read(() => new CompoundFile(source, leaveOpen));

    private CompoundFile(Stream source, bool leaveOpen)
    {
        this.source = source;
        this.leaveOpen = leaveOpen;
        if (!source.CanRead || !source.CanSeek)
            throw new OfficeFileException("Compound files require a readable, seekable stream.");
        var header = new byte[512];
        ReadAt(0, header);
        if (!header.AsSpan(0, 8).SequenceEqual(Signature))
            throw new OfficeFileException("Invalid compound-file signature.");
        var version = U16(header, 26);
        if (U16(header, 28) != 0xFFFE || version is not (3 or 4) ||
            U16(header, 30) != (version == 3 ? 9 : 12) || U16(header, 32) != 6 || U32(header, 56) != 4096)
            throw new OfficeFileException("Unsupported compound-file header: expected version 3/512 or 4/4096, little endian, 64-byte mini sectors and 4096-byte cutoff.");
        sectorSize = version == 3 ? 512 : 4096;
        if (source.Length < sectorSize)
            throw new OfficeFileException("Truncated compound file: incomplete header sector.");
        // A writer may end the file inside its last sector, after that sector's last used byte, and Windows reads such a
        // file: a cached Windows Installer package ends 1,777 bytes into its last 4096-byte sector. So a partial last
        // sector counts, and what is refused is a read that runs past the end (ReadAt).
        sectorCount = (source.Length + sectorSize - 1) / sectorSize - 1;
        fatBuffer = new byte[sectorSize];
        var fatCount = U32(header, 44);
        var difatCount = U32(header, 72);
        CheckCount(fatCount, "FAT");
        CheckCount(difatCount, "DIFAT");
        var fatSeen = new HashSet<uint>();
        void AddFat(uint id)
        {
            if (id == Free)
                return;
            CheckSector(id, "FAT");
            if (!fatSeen.Add(id) || fat.Count >= fatCount)
                throw new OfficeFileException("Invalid DIFAT: duplicate or excess FAT sector.");
            fat.Add(id);
        }
        for (var offset = 76; offset < 512; offset += 4)
            AddFat(U32(header, offset));
        var difat = U32(header, 68);
        var difatSeen = new HashSet<uint>();
        while (difat != End)
        {
            CheckSector(difat, "DIFAT");
            if (!difatSeen.Add(difat))
                throw new OfficeFileException("DIFAT chain loops.");
            if (difatSeen.Count > difatCount)
                throw new OfficeFileException("DIFAT chain exceeds its declared sector count.");
            var bytes = ReadSector(difat);
            for (var offset = 0; offset < sectorSize - 4; offset += 4)
                AddFat(U32(bytes, offset));
            difat = U32(bytes, sectorSize - 4);
        }
        if (difatSeen.Count != difatCount || fat.Count != fatCount || (ulong)fat.Count * (uint)(sectorSize / 4) < (ulong)sectorCount)
            throw new OfficeFileException("FAT or DIFAT sector count does not match the file.");
        foreach (var id in fat)
            if (Next(id) != FatSector)
                throw new OfficeFileException("FAT sector is not marked as FAT.");
        foreach (var id in difatSeen)
            if (fatSeen.Contains(id) || Next(id) != DifatSector)
                throw new OfficeFileException("DIFAT sector is not marked as DIFAT.");

        var directorySectors = Chain(U32(header, 48), false, "Directory FAT");
        if ((version == 3 && U32(header, 40) != 0) ||
            (version == 4 && (uint)directorySectors.Count != U32(header, 40)))
            throw new OfficeFileException("Directory sector count does not match the header.");
        var entries = new List<CompoundEntry>();
        foreach (var id in directorySectors)
        {
            var bytes = ReadSector(id);
            for (var offset = 0; offset < sectorSize; offset += 128)
            {
                var type = bytes[offset + 66];
                if (type is not (0 or 1 or 2 or 5))
                    throw new OfficeFileException("Invalid directory entry type.");
                var nameLength = U16(bytes, offset + 64);
                var name = "";
                if (type != 0)
                {
                    if (nameLength < 2 || nameLength > 64 || nameLength % 2 != 0 || U16(bytes, offset + nameLength - 2) != 0)
                        throw new OfficeFileException("Invalid directory entry name length or terminator.");
                    name = Encoding.Unicode.GetString(bytes, offset, nameLength - 2);
                    if (name.IndexOfAny(['\0', '/', '\\', ':', '!']) >= 0 || name.Length == 0)
                        throw new OfficeFileException("Invalid directory entry name.");
                    if (bytes[offset + 67] > 1)
                        throw new OfficeFileException("Invalid directory entry colour.");
                }
                // MS-CFB 2.6.3 requires readers to ignore an uninitialised high DWORD in v3.
                var length = version == 3 ? U32(bytes, offset + 120) : U64(bytes, offset + 120);
                entries.Add(new CompoundEntry(this, name, type, type == 1 ? 0 : length,
                    U32(bytes, offset + 68), U32(bytes, offset + 72), U32(bytes, offset + 76), U32(bytes, offset + 116)));
            }
        }
        if (entries.Count == 0 || entries[0].Type != 5 || entries[0].Left != Free || entries[0].Right != Free)
            throw new OfficeFileException("Missing or invalid root directory entry.");
        Root = entries[0];
        var visited = new HashSet<uint> { 0 };
        var pending = new Stack<(CompoundEntry Parent, uint Id)>();
        pending.Push((Root, Root.Child));
        while (pending.TryPop(out var item))
        {
            if (item.Id == Free)
                continue;
            if (item.Id >= entries.Count)
                throw new OfficeFileException("Directory tree entry is out of range.");
            if (!visited.Add(item.Id))
                throw new OfficeFileException("Directory tree loops or reuses an entry.");
            var entry = entries[(int)item.Id];
            if (entry.Type is not (1 or 2))
                throw new OfficeFileException("Directory tree references a non-child entry.");
            if (!item.Parent.AddChild(entry))
                throw new OfficeFileException("Directory storage contains duplicate names.");
            pending.Push((item.Parent, entry.Left));
            pending.Push((item.Parent, entry.Right));
            if (entry.IsStorage)
                pending.Push((entry, entry.Child));
            else if (entry.Child != Free)
                throw new OfficeFileException("A stream has a directory child.");
        }
        for (var i = 1; i < entries.Count; ++i)
            if (entries[i].Type != 0 && !visited.Contains((uint)i))
                throw new OfficeFileException("Directory has an unreachable entry.");
        CheckCount(U32(header, 64), "Mini FAT");
        miniFat = Chain(U32(header, 60), false, "Mini FAT");
        if (miniFat.Count != U32(header, 64))
            throw new OfficeFileException("Mini FAT sector count does not match the header.");
        miniStream = Chain(Root.Start, false, "Mini stream FAT");
        CheckLength(Root.Length, miniStream.Count, sectorSize, "Mini stream");
    }

    private void CheckCount(uint count, string kind)
    {
        if (count > sectorCount)
            throw new OfficeFileException($"{kind} sector count exceeds the file's sectors.");
    }

    private void CheckSector(uint id, string kind)
    {
        if (id >= sectorCount || id > 0xFFFFFFFA)
            throw new OfficeFileException($"{kind} sector number is past the end of the file or reserved.");
    }

    private void ReadAt(long offset, Span<byte> bytes)
    {
        if (offset < 0 || offset > source.Length || bytes.Length > source.Length - offset)
            throw new OfficeFileException("Truncated compound file: read is past the end.");
        source.Position = offset;
        source.ReadExactly(bytes);
    }

    private byte[] ReadSector(uint id)
    {
        CheckSector(id, "FAT");
        var bytes = new byte[sectorSize];
        ReadAt(((long)id + 1) * sectorSize, bytes);
        return bytes;
    }

    private uint Next(uint id)
    {
        var index = id / (uint)(sectorSize / 4);
        if (index >= fat.Count)
            throw new OfficeFileException("FAT chain sector is outside the FAT.");
        var sector = fat[(int)index];
        if (cachedFat != sector)
        {
            ReadAt(((long)sector + 1) * sectorSize, fatBuffer);
            cachedFat = sector;
        }
        return U32(fatBuffer, (int)(id % (sectorSize / 4)) * 4);
    }

    private uint NextMini(uint id)
    {
        var index = id / (uint)(sectorSize / 4);
        if (index >= miniFat.Count)
            throw new OfficeFileException("Mini FAT chain sector is outside the mini FAT.");
        return U32(ReadSector(miniFat[(int)index]), (int)(id % (sectorSize / 4)) * 4);
    }

    private List<uint> Chain(uint start, bool mini, string kind)
    {
        var chain = new List<uint>();
        var seen = new HashSet<uint>();
        var id = start;
        while (id != End)
        {
            if (mini)
            {
                if (id > 0xFFFFFFFA || (ulong)id * 64 >= Root.Length)
                    throw new OfficeFileException($"{kind} sector number is past the end of the mini stream.");
            }
            else
                CheckSector(id, kind);
            if (!seen.Add(id))
                throw new OfficeFileException($"{kind} chain loops.");
            if (chain.Count >= (mini ? (long)(Root.Length / 64) : sectorCount))
                throw new OfficeFileException($"{kind} chain exceeds the available sectors.");
            chain.Add(id);
            id = mini ? NextMini(id) : Next(id);
        }
        return chain;
    }

    private static void CheckLength(ulong length, int sectors, int size, string kind)
    {
        if (length > (ulong)sectors * (ulong)size ||
            (length + (ulong)size - 1) / (ulong)size != (ulong)sectors)
            throw new OfficeFileException($"{kind} stream size does not match its chain length.");
    }

    internal byte[] ReadStream(CompoundEntry entry) => OfficeFileException.Read(() =>
    {
        lock (gate)
        {
            if (disposed)
                throw new OfficeFileException("The compound file is closed.");
            // An empty stream has nothing to read, whatever its start sector says: a writer can leave something other
            // than end-of-chain there, and refusing the whole file over it would refuse a file with nothing wrong in it.
            if (entry.Length == 0)
                return [];
            var mini = entry.Length < 4096;
            var chain = Chain(entry.Start, mini, mini ? "Mini FAT" : "FAT");
            var size = mini ? 64 : sectorSize;
            CheckLength(entry.Length, chain.Count, size, entry.Name);
            if (entry.Length > (ulong)Array.MaxLength)
                throw new OfficeFileException("Stream exceeds the maximum byte-array length; it cannot be returned as bytes.");
            // Only a fully walked, file-bounded chain authorises this allocation, never the directory's claim.
            var output = new byte[(int)entry.Length];
            var offset = 0;
            foreach (var id in chain)
            {
                var count = Math.Min(size, output.Length - offset);
                var position = mini
                    ? ((long)miniStream[(int)((ulong)id * 64 / (ulong)sectorSize)] + 1) * sectorSize + (long)((ulong)id * 64 % (ulong)sectorSize)
                    : ((long)id + 1) * sectorSize;
                ReadAt(position, output.AsSpan(offset, count));
                offset += count;
            }
            return output;
        }
    });

    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    private static ulong U64(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset, 8));

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
            if (!leaveOpen)
                source.Dispose();
        }
    }
}
