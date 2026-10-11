using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HexIDE.IDE;

/// <summary>
/// The encoding of one source document. Keep it with the document across Save As and reloads.
/// A BOM is an explicit UTF-8 declaration; valid UTF-8 bytes alone are not one.
/// </summary>
/// <param name="CodePage">The code page the document's text is read and written in.</param>
/// <param name="Utf8Bom">
/// The file begins with the UTF-8 byte-order mark, which is kept on write. Normally with code page 65001.
/// With any other code page only for a file whose mark does not match its contents: the mark is kept in
/// front of whatever code page reads the rest, so the file's bytes survive and its first line still parses.
/// </param>
public sealed record Vb6TextEncoding(int CodePage, bool Utf8Bom = false)
{
    // VB6 uses the Windows ANSI code page, not the console/OEM code page or the UI language.
    // Other hosts have no Windows ACP; use Windows-1252 as the deterministic default there.
    public static Vb6TextEncoding Ansi { get; } = new(
        OperatingSystem.IsWindows()
            ? CodePagesEncodingProvider.Instance.GetEncoding(0)?.CodePage ?? 1252
            : 1252);

    public static Vb6TextEncoding Utf8 { get; } = new(65001);
    public static Vb6TextEncoding Utf8WithBom { get; } = new(65001, true);

    /// <summary>
    /// The last resort when nothing else reads a file. It maps all 256 byte values to characters and back,
    /// so a file read in it is always written back byte-for-byte, and an edit it cannot hold is refused.
    /// </summary>
    public static Vb6TextEncoding Latin1 { get; } = new(28591);

