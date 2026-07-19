using Avalonia.Media;

namespace HexIDE.Runtime.BuiltinTypes;

public static class VBFontExtensions
{
    public static FontFamily ToFontFamily(this VBFont font) =>
        // VB6's default UI font (MS Sans Serif) is proprietary and is NOT redistributed — it has been
        // removed from Resources/. Resolve the requested family from the bundled font collection, falling
        // back to the libre substitute Liberation Sans (SIL OFL — drop LiberationSans-*.ttf into
        // Resources/ for consistent form rendering) and finally to the platform's default sans-serif.
        new FontFamily($"fonts:App#{font.FontFamilyName}, Liberation Sans");

    public static FontWeight ToFontWeight(this VBFont font) =>
        font.Bold ? FontWeight.Bold : FontWeight.Normal;

    public static FontStyle ToFontStyle(this VBFont font) =>
        font.Italic ? FontStyle.Italic : FontStyle.Normal;
}