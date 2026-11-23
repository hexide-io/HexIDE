using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Converters;

public sealed class MenuItemNameConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count != 4 || values[0] is not string caption || values[1] is not int indent
            || values[2] is not string blank || values[3] is not string level)
            return AvaloniaProperty.UnsetValue;
        var name = VBCaptionText.Shown(caption);
        if (name.Length == 0)
            name = blank;
        return indent == 0 ? name : string.Format(culture, level, name, indent + 1);
    }
}
