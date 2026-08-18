using System;
using System.Collections.Generic;
using Avalonia.Input;

namespace HexIDE.Runtime.Components;

/// <summary>
/// A menu item's <c>Shortcut</c>, as VB6 writes it into a <c>.frm</c>.
///
/// The property is not modelled — it has no <see cref="PropertyClass"/> — so it survives a load as a
/// preserved raw line and is read back from there. That is deliberate rather than an oversight to fix in
/// passing: modelling it means writing it back, and the writer's property formatting is a separate open
/// defect. Reading it to draw the menu costs nothing and risks nothing.
///
/// The syntax is a small closed set. A modifier prefix — <c>^</c> Ctrl, <c>+</c> Shift, <c>%</c> Alt — then
/// either a single character (<c>^N</c>) or a braced key name (<c>^{F4}</c>, <c>{DEL}</c>). VB6's own menu
/// templates use <c>^N</c>, <c>^O</c>, <c>^S</c>, <c>^P</c>, <c>{F1}</c> and <c>^{INSERT}</c> among others.
/// </summary>
internal static class VBMenuShortcut
{
    /// <summary>VB6's braced key names. Anything not here is not a shortcut VB6 could have written.</summary>
    private static readonly Dictionary<string, Key> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["F1"] = Key.F1, ["F2"] = Key.F2, ["F3"] = Key.F3, ["F4"] = Key.F4,
        ["F5"] = Key.F5, ["F6"] = Key.F6, ["F7"] = Key.F7, ["F8"] = Key.F8,
        ["F9"] = Key.F9, ["F10"] = Key.F10, ["F11"] = Key.F11, ["F12"] = Key.F12,
        ["INSERT"] = Key.Insert, ["INS"] = Key.Insert,
        ["DELETE"] = Key.Delete, ["DEL"] = Key.Delete,
        ["BKSP"] = Key.Back, ["BACKSPACE"] = Key.Back,
    };

    public static bool TryParse(ComponentInstance component, out KeyGesture gesture)
    {
        gesture = null!;
        return TryReadRawValue(component, out var raw) && TryParse(raw, out gesture);
    }

    /// <summary>
    /// The value of a preserved <c>Shortcut</c> line, if the component has one.
    ///
    /// Anchored on the exact name so <c>ShortcutText</c> or anything else beginning "Shortcut" cannot match,
    /// the same care <c>Index</c> needs against <c>TabIndex</c>.
    /// </summary>
    private static bool TryReadRawValue(ComponentInstance component, out string value)
    {
        const string name = "Shortcut";
        foreach (var line in component.UnknownRawPropertyLines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                continue;
            var rest = trimmed[name.Length..].TrimStart();
            if (rest.Length == 0 || rest[0] != '=')
                continue;
            value = rest[1..].Trim();
            if (value.Length > 0)
                return true;
        }
        value = "";
        return false;
    }

    internal static bool TryParse(string raw, out KeyGesture gesture)
    {
        gesture = null!;
        var modifiers = KeyModifiers.None;
        var i = 0;

        for (; i < raw.Length; i++)
        {
            switch (raw[i])
            {
                case '^': modifiers |= KeyModifiers.Control; continue;
                case '+': modifiers |= KeyModifiers.Shift; continue;
                case '%': modifiers |= KeyModifiers.Alt; continue;
            }
            break;
        }

        var rest = raw[i..];
        if (rest.Length == 0)
            return false;

        Key key;
        if (rest[0] == '{')
        {
            var close = rest.IndexOf('}');
            if (close < 0 || !NamedKeys.TryGetValue(rest[1..close], out key))
                return false;
        }
        else if (rest.Length == 1 && char.IsAsciiLetter(rest[0]))
        {
            key = Enum.Parse<Key>(char.ToUpperInvariant(rest[0]).ToString());
        }
        else if (rest.Length == 1 && char.IsAsciiDigit(rest[0]))
        {
            key = Key.D0 + (rest[0] - '0');
        }
        else
        {
            return false;
        }

        gesture = new KeyGesture(key, modifiers);
        return true;
    }
}
