using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using HexIDE.IDE;
using HexIDE.Themes;

namespace HexIDE.Forms.ViewModels.Options;

/// <summary>
/// Environment &#8594; Theme: IDE chrome theme selection. This is the one page that
/// previews live — picking a theme applies it immediately so the change is visible;
/// the shell reverts to the original theme on Cancel.
/// </summary>
public partial class ThemePageViewModel : ObservableObject, IOptionsPage
{
    private readonly ISettingsService _settings;
    private readonly IThemeService _themeService;
    private bool _loading;

    public ThemePageViewModel(ISettingsService settings, IThemeService themeService)
    {
        _settings = settings;
        _themeService = themeService;
        LoadFromSettings();
    }

    public string Title => "Theme";

    public IReadOnlyList<string> AvailableThemes => _themeService.AvailableThemes;

    [ObservableProperty] private string _selectedTheme = "Classic";

    partial void OnSelectedThemeChanged(string value)
    {
        // Only a genuine user change previews; loading the current value must not re-apply.
        if (_loading) return;
        _themeService.Apply(value);
    }

    public void LoadFromSettings()
    {
        _loading = true;
        SelectedTheme = _settings.ActiveTheme;
        _loading = false;
    }

    public void SaveToSettings() => _settings.ActiveTheme = SelectedTheme;

    // Setting SelectedTheme applies the default theme live (preview), reverted on Cancel.
    public void RestoreDefaults() => SelectedTheme = SettingsDefaults.ActiveTheme;
}
