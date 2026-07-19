using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace HexIDE.Utils;

/// <summary>
/// Value converters for the main window status bar panels.
/// </summary>
public static class StatusBarConverters
{
    /// <summary>Converts bool InsertMode → "INS" or "OVR".</summary>
    public static readonly FuncValueConverter<bool, string> InsertModeConverter =
        new(insertMode => insertMode ? "INS" : "OVR");

    /// <summary>Converts (int? Line, int? Column) → "Ln x, Col y" or "".</summary>
    public static readonly LinColMultiConverter LinColConverter = new();
}

/// <summary>
/// MultiValueConverter: (int? Line, int? Column) → "Ln x, Col y" or "".
/// </summary>
public sealed class LinColMultiConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 2 && values[0] is int line && values[1] is int col)
            return $"Ln {line}, Col {col}";
        return "";
    }
}
