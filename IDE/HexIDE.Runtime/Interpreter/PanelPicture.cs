using System.IO;
using Avalonia.Media.Imaging;
using HexIDE.Runtime.Serialization;
using HexIDE.Imaging;

namespace HexIDE.Runtime.Interpreter;

// #1087, issuecomment-6428228813: saved panel pictures contain the original BMP/ICO file.
internal sealed class PanelPicture : ICSharpProxy, ICSharpPropertyBag
{
    private readonly Lazy<Bitmap?> bitmap;

    internal byte[] FileBytes { get; }

    /// <summary>The pixel size a PNG's IHDR declares, for one that doesn't decode (#4967); null for any other picture.</summary>
    private Avalonia.PixelSize? DeclaredSize { get; init; }

    // The size in a PNG's IHDR, which follows its 8-byte signature as the first chunk: a 4-byte length, "IHDR", then the
    // width and height, big-endian. Null unless the whole chunk is there, 33 bytes in all.
    private static Avalonia.PixelSize? PngDeclaredSize(byte[] file) =>
        file.Length >= 33 && file.AsSpan(12, 4).SequenceEqual("IHDR"u8)
            ? new Avalonia.PixelSize((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(16)),
                (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(20)))
            : null;

    internal PanelPicture(byte[] file, bool resourceIcon = false)
    {
        FileBytes = file;
        bitmap = new(() =>
        {
            // A picture holding a PNG was let in by FromFile's png, so it is drawn (#4967).
            var decoded = FrxImageHelper.TryDecodeToAvaloniaBitmap(file, png: true);
            if (!resourceIcon || decoded is null || decoded.PixelSize == new Avalonia.PixelSize(32, 32)) return decoded;
            using (decoded) return decoded.CreateScaledBitmap(new Avalonia.PixelSize(32, 32));
        });
    }

    internal static PanelPicture FromRecord(byte[] record)
    {
        var clsid = new Guid("0BE35204-8F91-11CE-9DE3-00AA004BB851").ToByteArray();
        if (record.Length < 28 || BitConverter.ToInt32(record, 0) != record.Length - 4
            || !record.AsSpan(4, 16).SequenceEqual(clsid)
            || BitConverter.ToInt32(record, 20) != 0x746c
            || BitConverter.ToInt32(record, 24) != record.Length - 28)
            throw new NotImplementedException("This Panel.Picture record is not implemented (#4882)");
        return FromFile(record[28..]);
    }

    /// <param name="png">
    /// Read a PNG, as RDCore's <c>LoadPicture</c> does (measured under RDC, #4967): it is a bitmap picture, Type 1. VB6
    /// refuses PNG with 481 (measured). So only a run that takes RDCore's additions sets this: for its
    /// <c>LoadPicture</c>, and to read back the picture a control was given. A Panel's record never does.
    /// </param>
    internal static PanelPicture FromFile(byte[] file, bool loadPicture = false, bool png = false)
    {
        var kind = FrxImageHelper.DetectKind(file);
        if (png && kind == FrxImageHelper.FrxBlobKind.Png)
        {
            var decoded = new PanelPicture(file);
            if (decoded.Bitmap is not null)
                return decoded;
            // RDC reads a PNG cut short by its header (measured, #4967): with its signature and a whole IHDR it is a
            // picture of the size IHDR declares, with no error; cut into IHDR it is 481. What such a picture draws is
            // unmeasured, and here it draws nothing (reasoned).
            return PngDeclaredSize(file) is { } declared
                ? new PanelPicture(file) { DeclaredSize = declared }
                : throw new VBRunTimeException(VBStandardError.InvalidPicture);
        }
        // Metafiles and cursors have no LoadPicture measurements (#4597).
        if (kind == FrxImageHelper.FrxBlobKind.Cursor
            || file.AsSpan().StartsWith(new byte[] { 0xd7, 0xcd, 0xc6, 0x9a })
            || file.AsSpan().StartsWith(new byte[] { 1, 0, 9, 0 })
            || file.AsSpan().StartsWith(new byte[] { 2, 0, 9, 0 })
            || (file.Length >= 44 && file.AsSpan().StartsWith(new byte[] { 1, 0, 0, 0 })
                && file.AsSpan(40, 4).SequenceEqual(" EMF"u8)))
            throw new NotImplementedException("LoadPicture metafiles and cursors are unmeasured (#4597)");
        if (kind is not (FrxImageHelper.FrxBlobKind.Bitmap or FrxImageHelper.FrxBlobKind.Icon
            or FrxImageHelper.FrxBlobKind.Jpeg or FrxImageHelper.FrxBlobKind.Gif))
            throw new VBRunTimeException(VBStandardError.InvalidPicture);
        if (loadPicture && kind == FrxImageHelper.FrxBlobKind.Icon)
            file = LoadPictureIcon(file);
        var picture = new PanelPicture(file);
        if (picture.Bitmap == null)
            throw new NotImplementedException("This Panel.Picture image is not implemented (#4882)");
        return picture;
    }

