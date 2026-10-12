using System;
using System.Text;

namespace HexIDE.IDE;

/// <summary>
/// A document holds a character its code page cannot represent, so it was not written. HexIDE keeps a file
/// in the encoding it was read or created in, and refuses an edit that encoding cannot hold rather than
/// convert the file or substitute the character.
/// </summary>
/// <remarks>
/// An <see cref="OperationCanceledException"/> because every caller already treats a refused save as a
/// cancelled one: Make does not report it as a fatal error, a close-time save keeps the project open, and a
/// fire-and-forget command stays silent. <see cref="Exception.Message"/> is the sentence an automation caller
/// receives; a person sees the localized warning instead.
/// </remarks>
public sealed class SourceEncodingRefusal : OperationCanceledException
{
    public SourceEncodingRefusal(string document, Vb6TextEncoding encoding, EncoderFallbackException cause)
        : this(document, encoding.CodePage, CharacterOf(cause), cause)
    {
    }

    private SourceEncodingRefusal(string document, int codePage, (string Text, int Value) character,
        EncoderFallbackException cause)
        : base(Describe(document, codePage, character.Text, character.Value), cause)
    {
        Document = document;
        CodePage = codePage;
        Character = character.Text;
        CodePoint = $"U+{character.Value:X4}";
    }

    /// <summary>The form, module, project or group that was not written, by name.</summary>
    public string Document { get; }

    public int CodePage { get; }

    /// <summary>The first character the code page cannot hold: one UTF-16 unit, or two for a surrogate pair.</summary>
    public string Character { get; }

    /// <summary><see cref="Character"/> as a Unicode code point, such as <c>U+2192</c>.</summary>
    public string CodePoint { get; }

    // A character outside the Basic Multilingual Plane arrives as its two surrogates, with CharUnknown left
    // at U+0000 (measured on .NET 10), so reading CharUnknown alone would name NUL. A lone surrogate arrives
    // in CharUnknown, and ConvertToUtf32 would throw on it.
    private static (string Text, int Value) CharacterOf(EncoderFallbackException cause) =>
        cause.IsUnknownSurrogate()
            ? (new string([cause.CharUnknownHigh, cause.CharUnknownLow]),
               char.ConvertToUtf32(cause.CharUnknownHigh, cause.CharUnknownLow))
            : (cause.CharUnknown.ToString(), cause.CharUnknown);

    private static string Describe(string document, int codePage, string character, int value) =>
        $"{document} is kept in code page {codePage}, which cannot hold '{character}' (U+{value:X4}). Nothing was "
        + "written. HexIDE keeps a file in the code page it was read or created in rather than convert it, so use "
        + "characters that code page holds.";
}
