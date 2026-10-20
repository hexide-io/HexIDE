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
    public static bool TryParse(PropertyClass property, string text, out object? value)
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
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    /// <summary>
    /// A stored value as text <see cref="TryParse"/> takes back, where that property can be set from text (#923): an
    /// enum as the Properties window's dropdown shows it, "1 - Opaque", a colour as its literal, a number with '.' in
    /// any culture. What is set through a dialog reads as a summary instead: a font as its face, size and style, a
    /// picture or other binary value as its size, or "(None)".
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
                font.Charset == 0 ? null : $"charset {font.Charset}",
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
            var shown = Shown(p, component.GetBoxedPropertyOrDefault(p));
            if (!type.IsVisual && (p == VBProperties.WidthProperty || p == VBProperties.HeightProperty || p == VBProperties.VisibleProperty))
                return new PropertyRow(p.Name, shown, false, $"a {type.VBTypeName} has no {p.Name} in VB6, so the file does not keep it");
            return new PropertyRow(p.Name, shown, TryParse(p, shown, out _));
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
            var index = int.Parse(element.Groups["index"].Value, CultureInfo.InvariantCulture);
            named = named.Where(c => ArrayIndexOf(c) == index);
        }
        return named.ToList();
    }

    /// <summary>A control array element's Index, which HexIDE keeps as the file's own line, or null.</summary>
    public static int? ArrayIndexOf(ComponentInstance component) =>
        int.TryParse(RawOf(component, "Index"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ? index : null;

    [GeneratedRegex(@"^(?<name>[^()]+)\(\s*(?<index>\d+)\s*\)$")]
    private static partial Regex ArrayElement();

    private static string? RawOf(ComponentInstance component, string name) =>
        component.UnknownRawProperties.Where(raw => string.Equals(raw.Name, name, StringComparison.OrdinalIgnoreCase))
            .Select(RawText).FirstOrDefault();

    // A one-line property as the text after its '=', a BeginProperty block as its lines.
    private static string RawText(ComponentInstance.UnknownRawProperty raw) =>
        raw.Lines is [var line] && line.IndexOf('=') is var eq and >= 0
            ? line[(eq + 1)..].Trim()
            : string.Join("\n", raw.Lines.Select(l => l.Trim()));

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