    private static byte[] LoadPictureIcon(byte[] file)
    {
        // #4623 / #4748: SP6 takes the first 32-by-32 image, or the first image if none is 32-by-32.
        if (file.Length < 6) return file;
        var count = BitConverter.ToUInt16(file, 4);
        if (count == 0 || file.Length < 6 + count * 16) return file;
        var selected = -1;
        for (var entry = 6; entry < 6 + count * 16; entry += 16)
        {
            var length = BitConverter.ToUInt32(file, entry + 8);
            var offset = BitConverter.ToUInt32(file, entry + 12);
            if ((ulong)offset + length > (ulong)file.Length) return file;
            if (count == 1 || (selected < 0 && file[entry] == 32 && file[entry + 1] == 32))
                selected = entry;
        }
        if (selected < 0)
            selected = 6;
        var size = (int)BitConverter.ToUInt32(file, selected + 8);
        var start = (int)BitConverter.ToUInt32(file, selected + 12);
        var image = file.AsSpan(start, size);
        if (image.StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47 }))
            throw new VBRunTimeException(VBStandardError.InvalidPicture);
        if (image.Length >= 40 && BitConverter.ToUInt32(image) == 40)
        {
            var width = Math.Abs((long)BitConverter.ToInt32(image[4..]));
            var height = Math.Abs((long)BitConverter.ToInt32(image[8..])) / 2;
            var depth = BitConverter.ToUInt16(image[14..]);
            var colours = BitConverter.ToUInt32(image[32..]);
            var palette = (colours != 0 ? colours : depth <= 8 ? 1L << depth : 0) * 4;
            var xor = (width * depth + 31) / 32 * 4 * (decimal)height;
            var and = (width + 31) / 32 * 4 * height;
            if (image.Length < 40 + palette + xor + and)
                throw new VBRunTimeException(VBStandardError.InvalidPicture);
        }
        if (count == 1) return file;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)1);
        writer.Write(file.AsSpan(selected, 12)); writer.Write(22); writer.Write(image);
        return stream.ToArray();
    }

    internal static PanelPicture FromResourceBitmap(byte[] dib)
    {
        // Only the measured 24-bit BITMAPINFOHEADER layout is supported.
        if (dib.Length < 40 || BitConverter.ToInt32(dib, 0) != 40
            || BitConverter.ToInt16(dib, 12) != 1 || BitConverter.ToInt16(dib, 14) != 24
            || BitConverter.ToInt32(dib, 16) != 0 || BitConverter.ToInt32(dib, 32) != 0)
            throw new NotImplementedException("This resource bitmap layout is unmeasured (#2626)");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0x4d42);
        writer.Write(14 + dib.Length);
        writer.Write(0);
        writer.Write(54);
        writer.Write(dib);
        return FromFile(stream.ToArray());
    }

    internal static PanelPicture FromResourceGroup(byte[] group, bool cursor, Func<ushort, byte[]?> image)
    {
        // #3969: 16/32 groups select 32; 16-only groups stretch to 32 (an-icon-pictures-size-and-the-image-it-draws.md).
        // Without 32 but with a larger image, selecting the largest remains unmeasured.
        static NotImplementedException Unsupported() => new("This resource icon/cursor layout is not implemented (#2626)");
        if (group.Length < 6 || BitConverter.ToUInt16(group, 0) != 0
            || BitConverter.ToUInt16(group, 2) != (cursor ? 2 : 1)) throw Unsupported();
        var count = BitConverter.ToUInt16(group, 4);
        if (count == 0 || group.Length != 6 + count * 14) throw Unsupported();
        var selected = 6;
        int Width(int offset) => cursor ? BitConverter.ToUInt16(group, offset) : group[offset] == 0 ? 256 : group[offset];
        for (var offset = 20; offset < group.Length; offset += 14)
            if (Width(selected) != 32 && (Width(offset) == 32 || Width(offset) > Width(selected))) selected = offset;
        var data = image(BitConverter.ToUInt16(group, selected + 12));
        if (data is null) throw Unsupported();
        if (cursor)
        {
            if (data.Length < 4) throw Unsupported();
            data = data[4..]; // RT_CURSOR stores the hotspot ahead of the DIB.
        }
        if (data.Length < 40 || BitConverter.ToInt32(data, 0) != 40
            || BitConverter.ToUInt16(data, 12) != 1 || BitConverter.ToUInt16(data, 14) != (cursor ? 1 : 32)
            || BitConverter.ToInt32(data, 16) != 0) throw Unsupported();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)1);
        var width = BitConverter.ToInt32(data, 4);
        var height = BitConverter.ToInt32(data, 8) / 2;
        writer.Write((byte)width); writer.Write((byte)height); writer.Write((ushort)0);
        writer.Write(BitConverter.ToUInt16(data, 12)); writer.Write(BitConverter.ToUInt16(data, 14));
        writer.Write(data.Length); writer.Write(22); writer.Write(data);
        var picture = new PanelPicture(stream.ToArray(), resourceIcon: true);
        if (picture.Bitmap is null) throw Unsupported();
        return picture;
    }

    internal Bitmap? Bitmap => bitmap.Value;

    internal Avalonia.PixelSize? PixelSize => Bitmap?.PixelSize ?? DeclaredSize;

    /// <param name="png">Whether PNG is read too, which RDCore's <c>LoadPicture</c> does (#4967); see <see cref="FromFile"/>.</param>
    internal static Vb6Value Load(IReadOnlyList<Vb6Value> args, VbFiles files, bool png = false)
    {
        // Clearing with LoadPicture() keeps an AutoSize PictureBox's dimensions (#922).
        if (args.Count == 0)
            return new Vb6Value(new PanelPicture([]));
        if (args.Count != 1 || args[0].Type != Vb6Value.ValueType.String || args[0].Value is not string { Length: > 0 } path)
            throw new NotImplementedException("This LoadPicture argument combination is unmeasured (#4882)");
        try
        {
            var fullPath = files.FullPath(path);
            if (Directory.Exists(fullPath))
                throw FileError(VBStandardError.PathFileAccessError);
            return new Vb6Value(FromFile(File.ReadAllBytes(fullPath), loadPicture: true, png: png));
        }
        catch (FileNotFoundException) { throw FileError(VBStandardError.FileNotFound); }
        catch (DirectoryNotFoundException) { throw FileError(VBStandardError.PathNotFound); }
        catch (IOException) { throw new NotImplementedException("LoadPicture file errors are unmeasured (#4882)"); }
        catch (UnauthorizedAccessException) { throw new NotImplementedException("LoadPicture file errors are unmeasured (#4882)"); }

        VBRunTimeException FileError(VBStandardError error) => new(new VBStandardError(error.ErrNo, $"{error.Description}: '{path}'"));
    }

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        value = default;
        // Empty Type is measured by #3426; native handles remain refused (#2626).
        if (FileBytes.Length == 0 && name.Equals("Type", StringComparison.OrdinalIgnoreCase))
        {
            value = new Vb6Value((short)0);
            return true;
        }
        if (PixelSize is { } size)
        {
            if (name.Equals("Type", StringComparison.OrdinalIgnoreCase))
                value = new Vb6Value((short)(FrxImageHelper.DetectKind(FileBytes) == FrxImageHelper.FrxBlobKind.Icon ? 3 : 1));
            else if (name.Equals("Width", StringComparison.OrdinalIgnoreCase))
                value = new Vb6Value((long)Math.Round(size.Width * 2540d / 96));
            else if (name.Equals("Height", StringComparison.OrdinalIgnoreCase))
                value = new Vb6Value((long)Math.Round(size.Height * 2540d / 96));
            else throw new NotImplementedException($"Panel.Picture.{name} is not implemented (#4882)");
            return true;
        }
        throw new NotImplementedException($"Panel.Picture.{name} is not implemented (#4882)");
    }

    public bool TrySetProperty(string name, Vb6Value value) => throw new NotImplementedException($"Panel.Picture.{name} is not implemented (#4882)");
    public void Call(string method, List<Vb6Value> args) => throw new NotImplementedException($"Panel.Picture.{method} is not implemented (#4882)");
}
