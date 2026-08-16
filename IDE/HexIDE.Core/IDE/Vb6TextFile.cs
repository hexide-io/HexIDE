using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HexIDE.IDE;

/// <summary>
/// Reading and writing VB6 source files, which are **ANSI** — not UTF-8.
///
/// VB6 wrote `.vbp`, `.frm`, `.bas`, `.cls` and friends in the authoring machine's ANSI codepage, with no
/// declaration of which one. Reading them as UTF-8 turns every byte ≥ 0x80 into U+FFFD and writes it back
/// as `EF BF BD`, so a copyright header, an accented identifier or a localized string literal is destroyed
/// by the first save.
///
/// **Decoding.** A UTF-8 BOM is honoured. Otherwise the bytes are strict-decoded as UTF-8: success means
/// the file almost certainly *is* UTF-8 (a file HexIDE wrote before this fix), failure means it is not, and
/// we fall back to Latin-1.
///
/// **Latin-1 rather than the system ANSI codepage**, deliberately. Latin-1 maps all 256 byte values to
/// characters and back, so the round-trip is lossless for *any* input regardless of the codepage it was
/// authored in. The system codepage would render a Western file more prettily but is lossy for anything
/// else — an unmappable character becomes '?' and the byte is gone. It is also meaningless on Linux, where
/// CI runs. Bytes first, display second: a Shift-JIS project will look like mojibake in the editor, but
/// saving it returns the file byte-for-byte, which is the promise that matters.
///
/// **Encoding.** Content that fits in Latin-1 is written as Latin-1 — the VB6-correct form, and
/// byte-identical to an ANSI source that was read back. Content that does not fit (genuine CJK typed into
/// HexIDE) is written as UTF-8, because losing it would be worse than writing something VB6 did not expect.
/// </summary>
public static class Vb6TextFile
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false,
                                                          throwOnInvalidBytes: true);

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>Decode VB6 source bytes. Never throws — the fallback always succeeds.</summary>
    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == Utf8Bom[0] && bytes[1] == Utf8Bom[1] && bytes[2] == Utf8Bom[2])
            return StrictUtf8.GetString(bytes, 3, bytes.Length - 3);

        try { return StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }

    /// <summary>Encode for disk. Latin-1 where it fits, which is what VB6 itself wrote.</summary>
    public static byte[] Encode(string content)
    {
        foreach (var c in content)
        {
            if (c > 'ÿ')
                return StrictUtf8.GetBytes(content); // cannot be represented as ANSI; do not drop it
        }
        return Encoding.Latin1.GetBytes(content);
    }

    public static async Task<string> ReadAllTextAsync(string path) =>
        Decode(await File.ReadAllBytesAsync(path));

    /// <summary>
    /// Decoded text plus the exact bytes it came from. Callers that record a file baseline need the bytes:
    /// the baseline is compared against a byte-level re-read by the file watcher, so hashing the decoded
    /// string instead would report every ANSI file as changed the moment it was opened.
    /// </summary>
    public static async Task<(string Text, byte[] Bytes)> ReadWithBytesAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        return (Decode(bytes), bytes);
    }

    public static string ReadAllText(string path) => Decode(File.ReadAllBytes(path));

    public static void WriteAllText(string path, string content) =>
        File.WriteAllBytes(path, Encode(content));
}
