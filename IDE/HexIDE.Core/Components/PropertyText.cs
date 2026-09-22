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