    internal Encoding GetEncoding()
    {
        if (CodePage == 65001)
            return new UTF8Encoding(false, true);
        return CodePagesEncodingProvider.Instance.GetEncoding(
            CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
            ?? Encoding.GetEncoding(CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }
}

/// <summary>
/// Reads VB6 source as ANSI unless a UTF-8 BOM or an explicit caller selection declares otherwise.
/// Never guesses an encoding from whether the bytes happen to be valid UTF-8, and never changes the
/// encoding to accommodate an edit. Unrepresentable edits fail before writing rather than becoming '?'
/// or silently converting the file into a format VB6 cannot read.
/// </summary>
public static class Vb6TextFile
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    public static Vb6TextEncoding DetectEncoding(byte[] bytes, Vb6TextEncoding? fallback = null) =>
        bytes.AsSpan().StartsWith(Utf8Bom)
            ? Vb6TextEncoding.Utf8WithBom
            : fallback is { Utf8Bom: true } ? fallback with { Utf8Bom = false } : fallback ?? Vb6TextEncoding.Ansi;

    public static string Decode(byte[] bytes, Vb6TextEncoding? encoding = null)
    {
        encoding ??= DetectEncoding(bytes);
        var offset = encoding.Utf8Bom && bytes.AsSpan().StartsWith(Utf8Bom) ? Utf8Bom.Length : 0;
        var codec = encoding.GetEncoding();
        var text = codec.GetString(bytes, offset, bytes.Length - offset);
        // Defence, not a case that occurs: with .NET's tables a strict decode always encodes back to the
        // same bytes. Measured 2026-10-11 over every 1- and 2-byte input in code pages 874, 932, 936, 949,
        // 950 and 1250-1258. Where a double-byte page spells one character two ways, such as 932's 87 90
        // beside 81 E0 for U+2252, the strict decoder itself refuses the spelling the encoder does not
        // write. Kept so a provider that maps differently is refused here rather than written back changed.
        if (!codec.GetBytes(text).AsSpan().SequenceEqual(bytes.AsSpan(offset)))
            throw new InvalidDataException($"Source bytes cannot round-trip in code page {encoding.CodePage}.");
        return text;
    }

    public static byte[] Encode(string content, Vb6TextEncoding? encoding = null)
    {
        encoding ??= Vb6TextEncoding.Ansi;
        var bytes = encoding.GetEncoding().GetBytes(content);
        return encoding.Utf8Bom ? Utf8Bom.Concat(bytes).ToArray() : bytes;
    }

    public static async Task<string> ReadAllTextAsync(string path) =>
        Decode(await File.ReadAllBytesAsync(path));

    /// <summary>
    /// Reads a document's bytes, deciding their encoding by a chain that cannot fail. A read that throws
    /// drops a project member, and the next Save Project removes its line from the <c>.vbp</c>; so every
    /// read ends somewhere, and the last step gives every byte back.
    /// </summary>
    /// <remarks>
    /// In order, the first step that applies:
    /// <list type="number">
    /// <item>A UTF-8 byte-order mark declares UTF-8, kept on write.</item>
    /// <item><paramref name="lastReadIn"/>, on a reload, without its byte-order mark: an external editor
    /// that removed the mark has not changed what the file is. Skipped when the last read was the last
    /// resort, which would otherwise accept every later version too, however readable.</item>
    /// <item><paramref name="ansi"/>, the host's ANSI code page.</item>
    /// <item><see cref="Vb6TextEncoding.Latin1"/>, which always succeeds; <c>ByteForByte</c> says so, and
    /// the caller warns.</item>
    /// </list>
    /// Each step must decode strictly and encode back to the same bytes (see <see cref="Decode"/>). A
    /// byte-order mark in front of bytes that are not UTF-8 is kept, and the steps read what follows it:
    /// decoded as text, the mark would sit in front of the first line and hide the header from the parser.
    /// </remarks>
    public static (string Text, Vb6TextEncoding Encoding, bool ByteForByte) ReadDocument(
        byte[] bytes, Vb6TextEncoding? lastReadIn = null, Vb6TextEncoding? ansi = null)
    {
        var marked = bytes.AsSpan().StartsWith(Utf8Bom);
        if (marked && TryDecode(bytes, Vb6TextEncoding.Utf8WithBom, out var text))
            return (text, Vb6TextEncoding.Utf8WithBom, false);

        if (lastReadIn is not null && lastReadIn.CodePage != Vb6TextEncoding.Latin1.CodePage)
        {
            var again = lastReadIn with { Utf8Bom = marked };
            if (TryDecode(bytes, again, out text))
                return (text, again, false);
        }

        var host = (ansi ?? Vb6TextEncoding.Ansi) with { Utf8Bom = marked };
        if (TryDecode(bytes, host, out text))
            return (text, host, false);

        var last = Vb6TextEncoding.Latin1 with { Utf8Bom = marked };
        return (Decode(bytes, last), last, true);
    }

    /// <summary>Reads text, original bytes for watcher baselines, and encoding for subsequent saves.</summary>
    /// <inheritdoc cref="ReadDocument"/>
    public static async Task<(string Text, byte[] Bytes, Vb6TextEncoding Encoding, bool ByteForByte)> ReadDocumentAsync(
        string path, Vb6TextEncoding? lastReadIn = null, Vb6TextEncoding? ansi = null)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        var (text, encoding, byteForByte) = ReadDocument(bytes, lastReadIn, ansi);
        return (text, bytes, encoding, byteForByte);
    }

    private static bool TryDecode(byte[] bytes, Vb6TextEncoding encoding, out string text)
    {
        try
        {
            text = Decode(bytes, encoding);
            return true;
        }
        catch (Exception e) when (e is DecoderFallbackException or InvalidDataException)
        {
            text = "";
            return false;
        }
    }

    public static async Task<(string Text, byte[] Bytes)> ReadWithBytesAsync(string path)
    {
        var (text, bytes, _, _) = await ReadDocumentAsync(path);
        return (text, bytes);
    }

    public static string ReadAllText(string path) => Decode(File.ReadAllBytes(path));

    public static void WriteAllText(string path, string content, Vb6TextEncoding? encoding = null) =>
        File.WriteAllBytes(path, Encode(content, encoding));
}
