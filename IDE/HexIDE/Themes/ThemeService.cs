using System;
using System.Collections.Generic;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Serilog;
using HexIDE.IDE;
using HexIDE.Profiles;

namespace HexIDE.Themes;

public sealed class ThemeService : IThemeService
{
    internal void Apply(ProfileSnapshot profile, SettingsService settings)
    {
        if (profile.Theme is not { } payload || settings.HasExplicitThemeChoice)
        {
            Apply(settings.ActiveTheme);
            return;
        }

        var (pack, dictionary) = PrepareProfileTheme(profile.Source, payload);
        var app = Application.Current!;
        if (_currentOverride != null)
            app.Resources.MergedDictionaries.Remove(_currentOverride);
        _currentOverride = dictionary;
        app.Resources.MergedDictionaries.Add(dictionary);
        ActiveTheme = pack.Name;
        app.RequestedThemeVariant = pack.ThemeVariant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    internal static (ThemePackRecord Pack, ResourceDictionary Resources) PrepareProfileTheme(string source, ProfilePayload payload)
    {
        try
        {
            var pack = payload.Content.Deserialize(ThemeJsonContext.Default.ThemePackRecord) ?? throw new JsonException();
            return (pack, BuildDictionary(pack, source, payload.File));
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or InvalidOperationException)
        {
            throw new ProfileLoadException(source, payload.File, ProfileError.InvalidPayload, ex);
        }
    }

    private ResourceDictionary? _currentOverride;

    public string ActiveTheme { get; private set; } = "Classic";

    public IReadOnlyList<string> AvailableThemes { get; } = ["Classic", "Dark", "Abyss"];

    public void Apply(string themeName)
    {
        var app = Application.Current!;

        if (_currentOverride != null)
        {
            app.Resources.MergedDictionaries.Remove(_currentOverride);
            _currentOverride = null;
        }

        ActiveTheme = themeName;

        if (themeName == "Classic")
        {
            // Classic always renders SimpleTheme's Light variant. VB6 chrome is light
            // regardless of the host OS dark/light setting, and the form-designer canvas
            // is already locked to Light via ThemeVariantScope. Using Default here would
            // follow the OS, leaving most chrome (menu, toolbox, dock headers, status bar)
            // stuck dark on a dark-mode OS while the pinned Classic.axaml keys stayed light.
            app.RequestedThemeVariant = ThemeVariant.Light;
            Log.Debug("ThemeService: Applied Classic theme");
            return;
        }

        try
        {
            var uri = new Uri($"avares://HexIDE/Themes/Packs/{themeName}.json");
            using var stream = AssetLoader.Open(uri);
            var pack = JsonSerializer.Deserialize(stream, ThemeJsonContext.Default.ThemePackRecord)
                       ?? throw new InvalidOperationException($"Theme pack '{themeName}' deserialized to null.");

            _currentOverride = BuildDictionary(pack);
            app.Resources.MergedDictionaries.Add(_currentOverride);
            // Themes fully specify their own colours, so the variant is deterministic and
            // never follows the OS: dark packs use Dark, everything else uses Light.
            app.RequestedThemeVariant = pack.ThemeVariant == "Dark"
                ? ThemeVariant.Dark
                : ThemeVariant.Light;

            Log.Debug("ThemeService: Applied theme '{ThemeName}'", themeName);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ThemeService: Failed to apply theme '{ThemeName}', reverting to Classic", themeName);
            ActiveTheme = "Classic";
            app.RequestedThemeVariant = ThemeVariant.Light;
        }
    }

    private static ResourceDictionary BuildDictionary(ThemePackRecord pack, string? source = null, string? file = null)
    {
        var dict = new ResourceDictionary();
        if (pack.Colors is null) return dict;

        foreach (var (name, hex) in pack.Colors)
        {
            if (!ColorKeyMapping.Table.TryGetValue(name, out var entry))
            {
                Log.Warning("ThemeService: Unknown color key '{Name}' in pack '{Pack}' (source '{Source}', file '{File}')",
                    name, pack.Name, source, file);
                continue;
            }

            var color = Color.Parse(hex);
            dict[entry.Key] = entry.IsBrush ? (object)new SolidColorBrush(color) : color;
        }

        return dict;
    }
}
