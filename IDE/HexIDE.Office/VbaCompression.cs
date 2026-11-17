using System.Buffers.Binary;

namespace HexIDE.Office;

/// <summary>MS-OVBA 2.4.1 compressed containers. No performance-cache bytes are interpreted.</summary>
public static class VbaCompression
{
    public const int MaximumOutputBytes = 16 * 1024 * 1024;

    public static byte[] Decompress(ReadOnlySpan<byte> container, int maximumOutputBytes = MaximumOutputBytes)
    {
        if (maximumOutputBytes < 0 || maximumOutputBytes > MaximumOutputBytes)
            throw new OfficeFileException("Invalid VBA decompressed output limit (maximum 16 MiB).");
        if (container.IsEmpty || container[0] != 1)
            throw new OfficeFileException("Invalid VBA compressed container signature.");
        using var output = new MemoryStream();
        var chunk = new byte[4096];
        var position = 1;
        while (position < container.Length)
        {
            if (container.Length - position < 2)
                throw new OfficeFileException("Truncated VBA chunk header.");
            var header = BinaryPrimitives.ReadUInt16LittleEndian(container[position..]);
            var size = (header & 0xFFF) + 3;
            if ((header & 0x7000) != 0x3000)
                throw new OfficeFileException("Invalid VBA chunk signature.");
            if (size > container.Length - position)
                throw new OfficeFileException("VBA chunk runs past the stream end.");
            var end = position + size;
            position += 2;
            var count = 0;
            if ((header & 0x8000) == 0)
            {
                if (size != 4098)
                    throw new OfficeFileException("A raw VBA chunk must contain 4096 bytes.");
                container.Slice(position, 4096).CopyTo(chunk);
                count = 4096;
                position = end;
            }
            else
            {
                while (position < end)
                {
                    var flags = container[position++];
                    for (var bit = 0; bit < 8 && position < end; ++bit)
                    {
                        if ((flags & (1 << bit)) == 0)
                        {
                            if (count == chunk.Length)
                                throw new OfficeFileException("VBA chunk expands past 4096 bytes.");
                            chunk[count++] = container[position++];
                        }
                        else
                        {
                            if (end - position < 2)
                                throw new OfficeFileException("Truncated VBA copy token.");
                            var token = BinaryPrimitives.ReadUInt16LittleEndian(container[position..]);
                            position += 2;
                            var offsetBits = 4;
                            while ((1 << offsetBits) < count)
                                ++offsetBits;
                            var lengthBits = 16 - offsetBits;
                            var offset = (token >> lengthBits) + 1;
                            var length = (token & ((1 << lengthBits) - 1)) + 3;
                            if (offset > count)
                                throw new OfficeFileException("VBA copy token reaches before the chunk start.");
                            if (length > chunk.Length - count)
                                throw new OfficeFileException("VBA copy token expands past 4096 bytes.");
                            for (var i = 0; i < length; ++i)
                            {
                                chunk[count] = chunk[count - offset];
                                ++count;
                            }
                        }
                    }
                }
            }
            if (count > maximumOutputBytes - output.Length)
                throw new OfficeFileException($"VBA decompressed output exceeds the {maximumOutputBytes}-byte limit (maximum 16 MiB).");
            output.Write(chunk, 0, count);
        }
        return output.ToArray();
    }
}
