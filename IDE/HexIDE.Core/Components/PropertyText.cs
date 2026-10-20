using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Runtime.Components;

/// <summary>
/// A property value written as text by a caller who is not typing into the Properties window: an automation
/// client passing a string. Accepts every spelling the Properties window shows for a value.
/// </summary>
/// <remarks>
/// <c>set_control_property</c> parsed string, number and bool itself and refused everything else, so a colour or
/// an enum -- a label's BackStyle, a form's BackColor -- could not be set at all (#641). This defers to
/// <see cref="PropertyClass.TryParseString"/>, the Properties window's own parser, for every type it knows, and
/// adds what that parser leaves to a dropdown: an enum by the name the dropdown shows. Numbers are read with the
/// invariant culture, because a caller's string is not typed in the machine's locale.
/// </remarks>
public static partial class PropertyText
{
    [GeneratedRegex(@"^\s*(?<number>-?\d+)\s*(?:-.*)?$")]
    private static partial Regex LeadingNumber();

    /// <summary>Parses <paramref name="text"/> as a value of <paramref name="property"/>'s type.</summary>
    /// <param name="current">
    /// The value the property holds now, for a text that changes part of one: a font's size alone keeps its face.
    /// </param>
    public static bool TryParse(PropertyClass property, string text, out object? value, object? current = null)
    {
        var type = property.PropertyType;
        var invariant = CultureInfo.InvariantCulture;
        value = null;

        if (type == typeof(double))
            return Parsed(double.TryParse(text, NumberStyles.Float, invariant, out var d), d, out value);
        if (type == typeof(float))
            return Parsed(float.TryParse(text, NumberStyles.Float, invariant, out var f), f, out value);
        if (type == typeof(int))
            return Parsed(int.TryParse(text, NumberStyles.Integer, invariant, out var i), i, out value);
        if (type == typeof(bool))
            return Parsed(bool.TryParse(text.Trim(), out var b), b, out value);
        if (type.IsEnum)
            return TryParseEnum(type, text, out value);
        if (type == typeof(VBFont))
            return TryParseFont(text, current is VBFont font ? font : VBFont.Default, out value);

        try
        {
            return property.TryParseString(text, out value);
        }
        catch (NotImplementedException)
        {
            // A type the Properties window has no text parser for either: it is edited through a dialog.
            return false;
        }
    }

    /// <summary>
    /// The spellings <see cref="TryParse"/> accepts for <paramref name="property"/>, for a refusal to name.
    /// </summary>
    public static string Accepted(PropertyClass property)
    {
        var type = property.PropertyType;
        if (type.IsEnum)
            return "one of " + string.Join(", ", Enum.GetValues(type).Cast<object>()
                       .Select(v => $"{Convert.ToInt64(v, CultureInfo.InvariantCulture)} ({DisplayName(v)})"))
                   + ", by number, by name, or as the Properties window shows it, e.g. \"1 - "
                   + DisplayName(Enum.GetValues(type).Cast<object>().Last()) + "\"";
        if (type == typeof(VBColor))
            return "a VB6 colour literal as the Properties window shows it, such as &H00C0FFC0& for a colour or "
                   + "&H8000000F& for a system colour";
        if (type == typeof(VBFont))
            return "a font as get_form_controls shows it, parts separated by commas, the face first, such as \"Tahoma, "
                   + "9.75 pt, bold, italic, charset 0\": a face, a size in points up to 2160, and style words (regular, "
                   + "bold, italic, underline, strikethrough, weight N, charset N), each once. A part not given keeps the "
                   + "current font's, but a new face without a charset takes charset 0, and when any style word is "
                   + "given the words given are the whole style, so \"regular\" clears it";
        if (type == typeof(byte[]))
            return "no text: it is loaded through the Properties window's dialog, and no automation route reaches it yet";
        if (type == typeof(bool))
            return "True or False";
        if (type == typeof(int))
            return "a whole number";
        if (type == typeof(double) || type == typeof(float))
            return "a number, with '.' for decimals";
        return $"a value the Properties window accepts for a {type.Name}";
    }

