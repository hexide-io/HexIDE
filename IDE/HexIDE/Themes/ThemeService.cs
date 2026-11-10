using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Avalonia.Media;
using Avalonia.Markup.Xaml.Styling;
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
        ApplyChromeBorders(pack.ChromeBorderThickness);
        ActiveTheme = pack.Name;
        app.RequestedThemeVariant = pack.ThemeVariant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        ThemeChanged?.Invoke();
    }

    internal void FollowPlatform(IPlatformSettings platform, SettingsService settings, Action restore)
    {
        var following = false;
        void Update(PlatformColorValues colors)
        {
            if (settings.HasExplicitThemeChoice) return;
            if (colors.ContrastPreference == ColorContrastPreference.High)
            {
                following = true;
                Apply(colors.ThemeVariant == PlatformThemeVariant.Dark ? "HighContrastDark" : "HighContrastLight");
            }
            else if (following)
            {
                following = false;
                restore();
            }
        }

        platform.ColorValuesChanged += (_, colors) =>
        {
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) Update(colors);
            else Avalonia.Threading.Dispatcher.UIThread.Post(() => Update(colors));
        };
        Update(platform.GetColorValues());
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
    private StyleInclude? _chromeBorders;
    private readonly Func<IEnumerable<Visual>> _roots;
    private readonly Dictionary<string, string?> _packs = new(StringComparer.OrdinalIgnoreCase);

    public string ActiveTheme { get; private set; } = "Classic";

    public IReadOnlyList<string> AvailableThemes { get; }

    public ThemeService() : this(UserDataPath.For("themes")) { }

    internal ThemeService(string packsDirectory, Func<IEnumerable<Visual>>? roots = null)
    {
        _roots = roots ?? ApplicationRoots;
        using var manifest = typeof(ThemeService).Assembly.GetManifestResourceStream("HexIDE.Themes.manifest.json")!;
        var shipped = JsonSerializer.Deserialize(manifest, ThemeJsonContext.Default.StringArray)!;
        foreach (var id in shipped)
            _packs.Add(id, null);

        try
        {
            if (!Static.IsRecoveryMode && Directory.Exists(packsDirectory))
                foreach (var path in Directory.EnumerateFiles(packsDirectory, "*.json").Order(StringComparer.Ordinal))
                    _packs.TryAdd(Path.GetFileNameWithoutExtension(path), path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "ThemeService: Failed to discover themes in '{Directory}'", packsDirectory);
        }

        AvailableThemes = _packs.Keys.ToArray();
    }

    public event Action? ThemeChanged;

    public void Apply(string themeName)
    {
        ApplyCore(themeName);
        ThemeChanged?.Invoke();
    }

    private void ApplyCore(string themeName)
    {
        var app = Application.Current!;

        if (_currentOverride != null)
        {
            app.Resources.MergedDictionaries.Remove(_currentOverride);
            _currentOverride = null;
        }

        ApplyChromeBorders(null);
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
            ApplyChromeBorders(pack.ChromeBorderThickness);
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

    private void ApplyChromeBorders(double? thickness)
    {
        var styles = Application.Current!.Styles;
        if (thickness is > 0)
        {
            _chromeBorders ??= new StyleInclude(new Uri("avares://HexIDE/"))
            {
                Source = new Uri("avares://HexIDE/Themes/ChromeBorders.axaml")
            };
            if (!styles.Contains(_chromeBorders))
                styles.Add(_chromeBorders);
        }
        else if (_chromeBorders is { } borders && styles.Remove(borders))
        {
            // Avalonia's global removal walks logical children, leaving styles on visual-only
            // template parts and popup children. ClassicRoundTripRestoresExistingMenuAndToolChrome
            // checks restored bounds, borders and padding, including reopened menus and Dock buttons.
            var visited = new HashSet<Visual>();
            void Detach(Visual visual)
            {
                if (!visited.Add(visual)) return;
                ((IStyleHost)visual).StylesRemoved([borders]);
                visual.ApplyStyling();
                foreach (var child in visual.GetVisualChildren()) Detach(child);
                if (visual is Popup { Child: { } content }) Detach(content);
            }
            foreach (var root in _roots())
            {
                // This notification invalidates styling; it does not add the include back to Styles.
                // Without it ApplyStyling skips surviving template rules: the round-trip test leaves
                // Dock's PART_Grid 20 DIP narrower and its close-button panel 20 DIP wider.
                ((IStyleHost)root).StylesAdded([borders]);
                Detach(root);
            }
        }
    }

    private static IEnumerable<Visual> ApplicationRoots() => Application.Current!.ApplicationLifetime switch
    {
        IClassicDesktopStyleApplicationLifetime desktop => desktop.Windows,
        ISingleViewApplicationLifetime { MainView: { } view } => [view],
        _ => []
    };

    private static ResourceDictionary BuildDictionary(ThemePackRecord pack, string? source = null, string? file = null)
    {
        var dict = new ResourceDictionary();
        if (pack.PlaceholderOpacity is { } opacity)
        {
            if (!double.IsFinite(opacity) || opacity < 0 || opacity > 1)
                throw new ArgumentException("Placeholder opacity must be between zero and one.");
            dict["TextControlPlaceholderOpacity"] = opacity;
        }
        if (pack.TabSelectionBorderThickness is { } thickness)
        {
            if (!double.IsFinite(thickness) || thickness < 0)
                throw new ArgumentException("Tab selection border thickness must be non-negative.");
            dict["TabSelectionBorderThickness"] = new Thickness(thickness);
        }
        if (pack.ChromeBorderThickness is { } chromeThickness)
        {
            if (!double.IsFinite(chromeThickness) || chromeThickness < 0)
                throw new ArgumentException("Chrome border thickness must be non-negative.");
            dict["ChromeBorderThickness"] = new Thickness(chromeThickness);
            dict["ChromeBorderStrokeThickness"] = chromeThickness;
        }
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
