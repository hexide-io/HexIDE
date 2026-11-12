using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Data.Converters;
using Dock.Model.Core;

namespace HexIDE.Automation;

// Layout keys identify tools independently of translated titles and project names (#3825).
public sealed class ToolAutomation : IValueConverter, IMultiValueConverter
{
    private sealed record Identity(string Key);
    private static readonly ConditionalWeakTable<IDockable, Identity> Keys = new();
    public static readonly ToolAutomation Instance = new();

    public static void Register(IDockable tool, string key)
    {
        Keys.Remove(tool);
        Keys.Add(tool, new Identity(key));
    }

    public static string? KeyOf(IDockable tool) => Keys.TryGetValue(tool, out var identity) ? identity.Key : null;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is IDock dock)
        {
            var keys = dock.VisibleDockables?.Select(KeyOf).OfType<string>().Order(StringComparer.Ordinal).ToArray() ?? [];
            return keys.Length switch
            {
                0 => AvaloniaProperty.UnsetValue,
                1 => "Tool." + keys[0],
                _ => "ToolGroup." + string.Join("+", keys),
            };
        }
        return value is IDockable tool && KeyOf(tool) is { } key ? "Tool." + key : AvaloniaProperty.UnsetValue;
    }

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        Convert(values.Count > 0 ? values[0] : null, targetType, parameter, culture);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
