using System.Collections.Concurrent;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using HexIDE.IDE;

namespace HexIDE.Runtime.BuiltinControls;

/// <summary>
/// Text drawn in a symbol font, as GDI draws it for VB6 (#1133). A font whose Charset is 2, SYMBOL_CHARSET, keeps its
/// glyphs at U+F020 to U+F0FF, and GDI draws each byte of the ANSI string at U+F000 plus that byte. So a Wingdings caption
/// of <c>PMJ</c> is a flag, a bomb and a smiling face, and <c>Chr$(&amp;H81)</c> is the circled one (measured, the oracle's
/// "The fonts a form names").
/// <para>
/// Only the text drawn is mapped. Caption and Text still read back the letters the program wrote.
/// </para>
/// <para>
/// Only where the font is installed. On a machine without it, which is any Linux or macOS machine for Wingdings, the
/// private-use characters would draw as empty boxes, so the letters are drawn as before. What to draw there instead is the
/// other half of #1133.
/// </para>
/// </summary>
public static class SymbolFontText
{
    /// <summary>GDI's SYMBOL_CHARSET, which a .frm records for Wingdings, Webdings, Symbol and Marlett.</summary>
    public const int SymbolCharset = 2;

    /// <summary>
    /// Whether text in this control is drawn as a symbol font's glyphs. Set from the control's font, and inherited, so the
    /// text a button's template makes for its caption follows the button's font.
    /// </summary>
    public static readonly AttachedProperty<bool> SymbolGlyphsProperty =
        AvaloniaProperty.RegisterAttached<VBProps, Control, bool>("SymbolGlyphs", false, inherits: true);

    public static bool GetSymbolGlyphs(AvaloniaObject element) => element.GetValue(SymbolGlyphsProperty);
    public static void SetSymbolGlyphs(AvaloniaObject element, bool value) => element.SetValue(SymbolGlyphsProperty, value);

    private static readonly ConcurrentDictionary<string, bool> Installed = new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="font"/> draws as a symbol font here: its Charset is 2 and the family is installed.</summary>
    public static bool UsesSymbolGlyphs(VBFont font) => font.Charset == SymbolCharset && IsInstalled(font.FontFamilyName);

    /// <summary>
    /// Whether this machine has a font of exactly this family. A platform may answer a family it lacks with a substitute,
    /// so the family found must be the one asked for.
    /// </summary>
    public static bool IsInstalled(string? family) =>
        !string.IsNullOrEmpty(family) && Installed.GetOrAdd(family, name =>
            FontManager.Current.TryGetGlyphTypeface(new Typeface(name), out var typeface)
            && string.Equals(typeface.FamilyName, name, System.StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// <paramref name="text"/> as GDI draws it in a symbol font: each character's byte in the ANSI code page, at U+F000
    /// plus that byte. An ampersand is left alone for the access key that follows it, and so are line breaks and tabs,
    /// which a caption lays out rather than draws. A character the code page has no single byte for is left as it is.
    /// </summary>
    public static string ToSymbolGlyphs(string text)
    {
        var ansi = AnsiEncoding();
        var result = new StringBuilder(text.Length);
        var one = new char[1];
        var bytes = new byte[4];
        foreach (var c in text)
        {
            if (c is '&' or '\r' or '\n' or '\t' || c is >= (char)0xF000 and <= (char)0xF0FF)
            {
                result.Append(c);
                continue;
            }
            if (c < 0x80)
            {
                result.Append((char)(0xF000 + c));
                continue;
            }
            one[0] = c;
            var count = ansi.GetBytes(one, 0, 1, bytes, 0);
            result.Append(count == 1 ? (char)(0xF000 + bytes[0]) : c);
        }
        return result.ToString();
    }

    private static Encoding? ansiEncoding;
    private static int ansiCodePage;

    private static Encoding AnsiEncoding()
    {
        var codePage = Vb6TextEncoding.Ansi.CodePage;
        if (ansiEncoding is null || ansiCodePage != codePage)
        {
            ansiEncoding = CodePagesEncodingProvider.Instance.GetEncoding(codePage, new EncoderReplacementFallback(""),
                               DecoderFallback.ReplacementFallback)
                           ?? Encoding.GetEncoding(codePage, new EncoderReplacementFallback(""), DecoderFallback.ReplacementFallback);
            ansiCodePage = codePage;
        }
        return ansiEncoding;
    }
}

/// <summary>
/// A caption's text with its access key, drawn as a symbol font's glyphs when the control's font is one (#1133). The
/// caption is <see cref="Source"/>; <see cref="TextBlock.Text"/> is what is drawn.
/// </summary>
public class VBAccessText : AccessText
{
    public static readonly StyledProperty<string?> SourceProperty =
        AvaloniaProperty.Register<VBAccessText, string?>(nameof(Source));

    public string? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    protected override System.Type StyleKeyOverride => typeof(AccessText);

    static VBAccessText()
    {
        SourceProperty.Changed.AddClassHandler<VBAccessText>((text, _) => text.Update());
        SymbolFontText.SymbolGlyphsProperty.Changed.AddClassHandler<VBAccessText>((text, _) => text.Update());
    }

    private void Update()
    {
        var source = Source ?? "";
        if (SymbolFontText.GetSymbolGlyphs(this))
            source = SymbolFontText.ToSymbolGlyphs(source);
        Text = source.Replace("&", "_");
    }
}
