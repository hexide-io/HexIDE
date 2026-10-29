using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using HexIDE.Automation;

namespace HexIDE.Converters;

/// <summary>
/// An automation name from a label, or a button-like control's content and tooltip: a caption as displayed, without the
/// underscore that marks its access key; else, for content that is not text, the tooltip. Anything else is left
/// to the peer.
/// </summary>
/// <remarks>
/// A button's automation peer names it by its content, marker included, so the New Project dialog's Open button
/// was announced and addressed as "_Open" (#578). For an icon the content is a Path, whose name is its type,
/// "Avalonia.Controls.Shapes.Path", the same on every icon button (#542). Menu items were already right; their
/// peer strips the marker itself.
/// </remarks>
public sealed class AccessKeyNameConverter : IValueConverter, IMultiValueConverter
{
    public static readonly AccessKeyNameConverter Instance = new();

    // Labels in Options keep punctuation visually, but omit trailing colons when announced.
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string caption
            ? parameter is "TrimColon"
                ? MenuPath.StripAccessKey(caption).TrimEnd().TrimEnd(':', '：').TrimEnd()
                : MenuPath.StripAccessKey(caption)
            : AvaloniaProperty.UnsetValue;

    /// <param name="values">The content, then the tooltip.</param>
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var content = values.Count > 0 ? values[0] : null;
        var tip = values.Count > 1 ? values[1] : null;
        if (content is string caption)
            return caption.Contains('_') ? Convert(caption, targetType, parameter, culture) : AvaloniaProperty.UnsetValue;
        return content is not null && tip is string { Length: > 0 } text ? text : AvaloniaProperty.UnsetValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
