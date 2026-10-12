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
    /// <param name="content">The text that failed to encode, which <paramref name="cause"/> indexes into.</param>
    public SourceEncodingRefusal(string document, Vb6TextEncoding encoding, EncoderFallbackException cause,
        string content)
        : this(document, encoding.CodePage, CharacterOf(cause), LineOf(content, cause.Index), cause)
    {
    }

    private SourceEncodingRefusal(string document, int codePage, (string Text, int Value) character, string line,
        EncoderFallbackException cause)
        : base(Describe(document, codePage, character.Text, character.Value, line), cause)
    {
        Document = document;
        CodePage = codePage;
        Character = character.Text;
        CodePoint = $"U+{character.Value:X4}";
        Line = line;
    }

    /// <summary>The form, module, project or group that was not written, by name.</summary>
    public string Document { get; }

    public int CodePage { get; }

    /// <summary>The first character the code page cannot hold: one UTF-16 unit, or two for a surrogate pair.</summary>
    public string Character { get; }

    /// <summary><see cref="Character"/> as a Unicode code point, such as <c>U+2192</c>.</summary>
    public string CodePoint { get; }

    /// <summary>
    /// The line of the file, as it would have been written, that holds <see cref="Character"/>: trimmed, and
    /// shortened around the character when it is long.
    /// </summary>
    /// <remarks>
    /// The character is not always in what the caller just changed. A form is written whole, so it can be in
    /// a property set earlier, or in a file name the project file gives a member. The line says which.
    /// </remarks>
    public string Line { get; }

    /// <summary>The longest <see cref="Line"/> quoted whole.</summary>
    private const int LongestLine = 100;

    // A character outside the Basic Multilingual Plane arrives as its two surrogates, with CharUnknown left
    // at U+0000 (measured on .NET 10), so reading CharUnknown alone would name NUL. A lone surrogate arrives
    // in CharUnknown, and ConvertToUtf32 would throw on it.
    private static (string Text, int Value) CharacterOf(EncoderFallbackException cause) =>
        cause.IsUnknownSurrogate()
            ? (new string([cause.CharUnknownHigh, cause.CharUnknownLow]),
               char.ConvertToUtf32(cause.CharUnknownHigh, cause.CharUnknownLow))
            : (cause.CharUnknown.ToString(), cause.CharUnknown);

    // Index is the character's position in the whole string, wherever the encoder works in chunks (measured
    // on .NET 10 at 0, 1023, 1024, 4097 and 70000, in code pages 1252, 1251 and 932 and in Latin-1).
    private static string LineOf(string content, int index)
    {
        if (index < 0 || index >= content.Length)
            return "";

        var start = index == 0 ? 0 : content.LastIndexOfAny(['\r', '\n'], index - 1) + 1;
        var end = content.IndexOfAny(['\r', '\n'], index);
        if (end < 0)
            end = content.Length;
        var line = content[start..end];
        var at = index - start;

        var trimmed = line.TrimStart();
        at -= line.Length - trimmed.Length;
        line = trimmed.TrimEnd();
        if (line.Length <= LongestLine)
            return line;

        var from = Math.Clamp(at - LongestLine / 2, 0, line.Length - LongestLine);
        var to = from + LongestLine;
        if (from > 0 && char.IsLowSurrogate(line[from]))
            from--;
        if (to < line.Length && char.IsLowSurrogate(line[to]))
            to++;
        return (from > 0 ? "…" : "") + line[from..to] + (to < line.Length ? "…" : "");
    }

    private static string Describe(string document, int codePage, string character, int value, string line) =>
        $"{document} is kept in code page {codePage}, which cannot hold '{character}' (U+{value:X4})"
        + (line.Length > 0 ? $", found in the line `{line}`" : "")
        + ". Nothing was written. HexIDE keeps a file in the code page it was read or created in rather than "
        + "convert it, so use characters that code page holds"
        + (line.Length > 0 ? ", and where that line names a file or folder, a name it can hold." : ".");
}
