using System;
using System.IO;
using Avalonia.Media.Imaging;

namespace HexIDE.Imaging;

/// <summary>
/// Helpers for decoding .frx blob bytes into Avalonia images.
/// </summary>
/// <remarks>
/// The IDE and the Basic provider both decode with these, so there is one copy of the checks (#3411). See this project's
/// README: a program made with File &gt; Make carries this assembly in its runner.
/// </remarks>
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
        Jpeg,
        Gif,
        /// <summary>
        /// VB6 refuses PNG (481, measured), and so does every decoder here unless its caller asks for it: only RDCore's
        /// <c>LoadPicture</c> reads one (#4967).
        /// </summary>
        Png,
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
        if (blob.AsSpan().StartsWith(new byte[] { 0xff, 0xd8, 0xff }))
            return FrxBlobKind.Jpeg;
        if (blob.AsSpan().StartsWith("GIF87a"u8) || blob.AsSpan().StartsWith("GIF89a"u8))
            return FrxBlobKind.Gif;
        if (blob.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }))
            return FrxBlobKind.Png;
        return FrxBlobKind.Unknown;
    }

    /// <summary>The image bytes of a complete VB6-framed StdPicture, or null for an opaque or empty record.</summary>
    public static byte[]? PicturePayload(byte[]? record)
    {
        if (record == null || RecordPayloadOffset(record) != 4
            || StdPictureHeaderLength(record, 4) != 8
            || BitConverter.ToInt32(record, 8) != record.Length - 12
            || record.Length == 12)
            return null;
        return record[12..];
    }

    /// <summary>
    /// Where the payload starts inside a .frx record: 4 when its four-byte length prefix exactly accounts for the rest of
    /// the record, which is true of every picture record and false of every List and ItemData record, and 0 otherwise.
    /// </summary>
    public static int RecordPayloadOffset(byte[] record)
    {
        if (record.Length < 4) return 0;
        var declared = BitConverter.ToInt32(record, 0);
        return declared == record.Length - 4 ? 4 : 0;
    }

    /// <summary>How many bytes to skip before the image itself: 8 for a StdPicture stream, 0 for anything else.</summary>
    public static int StdPictureHeaderLength(byte[] bytes, int from = 0)
    {
        const uint StdPicturePreamble = 0x0000746C;
        if (bytes.Length - from < 8) return 0;
        var preamble = (uint)(bytes[from] | (bytes[from + 1] << 8) | (bytes[from + 2] << 16) | (bytes[from + 3] << 24));
        return preamble == StdPicturePreamble ? 8 : 0;
    }

    /// <summary>
    /// Attempts to decode a blob as an Avalonia Bitmap.
    /// Returns null for unknown/OLE blobs or on decoding failure.
    /// </summary>
    /// <param name="png">
    /// Decode a PNG too. Only a picture in a run that takes RDCore's additions asks (#4967): the designer's own Load
    /// Picture refuses PNG, as VB6's does.
    /// </param>
    public static Bitmap? TryDecodeToAvaloniaBitmap(byte[]? blob, bool png = false)
    {
        if (blob == null || blob.Length == 0)
            return null;

        var payload = PicturePayload(blob) ?? blob;
        var kind = DetectKind(payload);
        if (kind is not (FrxBlobKind.Bitmap or FrxBlobKind.Icon or FrxBlobKind.Jpeg or FrxBlobKind.Gif)
            && !(png && kind == FrxBlobKind.Png))
            return null;

        try
        {
            using var stream = new MemoryStream(payload);
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
