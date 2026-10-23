using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;

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
/// pair becomes U+FFFD, as it did before. U+200C is retained between Arabic-script letters,
/// including intervening combining marks, where it visibly prevents joining (#1316).
/// It is never for JSON that goes into a web page.
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
            if (Rune.DecodeFromUtf16(span[i..], out var rune, out var used) != OperationStatus.Done
                || (WillEncode(rune.Value) && !IsPersianJoiner(span, i)))
                return i;
            i += used;
        }
        return -1;
    }

    // .NET exposes Unicode blocks, not Script / Script_Extensions. Restrict letters by its
    // Arabic block ranges (including Supplement, Extended-A/B and Presentation Forms), then
    // use Unicode categories so punctuation, digits and unassigned slots never qualify.
    private static bool IsArabicLetter(Rune rune) => Rune.IsLetter(rune) &&
        (InRange(rune, UnicodeRanges.Arabic) || InRange(rune, UnicodeRanges.ArabicSupplement)
         || InRange(rune, UnicodeRanges.ArabicExtendedA) || InRange(rune, UnicodeRanges.ArabicExtendedB)
         || InRange(rune, UnicodeRanges.ArabicPresentationFormsA)
         || InRange(rune, UnicodeRanges.ArabicPresentationFormsB));

    private static bool InRange(Rune rune, UnicodeRange range) =>
        rune.Value >= range.FirstCodePoint && rune.Value - range.FirstCodePoint < range.Length;

    private static bool IsMark(Rune rune) => Rune.GetUnicodeCategory(rune) is
        UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    private static bool IsPersianJoiner(ReadOnlySpan<char> text, int index) =>
        text[index] == '\u200C' && HasArabicNeighbour(text[..index], true)
        && HasArabicNeighbour(text[(index + 1)..], false);

    private static bool HasArabicNeighbour(ReadOnlySpan<char> text, bool backwards)
    {
        while (!text.IsEmpty)
        {
            var status = backwards
                ? Rune.DecodeLastFromUtf16(text, out var rune, out var used)
                : Rune.DecodeFromUtf16(text, out rune, out used);
            if (status != OperationStatus.Done)
                return false;
            if (!IsMark(rune))
                return IsArabicLetter(rune);
            text = backwards ? text[..^used] : text[used..];
        }
        return false;
    }

    // TextEncoder's base EncodeCore consults WillEncode alone after the first escape.
    // Keep context over the entire supplied span, even when the destination is shorter.
    // Utf8JsonWriter supplies a whole string suffix and enough space for its worst case;
    // these overrides also cover its UTF-8 overloads. WillEncode stays conservative.
    public override OperationStatus Encode(
        ReadOnlySpan<char> source, Span<char> destination,
        out int charsConsumed, out int charsWritten, bool isFinalBlock = true)
    {
        charsConsumed = charsWritten = 0;
        while (charsConsumed < source.Length)
        {
            var status = Rune.DecodeFromUtf16(source[charsConsumed..], out var rune, out var used);
            if (status == OperationStatus.NeedMoreData && !isFinalBlock)
                return status;
            var escape = WillEncode(rune.Value) && !IsPersianJoiner(source, charsConsumed);
            if (!escape && status == OperationStatus.Done)
            {
                if (used > destination.Length - charsWritten)
                    return OperationStatus.DestinationTooSmall;
                source.Slice(charsConsumed, used).CopyTo(destination[charsWritten..]);
                charsConsumed += used;
                charsWritten += used;
                continue;
            }
            var output = escape ? Escape(rune.Value) : rune.ToString();
            var needed = output.Length;
            if (needed > destination.Length - charsWritten)
                return OperationStatus.DestinationTooSmall;
            output.AsSpan().CopyTo(destination[charsWritten..]);
            charsConsumed += used;
            charsWritten += needed;
        }
        return OperationStatus.Done;
    }

    public override int FindFirstCharacterToEncodeUtf8(ReadOnlySpan<byte> utf8Text)
    {
        for (var i = 0; i < utf8Text.Length;)
        {
            if (Rune.DecodeFromUtf8(utf8Text[i..], out var rune, out var used) != OperationStatus.Done
                || (WillEncode(rune.Value) && !IsPersianJoiner(utf8Text, i)))
                return i;
            i += used;
        }
        return -1;
    }

    private static bool IsPersianJoiner(ReadOnlySpan<byte> text, int index) =>
        Rune.DecodeFromUtf8(text[index..], out var rune, out var used) == OperationStatus.Done
        && rune.Value == 0x200C && HasArabicNeighbour(text[..index], true)
        && HasArabicNeighbour(text[(index + used)..], false);

    private static bool HasArabicNeighbour(ReadOnlySpan<byte> text, bool backwards)
    {
        while (!text.IsEmpty)
        {
            var status = backwards
                ? Rune.DecodeLastFromUtf8(text, out var rune, out var used)
                : Rune.DecodeFromUtf8(text, out rune, out used);
            if (status != OperationStatus.Done)
                return false;
            if (!IsMark(rune))
                return IsArabicLetter(rune);
            text = backwards ? text[..^used] : text[used..];
        }
        return false;
    }

    public override OperationStatus EncodeUtf8(
        ReadOnlySpan<byte> source, Span<byte> destination,
        out int bytesConsumed, out int bytesWritten, bool isFinalBlock = true)
    {
        bytesConsumed = bytesWritten = 0;
        while (bytesConsumed < source.Length)
        {
            var status = Rune.DecodeFromUtf8(source[bytesConsumed..], out var rune, out var used);
            if (status == OperationStatus.NeedMoreData && !isFinalBlock)
                return status;
            var escape = WillEncode(rune.Value) && !IsPersianJoiner(source, bytesConsumed);
            if (!escape && status == OperationStatus.Done)
            {
                if (used > destination.Length - bytesWritten)
                    return OperationStatus.DestinationTooSmall;
                source.Slice(bytesConsumed, used).CopyTo(destination[bytesWritten..]);
                bytesConsumed += used;
                bytesWritten += used;
                continue;
            }
            var output = escape ? Escape(rune.Value) : rune.ToString();
            var needed = Encoding.UTF8.GetByteCount(output);
            if (needed > destination.Length - bytesWritten)
                return OperationStatus.DestinationTooSmall;
            Encoding.UTF8.GetBytes(output, destination[bytesWritten..]);
            bytesConsumed += used;
            bytesWritten += needed;
        }
        return OperationStatus.Done;
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
