using System.IO;
using HexIDE.Imaging;
using HexIDE.Runtime.Serialization;
using SkiaSharp;

namespace HexIDE.Runtime.Interpreter;

// VB6 SP6 bytes: #992, issuecomment-7094565387. Unsupported layouts must not be decoded and silently flattened.
internal static class PictureWriter
{
    internal static NotImplementedException Unmeasured() => new("SavePicture for this picture or destination is not implemented (#992)");

    internal static void Save(IReadOnlyList<Vb6Value> args, VbFiles files)
    {
        if (args.Count != 2 || args[0].Value is not PanelPicture picture || args[1].Value is not string path)
            throw Unmeasured();
        if (picture.FileBytes.Length == 0)
            throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
        if (picture.SaveLayoutUnmeasured) throw Unmeasured();
        byte[] bytes;
        try { bytes = Encode(picture.FileBytes); }
        catch (EndOfStreamException) { throw Unmeasured(); }
        catch (OverflowException) { throw Unmeasured(); }
        string? temporary = null;
        try
        {
            var destination = files.FullPath(path);
            if (File.Exists(destination)) throw Unmeasured();
            temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".savepicture-" + Guid.NewGuid());
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, destination);
        }
        catch (DirectoryNotFoundException)
        {
            throw new VBRunTimeException(new VBStandardError(76, $"Path not found: '{path}'"));
        }
        catch (IOException) { throw Unmeasured(); }
        catch (UnauthorizedAccessException) { throw Unmeasured(); }
        catch (ArgumentException) { throw Unmeasured(); }
        catch (NotSupportedException) { throw Unmeasured(); }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static byte[] Header(int width, int height, int depth)
    {
        var offset = depth == 8 ? 1078 : 54;
        var length = checked((width * (depth / 8) + 3) / 4 * 4 * height);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0x4d42); writer.Write(checked(offset + length)); writer.Write(0); writer.Write(offset);
        writer.Write(40); writer.Write(width); writer.Write(height); writer.Write((ushort)1); writer.Write((ushort)depth);
        writer.Write(0); writer.Write(length); writer.Write(new byte[16]);
        return stream.ToArray();
    }

    private static byte[] Encode(byte[] source)
    {
        switch (FrxImageHelper.DetectKind(source))
        {
            case FrxImageHelper.FrxBlobKind.Bitmap: return Bitmap(source);
            case FrxImageHelper.FrxBlobKind.Gif: return Gif(source);
            case FrxImageHelper.FrxBlobKind.Icon:
                if (source.Length < 22 || BitConverter.ToUInt16(source, 4) != 1 || source[6] != 32 || source[7] != 32)
                    throw Unmeasured();
                var icon = (byte[])source.Clone();
                icon[9] = 0;
                return icon;
            case FrxImageHelper.FrxBlobKind.Jpeg:
                using (var image = SKBitmap.Decode(source))
                {
                    if (image is null) throw Unmeasured();
                    using var stream = new MemoryStream();
                    stream.Write(Header(image.Width, image.Height, 24));
                    var row = new byte[(image.Width * 3 + 3) / 4 * 4];
                    for (var y = image.Height - 1; y >= 0; y--)
                    {
                        for (var x = 0; x < image.Width; x++)
                        {
                            var colour = image.GetPixel(x, y);
                            row[x * 3] = colour.Blue; row[x * 3 + 1] = colour.Green; row[x * 3 + 2] = colour.Red;
                        }
                        stream.Write(row);
                    }
                    return stream.ToArray();
                }
            default: throw Unmeasured();
        }
    }

    private static byte[] Bitmap(byte[] source)
    {
        if (source.Length < 54 || BitConverter.ToInt32(source, 14) != 40 || BitConverter.ToUInt16(source, 26) != 1
            || BitConverter.ToInt32(source, 30) != 0) throw Unmeasured();
        var width = BitConverter.ToInt32(source, 18);
        var height = BitConverter.ToInt32(source, 22);
        var depth = BitConverter.ToUInt16(source, 28);
        var offset = BitConverter.ToInt32(source, 10);
        if (width <= 0 || height <= 0 || depth is not (8 or 24 or 32) || offset < 54) throw Unmeasured();
        var stride = ((long)width * (depth / 8) + 3) / 4 * 4;
        if (offset + stride * height > source.Length) throw Unmeasured();
        if (depth == 8 && (offset < 1078 || BitConverter.ToUInt32(source, 46) is not (0 or 256))) throw Unmeasured();
        using var stream = new MemoryStream();
        stream.Write(Header(width, height, depth == 8 ? 8 : 24));
        if (depth == 8)
            for (var i = 0; i < 256; i++) { stream.Write(source, 54 + i * 4, 3); stream.WriteByte(0); }
        if (depth != 32)
            stream.Write(source, offset, checked((int)(stride * height)));
        else
        {
            var row = new byte[(width * 3 + 3) / 4 * 4];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++) Array.Copy(source, offset + y * stride + x * 4, row, x * 3, 3);
                stream.Write(row);
            }
        }
        return stream.ToArray();
    }

    private static byte[] Gif(byte[] source)
    {
        using var input = new BinaryReader(new MemoryStream(source));
        input.ReadBytes(6);
        var width = input.ReadUInt16(); var height = input.ReadUInt16();
        var packed = input.ReadByte();
        if ((packed & 0x87) != 0x87) throw Unmeasured();
        input.ReadBytes(2);
        var table = input.ReadBytes(768);
        var transparent = -1;
        byte[]? pixels = null;
        while (input.BaseStream.Position < input.BaseStream.Length)
        {
            var marker = input.ReadByte();
            if (marker == 0x3b) break;
            if (marker == 0x21)
            {
                if (transparent >= 0 || pixels is not null) throw Unmeasured();
                if (input.ReadByte() != 0xf9 || input.ReadByte() != 4) throw Unmeasured();
                var control = input.ReadByte(); input.ReadUInt16();
                transparent = input.ReadByte();
                if ((control & 1) == 0 || input.ReadByte() != 0) throw Unmeasured();
                continue;
            }
            if (marker != 0x2c || pixels is not null) throw Unmeasured();
            if (input.ReadUInt16() != 0 || input.ReadUInt16() != 0 || input.ReadUInt16() != width
                || input.ReadUInt16() != height || input.ReadByte() != 0) throw Unmeasured();
            var minimum = input.ReadByte();
            using var data = new MemoryStream();
            int length;
            while ((length = input.ReadByte()) != 0) data.Write(input.ReadBytes(length));
            pixels = DecodeIndexes(data.ToArray(), minimum, width * height);
        }
        if (pixels is null || transparent < 0 || pixels.Contains((byte)transparent)) throw Unmeasured();
        using var stream = new MemoryStream();
        stream.Write(Header(width, height, 8));
        for (var i = 0; i < 256; i++)
        {
            stream.WriteByte(i == transparent ? (byte)0 : table[i * 3 + 2]);
            stream.WriteByte(i == transparent ? (byte)0 : table[i * 3 + 1]);
            stream.WriteByte(i == transparent ? (byte)0 : table[i * 3]); stream.WriteByte(0);
        }
        for (var y = height - 1; y >= 0; y--)
        {
            stream.Write(pixels, y * width, width);
            stream.Write(new byte[(4 - width % 4) % 4]);
        }
        return stream.ToArray();
    }

    private static byte[] DecodeIndexes(byte[] data, int minimum, int count)
    {
        if (minimum is < 2 or > 8) throw Unmeasured();
        var clear = 1 << minimum;
        var dictionary = new byte[4096][];
        for (var i = 0; i < clear; i++) dictionary[i] = [(byte)i];
        var size = minimum + 1; var next = clear + 2; var bit = 0;
        byte[]? previous = null;
        using var pixels = new MemoryStream();
        while (bit + size <= data.Length * 8)
        {
            var code = 0;
            for (var i = 0; i < size; i++) code |= ((data[(bit + i) / 8] >> ((bit + i) % 8)) & 1) << i;
            bit += size;
            if (code == clear) { size = minimum + 1; next = clear + 2; previous = null; continue; }
            if (code == clear + 1)
            {
                if (pixels.Length != count) throw Unmeasured();
                return pixels.ToArray();
            }
            var entry = code < next ? dictionary[code] : code == next && previous is not null ? [.. previous, previous[0]] : null;
            if (entry is null || pixels.Length + entry.Length > count) throw Unmeasured();
            pixels.Write(entry);
            if (previous is not null && next < 4096)
            {
                dictionary[next++] = [.. previous, entry[0]];
                if (next == 1 << size && size < 12) size++;
            }
            previous = entry;
        }
        throw Unmeasured();
    }
}
