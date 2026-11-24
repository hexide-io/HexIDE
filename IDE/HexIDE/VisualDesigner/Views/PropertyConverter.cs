using System;
using System.Globalization;
using Avalonia.Data.Converters;
using HexIDE.Runtime.Components;

namespace HexIDE.VisualDesigner.Views;

public class PropertyConverter : IValueConverter
{
    public static PropertyConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ComponentInstanceViewModel instance)
        {
            var property = parameter as PropertyClass;
            if (parameter is string name)
                property = name == MenuPropertyNames.SubItems
                    ? MenuPropertyNames.SubItemsProperty
                    : instance.Instance.BaseClass.PropertiesByName[name];
            if (property is not null)
                return instance.Instance.GetBoxedPropertyOrDefault(property);
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}