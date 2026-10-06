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

    internal Encoding GetEncoding()
    {
        if (Utf8Bom && CodePage != 65001)
            throw new ArgumentException("A UTF-8 BOM requires the UTF-8 code page.");
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
        // Some legacy code pages have multiple byte spellings for one character. Strict fallbacks alone
        // do not catch that normalization. Refuse the load rather than silently changing such bytes.
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

    /// <summary>Reads text, original bytes for watcher baselines, and encoding for subsequent saves.</summary>
    public static async Task<(string Text, byte[] Bytes, Vb6TextEncoding Encoding)> ReadDocumentAsync(
        string path, Vb6TextEncoding? fallback = null)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        var encoding = DetectEncoding(bytes, fallback);
        return (Decode(bytes, encoding), bytes, encoding);
    }

    public static async Task<(string Text, byte[] Bytes)> ReadWithBytesAsync(string path)
    {
        var (text, bytes, _) = await ReadDocumentAsync(path);
        return (text, bytes);
    }

    public static string ReadAllText(string path) => Decode(File.ReadAllBytes(path));

    public static void WriteAllText(string path, string content, Vb6TextEncoding? encoding = null) =>
        File.WriteAllBytes(path, Encode(content, encoding));
}
