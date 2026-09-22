using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using HexIDE.Automation;

namespace HexIDE.Converters;

/// <summary>
/// A control's access-key caption as the automation name it should carry: the text as displayed, without the
/// underscore that marks the access key. Anything that is not a caption with a marker is left alone.
/// </summary>
/// <remarks>
/// A button's automation peer names it by its content string, marker included, so the New Project dialog's
/// Open button was announced and addressed as "_Open" (#578). Menu items were already right; their peer
/// strips the marker itself.
/// </remarks>
public sealed class AccessKeyNameConverter : IValueConverter
{
    public static readonly AccessKeyNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string caption && caption.Contains('_')
            ? MenuPath.StripAccessKey(caption)
            : AvaloniaProperty.UnsetValue;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
