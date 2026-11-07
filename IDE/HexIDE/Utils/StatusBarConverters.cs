using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace HexIDE.Utils;

/// <summary>
/// Value converters for the main window status bar panels.
/// </summary>
public static class StatusBarConverters
{
    /// <summary>Selects the localized insert or overtype label.</summary>
    public static readonly InsertModeMultiConverter InsertModeConverter = new();

    /// <summary>Formats the caret position using the localized format, or returns "".</summary>
    public static readonly LinColMultiConverter LinColConverter = new();
}

/// <summary>
/// MultiValueConverter: (int? Line, int? Column, string Format) → localized caret position or "".
/// </summary>
public sealed class LinColMultiConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 3 && values[0] is int line && values[1] is int col && values[2] is string format)
            return string.Format(culture, format, line, col);
        return "";
    }
}

public sealed class InsertModeMultiConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 3 && values[0] is bool insertMode)
            return values[insertMode ? 1 : 2] as string ?? "";
        return "";
    }
}
