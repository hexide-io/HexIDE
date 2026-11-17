using System;

namespace HexIDE.Runtime.Components;

/// <summary>Design-time size validation shared by the Properties window and automation (#589).</summary>
public static class DesignerSizeValidation
{
    public static bool Refuses(PropertyClass property, object? value)
    {
        if (property != VBProperties.WidthProperty && property != VBProperties.HeightProperty)
            return false;
        if (value is string text && PropertyText.TryParse(property, text, out var parsed))
            value = parsed;
        return value is double number && number < 0;
    }

    public static bool Refuses(double? width, double? height) => width is < 0 || height is < 0;
}
