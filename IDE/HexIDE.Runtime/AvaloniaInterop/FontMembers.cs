using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.AvaloniaInterop;

/// <summary>
/// A control's or a form's font members at run time: FontName (#1141), and FontSize, FontBold, FontItalic, FontUnderline
/// and FontStrikethru (#1343). FontName, measured on vb6.exe, native and p-code agreeing (the oracle's "The fonts a form
/// names"):
/// <list type="bullet">
/// <item>It reads as a String, the family's own spelling: <c>FontName = "arial"</c> reads back <c>Arial</c>.</item>
/// <item>Setting it sets the family's own charset with it: 2 for Wingdings, Webdings, Symbol and Marlett, 255 for
/// Terminal, Modern, Roman and Script, and otherwise 0. Size, bold and the rest are kept.</item>
/// <item>A family the machine doesn't have is 380, and so are <c>""</c>, <c>"  Arial  "</c> and <c>5</c>. Null is 94. The
/// font is left as it was.</item>
/// <item>On the form, it changes the form's font and none of its controls'.</item>
/// </list>
/// The others, measured the same way (the oracle's "A font's FontSize, FontBold and the rest"):
/// <list type="bullet">
/// <item>FontSize is a Single, in points, and reads back as the size the font was given (<see cref="VbFontSizes"/>), a
/// .frm's included: its <c>Size = 10</c> reads 9.75. A change of name gives the new family the size as it reads, so Arial
/// 9 made MS Sans Serif reads 8.25, and made Arial again still 8.25.</item>
/// <item>FontSize 0 or below is 380, True included, and above 2160 is 6. A String that is no number is 13, and so is
/// Null, where every other font member's Null is 94.</item>
/// <item>The four others are Booleans: any number but 0 is True, "x" is 13 and Null is 94. FontBold sets the weight to 700
/// or 400, and reads True for any weight above 550.</item>
/// <item>Each leaves the rest of the font as it was.</item>
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

    private enum Member { Name, Size, Bold, Italic, Underline, Strikethru }

    private static Member? Of(string member) => member.ToLowerInvariant() switch
    {
        "fontname" => Member.Name,
        "fontsize" => Member.Size,
        "fontbold" => Member.Bold,
        "fontitalic" => Member.Italic,
        "fontunderline" => Member.Underline,
        "fontstrikethru" => Member.Strikethru,
        _ => null,
    };

    /// <summary>A font member, read: true with its value, false for a member this doesn't answer or a control with no font.</summary>
    public static bool TryGet(Control control, string member, out Vb6Value value)
    {
        value = default;
        if (Of(member) is not { } which || AttachedProperties.GetFont(control) is not { } font)
            return false;
        var (size, family) = VbFontSizes.Snap(font.FontFamilyName, font.Size);
        value = which switch
        {
            Member.Name => new Vb6Value(family),
            Member.Size => new Vb6Value((float)size),
            // VB6 reads a weight above 550 as bold, as a .frm's 600 is (measured); VBFont.Bold's line is 700.
            Member.Bold => new Vb6Value(font.Weight > 550),
            Member.Italic => new Vb6Value(font.Italic),
            Member.Underline => new Vb6Value(font.Underline),
            _ => new Vb6Value(font.Strikethrough),
        };
        return true;
    }

    /// <summary>A font member, written: true once set, false for a member this doesn't answer or a control with no font.</summary>
    public static bool TrySet(Control control, string member, Vb6Value value)
    {
        if (Of(member) is not { } which || AttachedProperties.GetFont(control) is not { } font)
            return false;
        var set = which switch
        {
            Member.Name => Named(font, value),
            Member.Size => Sized(font, value),
            Member.Bold => With(font, weight: Flag(value) ? VBFont.BoldWeight : VBFont.NormalWeight),
            Member.Italic => With(font, italic: Flag(value)),
            Member.Underline => With(font, underline: Flag(value)),
            _ => With(font, strikethrough: Flag(value)),
        };
        AttachedProperties.SetFont(control, set);
        return true;
    }

    private static VBFont Named(VBFont font, Vb6Value value)
    {
        if (value.IsNull)
            throw new VBRunTimeException(VBStandardError.InvalidUseOfNull);
        var asked = VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.String, null).Value as string ?? "";
        var (name, charset) = Resolve(asked) ?? throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
        // The new family takes the size as it reads now, snapped to what that family has (measured).
        var readSize = VbFontSizes.Snap(font.FontFamilyName, font.Size).Size;
        return Snapped(new VBFont(name, readSize, italic: font.Italic, charset: charset,
            underline: font.Underline, strikethrough: font.Strikethrough, weight: font.Weight));
    }

    private static VBFont Sized(VBFont font, Vb6Value value)
    {
        // Null is 13 here, a Type mismatch, where the other font members' is 94 (measured).
        if (value.IsNull)
            throw new VBRunTimeException(VBStandardError.TypeMismatch);
        var size = Convert.ToDouble(VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Single, null).Value,
            System.Globalization.CultureInfo.InvariantCulture);
        if (size <= 0)
            throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
        if (size > VbFontSizes.Largest)
            throw new VBRunTimeException(VBStandardError.Overflow);
        return Snapped(With(font, size: size));
    }

    // The font with its size as VB6 gives it, which for a bitmap font may be Arial's, with Arial's charset.
    private static VBFont Snapped(VBFont font)
    {
        var (size, family) = VbFontSizes.Snap(font.FontFamilyName, font.Size);
        return family == font.FontFamilyName
            ? With(font, size: size)
            : new VBFont(family, size, italic: font.Italic, charset: 0, underline: font.Underline,
                strikethrough: font.Strikethrough, weight: font.Weight);
    }

    // A FontBold, FontItalic, FontUnderline or FontStrikethru: a Boolean as an assignment makes one, 94 for Null.
    private static bool Flag(Vb6Value value)
    {
        if (value.IsNull)
            throw new VBRunTimeException(VBStandardError.InvalidUseOfNull);
        return VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Boolean, null).Value is true;
    }

    private static VBFont With(VBFont font, double? size = null, int? weight = null, bool? italic = null,
        bool? underline = null, bool? strikethrough = null) =>
        new(font.FontFamilyName, size ?? font.Size, italic: italic ?? font.Italic, charset: font.Charset,
            underline: underline ?? font.Underline, strikethrough: strikethrough ?? font.Strikethrough,
            weight: weight ?? font.Weight);

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
