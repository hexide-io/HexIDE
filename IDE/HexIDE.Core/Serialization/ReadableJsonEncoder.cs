using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;

namespace HexIDE.Serialization;

/// <summary>
/// Writes JSON that a model reads as text: every character as itself, except those JSON requires escaped and those
/// that would not be seen (#753).
/// </summary>
/// <remarks>
/// <para>
/// The default encoder escapes anything a web page could misread, so a reply quoting a name with an apostrophe read
/// <c>\u0027Form1\u0027</c>, and a module named in Cyrillic could not be read at all. The relaxed encoder still
/// escapes every character outside the Basic Multilingual Plane, an emoji or a CJK Extension B character, which is the
/// one character a code-page refusal is about. It also writes bidirectional and other format characters raw, where a
/// reader cannot see them: a right-to-left override in a module would reorder the reply around it.
/// </para>
/// <para>
/// So this escapes by category: a quote and a backslash, as JSON requires, and control, format, line and paragraph
/// separator characters, with the short forms for <c>\n</c>, <c>\t</c> and the like. A surrogate that is not half of a
/// pair becomes U+FFFD, as it did before. It is never for JSON that goes into a web page.
/// </para>
/// </remarks>
public sealed class ReadableJsonEncoder : JavaScriptEncoder
{
    public static readonly ReadableJsonEncoder Instance = new();

    private ReadableJsonEncoder() { }

    public override int MaxOutputCharactersPerInputCharacter => 6;

    public override bool WillEncode(int unicodeScalar) =>
        unicodeScalar is '"' or '\\'
        || !Rune.IsValid(unicodeScalar)
        || Rune.GetUnicodeCategory(new Rune(unicodeScalar)) is UnicodeCategory.Control or UnicodeCategory.Format
            or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;

    public override unsafe int FindFirstCharacterToEncode(char* text, int textLength)
    {
        var span = new ReadOnlySpan<char>(text, textLength);
        for (var i = 0; i < span.Length;)
        {
            if (Rune.DecodeFromUtf16(span[i..], out var rune, out var used) != OperationStatus.Done || WillEncode(rune.Value))
                return i;
            i += used;
        }
        return -1;
    }

    public override unsafe bool TryEncodeUnicodeScalar(
        int unicodeScalar, char* buffer, int bufferLength, out int numberOfCharactersWritten)
    {
        var escaped = Escape(unicodeScalar);
        numberOfCharactersWritten = 0;
        if (escaped.Length > bufferLength)
            return false;
        escaped.AsSpan().CopyTo(new Span<char>(buffer, bufferLength));
        numberOfCharactersWritten = escaped.Length;
        return true;
    }

    private static string Escape(int scalar) => scalar switch
    {
        '"' => "\\\"",
        '\\' => "\\\\",
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        '\b' => "\\b",
        '\f' => "\\f",
        // An invisible tag character, U+E0001 and on, as its surrogate pair.
        > 0xFFFF => $"\\u{0xD800 + ((scalar - 0x10000) >> 10):X4}\\u{0xDC00 + ((scalar - 0x10000) & 0x3FF):X4}",
        _ => $"\\u{scalar:X4}",
    };
}
