using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.AvaloniaInterop;

/// <summary>
/// FontName, the font a program sets by name at run time (#1141). Measured on vb6.exe, native and p-code agreeing (the
/// oracle's "The fonts a form names"):
/// <list type="bullet">
/// <item>It reads as a String, the family's own spelling: <c>FontName = "arial"</c> reads back <c>Arial</c>.</item>
/// <item>Setting it sets the family's own charset with it: 2 for Wingdings, Webdings, Symbol and Marlett, 255 for
/// Terminal, Modern, Roman and Script, and otherwise 0. Size, bold and the rest are kept.</item>
/// <item>A family the machine doesn't have is 380, and so are <c>""</c>, <c>"  Arial  "</c> and <c>5</c>. Null is 94. The
/// font is left as it was.</item>
/// <item>On the form, it changes the form's font and none of its controls'.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// <b>What counts as a family the machine has</b> is a decision, since HexIDE runs where Windows' fonts mostly aren't:
/// a family installed here, by its exact name, or one of the names vb6.exe accepted on a stock Windows 10 that are text
/// fonts (<see cref="WindowsTextFonts"/>). Those draw in a substitute here when they aren't installed, as a form that
/// names them at design time does. A symbol font has no substitute, since letters aren't its glyphs, so one that isn't
/// installed is 380: VB6's own error on a machine without the font, which a program can trap and fall back from.
/// </para>
/// <para>
/// Not modelled: vb6.exe also snaps the size to one a bitmap font has, so after <c>FontName = "System"</c> a 12-point
/// font reads 9.75. HexIDE can't read a size yet, so nothing reads wrong.
/// </para>
/// </remarks>
internal static class FontMembers
{
    /// <summary>
    /// The text families vb6.exe accepted by name on a stock Windows 10, with the charset each took, several of which
    /// have no face a modern renderer draws: GDI's bitmap and vector fonts and the names Windows substitutes.
    /// </summary>
    private static readonly Dictionary<string, (string Name, int Charset)> WindowsTextFonts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MS Sans Serif"] = ("MS Sans Serif", 0),
        ["MS Serif"] = ("MS Serif", 0),
        ["System"] = ("System", 0),
        ["Terminal"] = ("Terminal", 255),
        ["Fixedsys"] = ("Fixedsys", 0),
        ["Courier"] = ("Courier", 0),
        ["Small Fonts"] = ("Small Fonts", 0),
        ["Modern"] = ("Modern", 255),
        ["Roman"] = ("Roman", 255),
        ["Script"] = ("Script", 255),
        ["MS Shell Dlg"] = ("MS Shell Dlg", 0),
        ["MS Shell Dlg 2"] = ("MS Shell Dlg 2", 0),
        ["Microsoft Sans Serif"] = ("Microsoft Sans Serif", 0),
        ["Helvetica"] = ("Helvetica", 0),
        ["Times"] = ("Times", 0),
        ["Arial"] = ("Arial", 0),
        ["Segoe UI"] = ("Segoe UI", 0),
        ["Tahoma"] = ("Tahoma", 0),
        ["Courier New"] = ("Courier New", 0),
        ["MS Gothic"] = ("MS Gothic", 0),
    };

    private static bool IsFontName(string member) => string.Equals(member, "FontName", StringComparison.OrdinalIgnoreCase);

    /// <summary>A font member, read: true with its value, false for a member this doesn't answer or a control with no font.</summary>
    public static bool TryGet(Control control, string member, out Vb6Value value)
    {
        value = default;
        if (!IsFontName(member) || AttachedProperties.GetFont(control) is not { } font)
            return false;
        value = new Vb6Value(font.FontFamilyName);
        return true;
    }

    /// <summary>A font member, written: true once set, false for a member this doesn't answer or a control with no font.</summary>
    public static bool TrySet(Control control, string member, Vb6Value value)
    {
        if (!IsFontName(member) || AttachedProperties.GetFont(control) is not { } font)
            return false;
        if (value.IsNull)
            throw new VBRunTimeException(VBStandardError.InvalidUseOfNull);
        var asked = VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.String, null).Value as string ?? "";
        var (name, charset) = Resolve(asked) ?? throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
        AttachedProperties.SetFont(control, new VBFont(name, font.Size, italic: font.Italic, charset: charset,
            underline: font.Underline, strikethrough: font.Strikethrough, weight: font.Weight));
        return true;
    }

    /// <summary>The family a name sets, in its own spelling, with its charset; null for one the machine doesn't have.</summary>
    private static (string Name, int Charset)? Resolve(string asked)
    {
        if (asked.Length == 0)
            return null;
        if (VBFontExtensions.IsInstalled(asked) && FontManager.Current.TryGetGlyphTypeface(new Typeface(asked), out var typeface))
        {
            // A symbol font maps its glyphs at U+F020 to U+F0FF and leaves the letters unmapped.
            var map = typeface.CharacterToGlyphMap;
            var symbol = !map.ContainsGlyph('a') && map.ContainsGlyph(0xF061);
            var charset = symbol ? SymbolFontText.SymbolCharset
                : WindowsTextFonts.TryGetValue(asked, out var known) ? known.Charset : 0;
            return (typeface.FamilyName.Replace("\0", ""), charset);
        }
        return WindowsTextFonts.TryGetValue(asked, out var windows) ? windows : null;
    }
}
