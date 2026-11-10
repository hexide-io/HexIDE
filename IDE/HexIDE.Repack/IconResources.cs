using System.Buffers.Binary;
using HexIDE.IDE;
using SkiaSharp;

namespace HexIDE.Repack;

/// <summary>The executable's icon, made from the brand's logo.</summary>
internal static class IconResources
{
    // The group Windows takes for the application's icon, and the one stock's executable uses.
    private const int GroupName = 32512;

    // The frames stock's own icon has (tools/IcoGen). PNG frames, as there.
    private static readonly int[] Sizes = [16, 32, 48, 256];

    private const int GroupHeaderSize = 6;
    private const int GroupEntrySize = 14;

    /// <summary>The bytes of a brand's logo, which the checks on the way in have already found to be there.</summary>
    public static byte[] LogoOf(DistributionIdentity brand)
    {
        using var logo = new MemoryStream();
        using (var source = brand.OpenLogo()!)
            source.CopyTo(logo);
        return logo.ToArray();
    }

    /// <summary>The logo as a PNG at each of the given sizes, or at the sizes stock's own icon has.</summary>
    public static IReadOnlyList<(int Size, byte[] Png)> Render(byte[] logoPng, IReadOnlyList<int>? sizes = null)
    {
        using var source = SKBitmap.Decode(logoPng) ?? throw new RepackException("The brand's logo could not be decoded as an image.");
        using var image = SKImage.FromBitmap(source);
        return (sizes ?? Sizes).Select(size => (size, Render(image, size))).ToList();
    }

    public static IReadOnlyList<NativeResource> FromLogo(byte[] logoPng)
    {
        var frames = Render(logoPng);
        var result = new List<NativeResource>();
        var group = new byte[GroupHeaderSize + Sizes.Length * GroupEntrySize];
        BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(2), 1); // An icon, where 2 would be a cursor.
        BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(4), (ushort)Sizes.Length);

        for (var i = 0; i < frames.Count; i++)
        {
            var (size, frame) = frames[i];
            var name = i + 1;
            result.Add(new NativeResource(PeImage.TypeIcon, name, 0, frame));

            var entry = group.AsSpan(GroupHeaderSize + i * GroupEntrySize);
            entry[0] = entry[1] = (byte)(size == 256 ? 0 : size); // The format's way of writing 256.
            BinaryPrimitives.WriteUInt16LittleEndian(entry[4..], 1);  // Planes.
            BinaryPrimitives.WriteUInt16LittleEndian(entry[6..], 32); // Bits per pixel.
            BinaryPrimitives.WriteInt32LittleEndian(entry[8..], frame.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[12..], (ushort)name);
        }

        result.Add(new NativeResource(PeImage.TypeGroupIcon, GroupName, 0, group));
        return result;
    }

    // A logo that isn't square sits in the middle of a transparent square, and keeps its shape.
    private static byte[] Render(SKImage image, int size)
    {
        var info = new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.Transparent);

        var scale = (float)size / Math.Max(image.Width, image.Height);
        var width = image.Width * scale;
        var height = image.Height * scale;
        var target = SKRect.Create((size - width) / 2, (size - height) / 2, width, height);
        surface.Canvas.DrawImage(image, target, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        surface.Canvas.Flush();

        using var snapshot = surface.Snapshot();
        using var encoded = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }
}
