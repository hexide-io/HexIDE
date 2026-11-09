using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media;
using AvaloniaEdit.Highlighting;
using HexIDE.IDE;
using HexIDE.Themes;
using HexIDE.Localization;

namespace HexIDE.Forms.ViewModels.Options;

public partial class EditorFormattingPageViewModel : ObservableObject, IOptionsPage, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IThemeService? _themes;
    private readonly ILocalizationService? _localization;
    private Dictionary<string, Dictionary<string, EditorTokenColors>> _choices = new();
    private string Theme => _themes?.ActiveTheme ?? _settings.ActiveTheme ?? "Classic";

    public EditorFormattingPageViewModel(ISettingsService settings, IThemeService? themes = null, ILocalizationService? localization = null)
    {
        _settings = settings;
        _themes = themes;
        _localization = localization;
        LoadFromSettings();
        if (_themes is not null) _themes.ThemeChanged += LoadTheme;
        if (_localization is not null) _localization.LanguageChanged += RefreshLabels;
    }

    public string Title => "Formatting";
    public ObservableCollection<EditorTokenViewModel> Tokens { get; } = [];
    [ObservableProperty] private EditorTokenViewModel? _selectedToken;
    [ObservableProperty] private IHighlightingDefinition? _previewDefinition;
    [ObservableProperty] private int _tabWidth;
    [ObservableProperty] private string _fontFamily = SettingsDefaults.EditorFontFamily;
    [ObservableProperty] private double _fontSize = SettingsDefaults.EditorFontSize;

    public void LoadFromSettings()
    {
        TabWidth = _settings.TabWidth;
        FontFamily = string.IsNullOrWhiteSpace(_settings.EditorFontFamily) ? SettingsDefaults.EditorFontFamily : _settings.EditorFontFamily;
        FontSize = _settings.EditorFontSize > 0 ? _settings.EditorFontSize : SettingsDefaults.EditorFontSize;
        _choices = _settings.EditorColorOverrides ?? new();
        LoadTheme();
    }

    private void LoadTheme()
    {
        var selected = SelectedToken?.Name;
        Tokens.Clear();
        var definition = SyntaxHighlightingTheme.CreatePreviewDefinition();
        var choices = _choices.GetValueOrDefault(Theme);
        foreach (var color in definition.NamedHighlightingColors)
        {
            var token = new EditorTokenViewModel(color.Name, choices?.GetValueOrDefault(color.Name),
                color.Foreground?.GetColor(null!)?.ToString(), color.Background?.GetColor(null!)?.ToString(), _localization);
            token.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(EditorTokenViewModel.Foreground) or nameof(EditorTokenViewModel.Background)) UpdatePreview();
            };
            Tokens.Add(token);
        }
        SelectedToken = Tokens.FirstOrDefault(t => t.Name == selected) ?? Tokens.FirstOrDefault();
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var definition = SyntaxHighlightingTheme.CreatePreviewDefinition();
        var choices = new Dictionary<string, EditorTokenColors>(_choices.GetValueOrDefault(Theme) ?? new());
        foreach (var token in Tokens)
        {
            var foreground = Color.TryParse(token.Foreground, out var fg);
            var background = Color.TryParse(token.Background, out var bg);
            if (foreground || background)
                choices[token.Name] = new(foreground ? token.Foreground : null, background ? token.Background : null);
            else choices.Remove(token.Name);
            var color = definition.GetNamedColor(token.Name);
            if (foreground) color.Foreground = new SimpleHighlightingBrush(fg);
            if (background) color.Background = new SimpleHighlightingBrush(bg);
        }
        if (choices.Count == 0) _choices.Remove(Theme);
        else _choices[Theme] = choices;
        PreviewDefinition = definition;
    }

    [RelayCommand]
    private void ResetColors()
    {
        _choices.Remove(Theme);
        LoadTheme();
    }

    public void SaveToSettings()
    {
        _settings.TabWidth = TabWidth;
        _settings.EditorFontFamily = FontFamily;
        _settings.EditorFontSize = FontSize;
        _settings.EditorColorOverrides = _choices;
    }

    public void RestoreDefaults()
    {
        TabWidth = SettingsDefaults.TabWidth;
        FontFamily = SettingsDefaults.EditorFontFamily;
        FontSize = SettingsDefaults.EditorFontSize;
        ResetColors();
    }

    private void RefreshLabels()
    {
        foreach (var token in Tokens) token.RefreshLabel();
    }

    public void Dispose()
    {
        if (_themes is not null) _themes.ThemeChanged -= LoadTheme;
        if (_localization is not null) _localization.LanguageChanged -= RefreshLabels;
    }
}

public partial class EditorTokenViewModel(string name, EditorTokenColors? colors, string? themeForeground, string? themeBackground, ILocalizationService? localization) : ObservableObject
{
    public string Name { get; } = name;
    public string Label => localization?.GetString(Name is "String" or "Number" or "Constant" or "Function" or "Operator"
        ? "Str.Tool.Outline.Kind." + Name : "Str.Options.Formatting.Token." + Name) ?? Name;
    internal void RefreshLabel() => OnPropertyChanged(nameof(Label));
    public Color ForegroundColor
    {
        get => Color.TryParse(Foreground ?? ThemeForeground, out var color) ? color : Colors.Transparent;
        set => Foreground = value.ToString();
    }
    public Color BackgroundColor
    {
        get => Color.TryParse(Background ?? ThemeBackground, out var color) ? color : Colors.Transparent;
        set => Background = value.ToString();
    }
    public IBrush ForegroundBrush => new SolidColorBrush(ForegroundColor);
    public IBrush BackgroundBrush => new SolidColorBrush(BackgroundColor);
    partial void OnForegroundChanged(string? value)
    {
        OnPropertyChanged(nameof(ForegroundColor));
        OnPropertyChanged(nameof(ForegroundBrush));
    }
    partial void OnBackgroundChanged(string? value)
    {
        OnPropertyChanged(nameof(BackgroundColor));
        OnPropertyChanged(nameof(BackgroundBrush));
    }
    public string? ThemeForeground { get; } = themeForeground;
    public string? ThemeBackground { get; } = themeBackground;
    [ObservableProperty] private string? _foreground = colors?.Foreground;
    [ObservableProperty] private string? _background = colors?.Background;

    [RelayCommand]
    private void Reset()
    {
        Foreground = null;
        Background = null;
    }
}
