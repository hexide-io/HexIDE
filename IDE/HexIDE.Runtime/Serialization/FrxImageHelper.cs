using System;
using System.IO;
using Avalonia.Media.Imaging;

namespace HexIDE.Runtime.Serialization;

/// <summary>
/// Helpers for decoding .frx blob bytes into Avalonia images.
/// </summary>
public static class FrxImageHelper
{
    // BMP magic: "BM"
    private static readonly byte[] BmpMagic = [0x42, 0x4D];
    // ICO magic: 00 00 01 00
    private static readonly byte[] IcoMagic = [0x00, 0x00, 0x01, 0x00];
    // CUR magic: 00 00 02 00
    private static readonly byte[] CurMagic = [0x00, 0x00, 0x02, 0x00];

    public enum FrxBlobKind
    {
        Bitmap,
        Icon,
        Cursor,
        Unknown,
    }

    public static FrxBlobKind DetectKind(byte[] blob)
    {
        if (blob.Length >= 2 && blob[0] == BmpMagic[0] && blob[1] == BmpMagic[1])
            return FrxBlobKind.Bitmap;
        if (blob.Length >= 4 && blob[0] == IcoMagic[0] && blob[1] == IcoMagic[1]
                             && blob[2] == IcoMagic[2] && blob[3] == IcoMagic[3])
            return FrxBlobKind.Icon;
        if (blob.Length >= 4 && blob[0] == CurMagic[0] && blob[1] == CurMagic[1]
                             && blob[2] == CurMagic[2] && blob[3] == CurMagic[3])
            return FrxBlobKind.Cursor;
        return FrxBlobKind.Unknown;
    }

    /// <summary>
    /// Attempts to decode a blob as an Avalonia Bitmap.
    /// Returns null for unknown/OLE blobs or on decoding failure.
    /// </summary>
    public static Bitmap? TryDecodeToAvaloniaBitmap(byte[]? blob)
    {
        if (blob == null || blob.Length == 0)
            return null;

        var kind = DetectKind(blob);
        if (kind is not (FrxBlobKind.Bitmap or FrxBlobKind.Icon))
            return null;

        try
        {
            return new Bitmap(new MemoryStream(blob));
        }
        catch (Exception)
        {
            return null;
        }
    }
}
