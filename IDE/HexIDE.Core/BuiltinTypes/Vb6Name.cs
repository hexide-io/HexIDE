using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace HexIDE.Runtime.BuiltinTypes;

/// <summary>
/// The name VB6 itself shows for an enum value, and writes into the <c>.frm</c> beside it.
///
/// A designer file records an enum as its number followed by a comment naming it —
/// <c>ScaleMode = 3  'Pixel</c>, <c>StartUpPosition = 3  'Windows Default</c>. The comment is not
/// documentation VB6 ignores on the way back in; it is part of what VB6 writes, so a round-trip without it
/// differs from the original on every enum-valued line.
///
/// These strings are VB6's, not ours: they carry its spacing and its capitalisation, which is why
/// <c>Windows Default</c> has a space and <c>CenterScreen</c> does not. Where the corpus shows the string,
/// the corpus is the source; the rest come from VB6's own property window.
///
/// A member with no attribute simply gets no comment. That is the deliberate fallback — an enum value
/// HexIDE cannot name should write a bare number rather than a name VB6 would not recognise.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class Vb6NameAttribute : Attribute
{
    public Vb6NameAttribute(string name) => Name = name;

    public string Name { get; }
}

/// <summary>
/// Looks up the <see cref="Vb6NameAttribute"/> for an enum value, once per enum type.
/// </summary>
public static class Vb6EnumNames
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<long, string>> byType = new();

    /// <summary>
    /// The VB6 name for this enum value, or null when the value has none — including when the number is
    /// outside the enum entirely, which a designer file is perfectly capable of containing.
    /// </summary>
    public static string? For(object? value)
    {
        if (value is null) return null;
        var type = value.GetType();
        if (!type.IsEnum) return null;

        var names = byType.GetOrAdd(type, Build);
        return names.TryGetValue(Convert.ToInt64(value), out var name) ? name : null;
    }

    private static IReadOnlyDictionary<long, string> Build(Type enumType)
    {
        var names = new Dictionary<long, string>();
        foreach (var field in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetCustomAttribute<Vb6NameAttribute>() is not { } attribute) continue;
            names[Convert.ToInt64(field.GetRawConstantValue())] = attribute.Name;
        }
        return names;
    }
}
