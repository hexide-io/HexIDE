using System;
using System.Collections.Concurrent;
using Avalonia.Media;

namespace HexIDE.Runtime.BuiltinTypes;

public static class VBFontExtensions
{
    public static FontFamily ToFontFamily(this VBFont font) =>
        // The family the form names, installed on this machine, as VB6 draws it: Courier New, Times New Roman,
        // Wingdings. It used to be looked up only in the bundled collection, which holds no fonts, so every form drew in
        // one sans serif (#1132).
        //
        // An installed family is named alone. In the list below, Marlett was passed over for the platform's default
        // face, which has none of its glyphs, though Marlett by itself resolves (#1133).
        //
        // Otherwise the bundled collection, for a substitute shipped there; then the libre Liberation Sans (SIL OFL);
        // then the platform's default sans serif. VB6's default, MS Sans Serif, is proprietary, is not redistributed,
        // and as a bitmap font is not one the renderer draws, so a form on it still falls through.
        IsInstalled(font.FontFamilyName)
            ? new FontFamily(font.FontFamilyName)
            : new FontFamily($"{font.FontFamilyName}, fonts:App#{font.FontFamilyName}, Liberation Sans");

    private static readonly ConcurrentDictionary<string, bool> Installed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether this machine has a font of exactly this family. A platform may answer a family it lacks with a substitute,
    /// so the family found must be the one asked for.
    /// </summary>
    /// <remarks>
    /// The NULs are removed because Avalonia reads a symbol font's name a byte at a time: Wingdings comes back as
    /// "\0W\0i\0n...", its UTF-16 name read as single bytes. A comparison as it stands never matches a symbol font.
    /// </remarks>
    public static bool IsInstalled(string? family) =>
        !string.IsNullOrEmpty(family) && Installed.GetOrAdd(family, name =>
            FontManager.Current.TryGetGlyphTypeface(new Typeface(name), out var typeface)
            && string.Equals(typeface.FamilyName.Replace("\0", ""), name, StringComparison.OrdinalIgnoreCase));

    public static FontWeight ToFontWeight(this VBFont font) =>
        font.Bold ? FontWeight.Bold : FontWeight.Normal;

    public static FontStyle ToFontStyle(this VBFont font) =>
        font.Italic ? FontStyle.Italic : FontStyle.Normal;
}