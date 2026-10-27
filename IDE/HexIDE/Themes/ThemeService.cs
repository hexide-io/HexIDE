using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Serilog;

namespace HexIDE.Themes;

public sealed class ThemeService : IThemeService
{
    private ResourceDictionary? _currentOverride;
    private readonly Dictionary<string, string?> _packs = new(StringComparer.OrdinalIgnoreCase);

    public string ActiveTheme { get; private set; } = "Classic";

    public IReadOnlyList<string> AvailableThemes { get; }

    public ThemeService() : this(UserDataPath.For("themes")) { }

    internal ThemeService(string packsDirectory)
    {
        using var manifest = typeof(ThemeService).Assembly.GetManifestResourceStream("HexIDE.Themes.manifest.json")!;
        var shipped = JsonSerializer.Deserialize(manifest, ThemeJsonContext.Default.StringArray)!;
        foreach (var id in shipped)
            _packs.Add(id, null);

        try
        {
            if (Directory.Exists(packsDirectory))
                foreach (var path in Directory.EnumerateFiles(packsDirectory, "*.json").Order(StringComparer.Ordinal))
                    _packs.TryAdd(Path.GetFileNameWithoutExtension(path), path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "ThemeService: Failed to discover themes in '{Directory}'", packsDirectory);
        }

        AvailableThemes = _packs.Keys.ToArray();
    }

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
            if (!_packs.TryGetValue(themeName, out var path))
                throw new InvalidOperationException($"Unknown theme '{themeName}'.");
            using var stream = path != null
                ? File.OpenRead(path)
                : AssetLoader.Open(new Uri($"avares://HexIDE/Themes/Packs/{themeName}.json"));
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

    private static ResourceDictionary BuildDictionary(ThemePackRecord pack)
    {
        var dict = new ResourceDictionary();
        if (pack.Colors is null) return dict;

        foreach (var (name, hex) in pack.Colors)
        {
            if (!ColorKeyMapping.Table.TryGetValue(name, out var entry))
            {
                Log.Warning("ThemeService: Unknown color key '{Name}' in pack '{Pack}'", name, pack.Name);
                continue;
            }

            var color = Color.Parse(hex);
            dict[entry.Key] = entry.IsBrush ? (object)new SolidColorBrush(color) : color;
        }

        return dict;
    }
}
