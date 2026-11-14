using System.IO;
using Avalonia.Media.Imaging;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Interpreter;

// #1087, issuecomment-6428228813: saved panel pictures contain the original BMP/ICO file.
internal sealed class PanelPicture : ICSharpProxy, ICSharpPropertyBag
{
    private readonly Lazy<Bitmap?> bitmap;

    internal byte[] FileBytes { get; }

    private PanelPicture(byte[] file, bool resourceIcon = false)
    {
        FileBytes = file;
        bitmap = new(() =>
        {
            var decoded = FrxImageHelper.TryDecodeToAvaloniaBitmap(file);
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
            throw new NotImplementedException("This Panel.Picture record is not implemented (#1087)");
        return FromFile(record[28..]);
    }

    internal static PanelPicture FromFile(byte[] file)
    {
        if (FrxImageHelper.DetectKind(file) is not (FrxImageHelper.FrxBlobKind.Bitmap or FrxImageHelper.FrxBlobKind.Icon))
            throw new NotImplementedException("This Panel.Picture file format is not implemented (#1087)");
        var picture = new PanelPicture(file);
        if (picture.Bitmap == null)
            throw new NotImplementedException("This Panel.Picture image is not implemented (#1087)");
        return picture;
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

    internal static Vb6Value Load(IReadOnlyList<Vb6Value> args, VbFiles files)
    {
        // Clearing with LoadPicture() keeps an AutoSize PictureBox's dimensions (#922).
        if (args.Count == 0)
            return new Vb6Value(new PanelPicture([]));
        if (args.Count != 1 || args[0].Type != Vb6Value.ValueType.String || args[0].Value is not string { Length: > 0 } path)
            throw new NotImplementedException("This LoadPicture argument combination is unmeasured (#1087)");
        try { return new Vb6Value(FromFile(File.ReadAllBytes(files.FullPath(path)))); }
        catch (IOException) { throw new NotImplementedException("LoadPicture file errors are unmeasured (#1087)"); }
        catch (UnauthorizedAccessException) { throw new NotImplementedException("LoadPicture file errors are unmeasured (#1087)"); }
    }

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        value = default;
        // Native handles and the rest of StdPicture remain refused (#2626).
        if (Bitmap is { } decoded)
        {
            if (name.Equals("Type", StringComparison.OrdinalIgnoreCase))
                value = new Vb6Value((short)(FrxImageHelper.DetectKind(FileBytes) == FrxImageHelper.FrxBlobKind.Icon ? 3 : 1));
            else if (name.Equals("Width", StringComparison.OrdinalIgnoreCase))
                value = new Vb6Value((long)Math.Round(decoded.PixelSize.Width * 2540d / 96));
            else if (name.Equals("Height", StringComparison.OrdinalIgnoreCase))
                value = new Vb6Value((long)Math.Round(decoded.PixelSize.Height * 2540d / 96));
            else throw new NotImplementedException($"Panel.Picture.{name} is not implemented (#1087)");
            return true;
        }
        throw new NotImplementedException($"Panel.Picture.{name} is not implemented (#1087)");
    }

    public bool TrySetProperty(string name, Vb6Value value) => throw new NotImplementedException($"Panel.Picture.{name} is not implemented (#1087)");
    public void Call(string method, List<Vb6Value> args) => throw new NotImplementedException($"Panel.Picture.{method} is not implemented (#1087)");
}
