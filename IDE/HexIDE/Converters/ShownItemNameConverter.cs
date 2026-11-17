using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using HexIDE.Localization;

namespace HexIDE.Converters;

/// <summary>
/// The accessible name of an Options list item whose template shows a localized string for some rows: the text that row
/// shows. These are the language list's "System default" and the theme list's two high-contrast packs.
/// </summary>
/// <remarks>
/// Avalonia names a list item from its template only when the template is a single <c>TextBlock</c>. These templates
/// switch between several, so the item fell back to its <c>ToString</c>. A screen reader read
/// <c>LanguageInfo { Id = ar, … }</c>, or the theme pack's id, <c>HighContrastLight</c>, beside "High contrast light"
/// (#4176). The name is read when the row's container binds, so after a live language switch a row already realized
/// keeps its earlier name until the list makes it again.
/// </remarks>
public sealed class ShownItemNameConverter : IValueConverter
{
    public static readonly ShownItemNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        LanguageInfo { Id: "system" } language => Shown("Str.Options.Language.SystemDefault") ?? language.DisplayName,
        LanguageInfo language => language.DisplayName,
        "HighContrastLight" or "HighContrastDark" => Shown("Str.Theme." + value) ?? value,
        _ => value?.ToString(),
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    // The same resource the row's TextBlock shows through DynamicResource.
    private static string? Shown(string key) =>
        Application.Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out var text) ? text as string : null;
}
