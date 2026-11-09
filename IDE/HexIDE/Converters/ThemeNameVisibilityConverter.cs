using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace HexIDE.Converters;

// Keeps pack ids as selection values while the two accessibility packs display localized names.
public sealed class ThemeNameVisibilityConverter : IValueConverter
{
    public static readonly ThemeNameVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        parameter is string id ? Equals(value, id) : value is not ("HighContrastLight" or "HighContrastDark");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
