using Avalonia.Media;

namespace HexIDE.Runtime.BuiltinTypes;

public static class VBFontExtensions
{
    public static FontFamily ToFontFamily(this VBFont font) =>
        // The family the form names, installed on this machine, as VB6 draws it: Courier New, Times New Roman,
        // Wingdings. It used to be looked up only in the bundled collection, which holds no fonts, so every form drew in
        // one sans serif (#1132). Then the bundled collection, for a substitute shipped there; then the libre Liberation
        // Sans (SIL OFL); then the platform's default sans serif. VB6's default, MS Sans Serif, is proprietary, is not
        // redistributed, and as a bitmap font is not one the renderer draws, so a form on it still falls through.
        new FontFamily($"{font.FontFamilyName}, fonts:App#{font.FontFamilyName}, Liberation Sans");

    public static FontWeight ToFontWeight(this VBFont font) =>
        font.Bold ? FontWeight.Bold : FontWeight.Normal;

    public static FontStyle ToFontStyle(this VBFont font) =>
        font.Italic ? FontStyle.Italic : FontStyle.Normal;
}