    /// <summary>A stored value as a reply shows it: an enum as its number and name, anything else as written.</summary>
    public static string Display(object? value) => value switch
    {
        null => "(none)",
        Enum member => $"{Convert.ToInt64(member, CultureInfo.InvariantCulture)} ({DisplayName(member)})",
        string text => $"'{text}'",
        // The whole font, not the face VBFont.ToString gives: a size or style sent alone is what changed (#952 review).
        VBFont font => Shown(VBProperties.FontProperty, font),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    /// <summary>
    /// A stored value as text <see cref="TryParse"/> takes back, where that property can be set from text (#923): an
    /// enum as the Properties window's dropdown shows it, "1 - Opaque", a colour as its literal, a number with '.' in
    /// any culture, a font as its face, size and style (#952). A picture or other binary value, which is loaded through
    /// a dialog, reads as a summary instead: its size, or "(None)".
    /// </summary>
    public static string Shown(PropertyClass property, object? value) => value switch
    {
        _ when property.PropertyType == typeof(byte[]) =>
            value is byte[] { Length: > 0 } bytes ? $"({bytes.Length} bytes)" : "(None)",
        null => "",
        Enum member => $"{Convert.ToInt64(member, CultureInfo.InvariantCulture)} - {DisplayName(member)}",
        bool flag => flag ? "True" : "False",
        VBFont font => string.Join(", ", new[]
            {
                font.FontFamilyName, $"{font.Size.ToString(CultureInfo.InvariantCulture)} pt",
                font.Weight switch { VBFont.NormalWeight => null, VBFont.BoldWeight => "bold", var w => $"weight {w}" },
                font.Italic ? "italic" : null,
                font.Underline ? "underline" : null, font.Strikethrough ? "strikethrough" : null,
                // So the text names the whole style, and sent back it clears one another font had (#952).
                font is { Weight: VBFont.NormalWeight, Italic: false, Underline: false, Strikethrough: false } ? "regular" : null,
                // Always, 0 too, so the text is the whole font and sets another control to it exactly (#952 review).
                $"charset {font.Charset}",
            }.Where(part => part is not null)),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    /// <summary>The order the Properties window lists a component's properties in, by name.</summary>
    public static IEnumerable<PropertyClass> InWindowOrder(IComponentClass type) => type.Properties.OrderBy(p => p.Name);

    /// <summary>
    /// Every property of <paramref name="component"/>: each the Properties window shows, in its order, with its value
    /// as <see cref="Shown"/> writes it and whether that text can be sent back to set it, and each its file holds
    /// that HexIDE does not model (#923).
    /// </summary>
    /// <remarks>
    /// <para>The review of #923 found three ways the window's rows alone would mislead a caller:</para>
    /// <list type="bullet">
    /// <item>A value the file holds and HexIDE could not read, such as a palette colour (#786), is kept as the file's
    /// own line and the window shows the default. The row gives the file's text, not settable, since setting it would
    /// replace the line the save otherwise keeps.</item>
    /// <item>A property HexIDE does not model, such as a TextBox's MultiLine or a control array's Index, is in no row
    /// at all, so it read as absent. It gets a row of its own with the file's text, not settable.</item>
    /// <item>Width, Height and Visible on a control VB6 draws nothing for, the Timer, are rows the save leaves out, so
    /// a value set there was gone on reload. They are marked not settable.</item>
    /// </list>
    /// </remarks>
    public static PropertyRow[] Rows(ComponentInstance component)
    {
        var type = component.BaseClass;
        var modelled = InWindowOrder(type).Select(p =>
        {
            if (!component.TryGetBoxedProperty(p, out _) && RawOf(component, p.Name) is { } unread)
                return new PropertyRow(p.Name, unread, false,
                    "the file holds this, which HexIDE could not read, so the Properties window shows the default; the "
                    + "save keeps the file's text unless the property is set");
            var held = component.GetBoxedPropertyOrDefault(p);
            var shown = Shown(p, held);
            if (!type.IsVisual && (p == VBProperties.WidthProperty || p == VBProperties.HeightProperty || p == VBProperties.VisibleProperty))
                return new PropertyRow(p.Name, shown, false, $"a {type.VBTypeName} has no {p.Name} in VB6, so the file does not keep it");
            // Settable when the text sets back the value it shows, not merely parses: a font whose face reads as a size or a
            // style word parsed to another font (#952 review). A string or Tag never set reads as "" and sets "".
            return new PropertyRow(p.Name, shown, TryParse(p, shown, out var back, held)
                && Equals(back, held ?? (p.PropertyType == typeof(string) || p.PropertyType == typeof(object) ? "" : null)));
        });
        var unmodelled = component.UnknownRawProperties
            .Where(raw => !type.PropertiesByName.ContainsKey(raw.Name))
            .Select(raw => new PropertyRow(raw.Name, RawText(raw), false,
                "not modelled by HexIDE, so not in the Properties window: the file's own text, which the save keeps"));
        return [.. modelled.Concat(unmodelled).OrderBy(r => r.Name)];
    }

    /// <summary>
    /// The components <paramref name="name"/> names, without regard to case: one, or every element of a control
    /// array, or with an index, <c>lblTitle(1)</c>, the element whose Index is that (#923 review).
    /// </summary>
    public static IReadOnlyList<ComponentInstance> Named(IEnumerable<ComponentInstance> components, string name)
    {
        var element = ArrayElement().Match(name);
        var wanted = element.Success ? element.Groups["name"].Value.Trim() : name;
        var named = components.Where(c => string.Equals(c.GetPropertyOrDefault(VBProperties.NameProperty), wanted,
            StringComparison.OrdinalIgnoreCase));
        if (element.Success)
        {
            // An index too big for an int names no element, rather than throwing.
            int? index = int.TryParse(element.Groups["index"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var i)
                ? i : null;
            named = named.Where(c => index is not null && ArrayIndexOf(c) == index);
        }
        return named.ToList();
    }

    /// <summary>
    /// The one component <paramref name="name"/> names, for a tool that changes it and saves: a control by its name, or
    /// an element by <c>name(index)</c>. A name that matches more than one is refused rather than taken as its first,
    /// since the guess would be written to the file (#1252). Null with no refusal when nothing matches.
    /// </summary>
    public static ComponentInstance? NamedOne(IEnumerable<ComponentInstance> components, string name, out string? refusal)
    {
        refusal = null;
        var all = components.ToList();
        var matches = Named(all, name);
        if (matches.Count == 0 && ArrayElement().Match(name) is { Success: true } element
            && Named(all, element.Groups["name"].Value.Trim()) is { Count: > 0 } array)
        {
            // The array exists and the index does not: say which do, rather than that no such control exists.
            refusal = $"'{name}' names no element: {array[0].GetPropertyOrDefault(VBProperties.NameProperty)} has Index "
                      + string.Join(", ", array.Select(ArrayIndexOf).OfType<int>().Order()
                          .Select(i => i.ToString(CultureInfo.InvariantCulture)));
            return null;
        }
        if (matches.Count <= 1)
            return matches.FirstOrDefault();

        var indexes = matches.Select(ArrayIndexOf).OfType<int>().Order().ToList();
        refusal = indexes.Count == matches.Count && indexes.Distinct().Count() == indexes.Count
            ? $"'{name}' names a control array of {matches.Count}, Index "
              + $"{string.Join(", ", indexes.Select(i => i.ToString(CultureInfo.InvariantCulture)))}; name one element, "
              + $"such as {matches[0].GetPropertyOrDefault(VBProperties.NameProperty)}"
              + $"({indexes[0].ToString(CultureInfo.InvariantCulture)})"
            : $"'{name}' names {matches.Count} controls, which do not each have an Index of their own to tell them apart";
        return null;
    }

    /// <summary>A control's name as a caller addresses it: <c>lblTitle(1)</c> for a control array's element.</summary>
    public static string DisplayName(ComponentInstance component) =>
        component.GetPropertyOrDefault(VBProperties.NameProperty)
        + (ArrayIndexOf(component) is { } index ? $"({index.ToString(CultureInfo.InvariantCulture)})" : "");

    /// <summary>A control array element's Index, which HexIDE keeps as the file's own line, or null.</summary>
    public static int? ArrayIndexOf(ComponentInstance component) =>
        int.TryParse(RawOf(component, "Index"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ? index : null;

    // ASCII digits only, as FontSize below: \d also matches an Arabic-Indic digit, which the invariant parse threw on.
    [GeneratedRegex(@"^(?<name>[^()]+)\(\s*(?<index>[0-9]+)\s*\)$")]
    private static partial Regex ArrayElement();

    private static string? RawOf(ComponentInstance component, string name) =>
        component.UnknownRawProperties.Where(raw => string.Equals(raw.Name, name, StringComparison.OrdinalIgnoreCase))
            .Select(RawText).FirstOrDefault();

    // A one-line property as the text after its '=', a BeginProperty block as its lines.
    private static string RawText(ComponentInstance.UnknownRawProperty raw) =>
        raw.Lines is [var line] && line.IndexOf('=') is var eq and >= 0
            ? line[(eq + 1)..].Trim()
            : string.Join("\n", raw.Lines.Select(l => l.Trim()));

    // ASCII digits only: \d also matches a fullwidth or Arabic-Indic digit, which the invariant parse then threw on.
    [GeneratedRegex(@"^(?<size>[0-9]+(?:\.[0-9]+)?)\s*(?:pt)?$", RegexOptions.IgnoreCase)]
    private static partial Regex FontSize();

    [GeneratedRegex(@"^(?<what>weight|charset)\s+(?<n>[0-9]+)$", RegexOptions.IgnoreCase)]
    private static partial Regex FontNumber();

    // What a size or a style meant as one part looks like when it is the first, so it is not taken for a face: "-3 pt",
    // "12px", ".5", "1e2 pt", "Arial 12", "Tahoma bold". A face can start with a digit, as 8514oem does, but not be only
    // a number, or end in a size or a unit.
    [GeneratedRegex(@"^[-+.]|^[0-9.]+\s*(?:pt|px|points?)?$|\s(?:[0-9.]+\s*(?:pt|px|points?)?|pt|px|points?|regular|bold|italic|underline|strikethrough)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex NotAFace();

    /// <summary>The largest font size VB6 takes, in points.</summary>
    private const double LargestFontSize = 2160;

    /// <summary>
    /// A font as <see cref="Shown"/> writes it, "Tahoma, 9.75 pt, bold, italic, charset 0", merged with <paramref
    /// name="current"/> (#952): set_control_property refused every Font, and its refusal named a text form that did not
    /// exist.
    /// </summary>
    /// <remarks>
    /// <para>Parts are separated by commas, the face first. A part not given keeps <paramref name="current"/>'s, so
    /// "12 pt" changes the size alone. A new face without a charset takes charset 0, as the Properties window's Font
    /// dialog sets it: keeping a symbol font's 2 drew a Tahoma caption as boxes. Style words are taken as a set: when
    /// any is given, the words given are the whole style, so a font read off one control and sent to another is that
    /// font, and "regular" clears every style.</para>
    /// <para>Anything that does not say one font is refused rather than read as some font (#952 review): a part given
    /// twice, as "9,75 pt" splits into two sizes; a first part that is a size or style mistyped, which was taken for a
    /// face; a face VB6 cannot hold or the .frm writer cannot quote; a size beyond VB6's 2160 points or finer than its
    /// four decimal places.</para>
    /// </remarks>
    private static bool TryParseFont(string text, VBFont current, out object? value)
    {
        value = null;
        var parts = text.Split(',').Select(p => p.Trim()).ToList();
        if (parts.Any(p => p.Length == 0))
            return false;

        string? face = null;
        double? size = null;
        int? charset = null, weight = null;
        bool italic = false, underline = false, strikethrough = false, styled = false;
        var said = new HashSet<string>();
        bool Once(string what) => said.Add(what);
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            if (FontSize().Match(part) is { Success: true } sized)
            {
                if (!Once("size") || !double.TryParse(sized.Groups["size"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var points)
                    || points <= 0 || points > LargestFontSize || Math.Round(points, 4) != points)
                    return false;
                size = points;
            }
            else if (FontNumber().Match(part) is { Success: true } numbered)
            {
                if (!int.TryParse(numbered.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                    return false;
                if (numbered.Groups["what"].Value.Equals("charset", StringComparison.OrdinalIgnoreCase))
                {
                    if (!Once("charset") || n > 255) return false;
                    charset = n;
                }
                else
                {
                    if (!Once("weight") || n is < 1 or > 1000) return false;
                    (weight, styled) = (n, true);
                }
            }
            else
            {
                switch (part.ToLowerInvariant())
                {
                    // Regular, bold and weight N all say the weight, so one of them at most.
                    case "regular": if (!Once("weight")) return false; styled = true; break;
                    case "bold": if (!Once("weight")) return false; (weight, styled) = (VBFont.BoldWeight, true); break;
                    case "italic": if (!Once("italic")) return false; (italic, styled) = (true, true); break;
                    case "underline": if (!Once("underline")) return false; (underline, styled) = (true, true); break;
                    case "strikethrough": if (!Once("strikethrough")) return false; (strikethrough, styled) = (true, true); break;
                    // Anything else is the face, which comes first; a word later is a mistake, not a face.
                    default:
                        if (i != 0 || !IsFace(part)) return false;
                        face = part;
                        break;
                }
            }
        }

        var font = new VBFont(
            face ?? current.FontFamilyName,
            size ?? current.Size,
            charset: charset ?? (face is null || face == current.FontFamilyName ? current.Charset : 0),
            italic: styled ? italic : current.Italic,
            underline: styled ? underline : current.Underline,
            strikethrough: styled ? strikethrough : current.Strikethrough,
            weight: styled ? weight ?? VBFont.NormalWeight : current.Weight);
        value = font;
        return true;
    }

    // A face as GDI holds one, at most 31 characters, and as the .frm writer can quote it, without '"' or a control
    // character; and not a size or a style mistyped (#952 review).
    private static bool IsFace(string face) =>
        face.Length <= 31 && !face.Any(c => c == '"' || char.IsControl(c)) && !NotAFace().IsMatch(face);

    private static bool TryParseEnum(Type type, string text, out object? value)
    {
        value = null;
        if (LeadingNumber().Match(text) is { Success: true } numbered)
        {
            if (!long.TryParse(numbered.Groups["number"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                return false;
            var candidate = Enum.ToObject(type, n);
            if (!Enum.IsDefined(type, candidate)) return false;
            value = candidate;
            return true;
        }

        var wanted = Squeezed(text);
        foreach (var member in Enum.GetValues(type))
        {
            if (Squeezed(DisplayName(member)) == wanted || Squeezed(member.ToString()!.TrimStart('_')) == wanted)
            {
                value = member;
                return true;
            }
        }
        return false;
    }

    private static string DisplayName(object member) => Vb6EnumNames.For(member) ?? member.ToString()!.TrimStart('_');

    private static string Squeezed(string text) =>
        new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();

    private static bool Parsed<T>(bool ok, T parsed, out object? value)
    {
        value = ok ? parsed : null;
        return ok;
    }
}

/// <summary>
/// A property of a control as an automation client reads it (#923): its name, as the Properties window shows it,
/// its value as text, whether set_control_property takes that text back, and, where the value is not what the row
/// alone would suggest, why.
/// </summary>
public sealed record PropertyRow(string Name, string Value, bool Settable, string? Note = null);
