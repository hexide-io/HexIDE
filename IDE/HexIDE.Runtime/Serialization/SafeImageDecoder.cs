using System;
using System.IO;
using Avalonia.Media.Imaging;

namespace HexIDE.Runtime.Serialization;

/// <summary>
/// Decodes a small, untrusted PNG (e.g. an add-in's publisher logo) into an Avalonia <see cref="Bitmap"/>,
/// hardened against hostile input. The bytes are authenticated (hash-verified) but the CONTENT is
/// attacker-controlled, so this is an untrusted-input path:
/// <list type="bullet">
/// <item>a byte-size cap, checked before anything else;</item>
/// <item>PNG-only via a magic-byte gate — raster-only excludes SVG's script/XXE class entirely;</item>
/// <item>a pixel-dimension cap read straight from the PNG <c>IHDR</c> header (width and height are
/// big-endian <c>uint32</c> at fixed offsets) <b>before any pixel buffer is allocated</b> — the
/// decompression-bomb defense, so a tiny file cannot declare a gigapixel canvas and OOM the host;</item>
/// <item>every failure path returns <c>null</c>; this method never throws.</item>
/// </list>
/// Because the format is locked to PNG, the dimensions come from the header with no image-decoding
/// dependency. Avalonia 12.0.4 has no bounded-decode API, so the dimension cap is what keeps the
/// subsequent full decode bounded — a capped 1024×1024 image is at most ~4 MB.
/// </summary>
public static class SafeImageDecoder
{
    /// <summary>Default byte cap (1 MB).</summary>
    public const int DefaultMaxBytes = 1024 * 1024;
    /// <summary>Default per-axis pixel-dimension cap (1024 px).</summary>
    public const int DefaultMaxDimension = 1024;

    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Reads <paramref name="path"/> — rejecting it <i>unread</i> if it exceeds the byte cap —
    /// and decodes it as a bounded PNG. Returns null if the file is missing, too large, not a PNG,
    /// malformed, or over the dimension cap.</summary>
    public static Bitmap? DecodeBoundedPngFile(string path,
        int maxBytes = DefaultMaxBytes, int maxDimension = DefaultMaxDimension)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length == 0 || fi.Length > maxBytes)
                return null;
            return DecodeBoundedPng(File.ReadAllBytes(path), maxBytes, maxDimension);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Decodes <paramref name="bytes"/> as a bounded PNG. See the type summary for the gates.
    /// Returns null on any rejection or decode failure; never throws.</summary>
    public static Bitmap? DecodeBoundedPng(byte[]? bytes,
        int maxBytes = DefaultMaxBytes, int maxDimension = DefaultMaxDimension)
    {
        if (!IsAcceptablePng(bytes, maxBytes, maxDimension))
            return null;
        try
        {
            return new Bitmap(new MemoryStream(bytes!));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The Avalonia-free safety gate: true iff <paramref name="bytes"/> are a non-empty PNG
    /// within the byte cap whose IHDR dimensions are positive and within the per-axis cap. Pure and
    /// side-effect-free — the unit-testable core of the decode, evaluated <i>before</i> any pixel buffer
    /// is allocated, so a hostile image is rejected without ever materialising it.</summary>
    public static bool IsAcceptablePng(byte[]? bytes,
        int maxBytes = DefaultMaxBytes, int maxDimension = DefaultMaxDimension)
    {
        if (bytes is null || bytes.Length == 0 || bytes.Length > maxBytes)
            return false;
        if (!TryReadPngDimensions(bytes, out var width, out var height))
            return false;
        // width/height come straight from the IHDR; an out-of-range value (incl. one with bit 31 set,
        // which reads back negative) is rejected here.
        return width > 0 && height > 0 && width <= maxDimension && height <= maxDimension;
    }

    // PNG layout: an 8-byte signature, then the IHDR chunk — a 4-byte length, the ASCII tag "IHDR",
    // a 4-byte big-endian width, then a 4-byte big-endian height. Width/height therefore sit at byte
    // offsets 16 and 20. Reading them does not parse any attacker-influenced structure beyond a bounds
    // check, so it is safe to do before deciding whether to hand the bytes to the real decoder.
    private static bool TryReadPngDimensions(byte[] b, out int width, out int height)
    {
        width = height = 0;
        if (b.Length < 24)
            return false;
        for (var i = 0; i < PngMagic.Length; i++)
            if (b[i] != PngMagic[i])
                return false;
        if (b[12] != (byte)'I' || b[13] != (byte)'H' || b[14] != (byte)'D' || b[15] != (byte)'R')
            return false;
        width = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
        height = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
        return true;
    }
}
