using CommunityToolkit.Mvvm.ComponentModel;
using HexIDE.IDE;

namespace HexIDE.Forms.ViewModels.Options;

/// <summary>Editor &#8594; Formatting: indentation settings.</summary>
public partial class EditorFormattingPageViewModel : ObservableObject, IOptionsPage
{
    private readonly ISettingsService _settings;

    public EditorFormattingPageViewModel(ISettingsService settings)
    {
        _settings = settings;
        LoadFromSettings();
    }

    public string Title => "Formatting";

    [ObservableProperty] private int _tabWidth;

    public void LoadFromSettings() => TabWidth = _settings.TabWidth;

    public void SaveToSettings() => _settings.TabWidth = TabWidth;

    public void RestoreDefaults() => TabWidth = SettingsDefaults.TabWidth;
}
