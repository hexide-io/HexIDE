using CommunityToolkit.Mvvm.ComponentModel;
using HexIDE.IDE;

namespace HexIDE.Forms.ViewModels.Options;

/// <summary>Environment &#8594; General: IDE startup behaviour.</summary>
public partial class EnvironmentGeneralPageViewModel : ObservableObject, IOptionsPage
{
    private readonly ISettingsService _settings;

    public EnvironmentGeneralPageViewModel(ISettingsService settings)
    {
        _settings = settings;
        LoadFromSettings();
    }

    public string Title => "General";

    [ObservableProperty] private bool _promptForProjectOnStartup;
    [ObservableProperty] private bool _reloadFilesChangedOutsideIde;

    public void LoadFromSettings()
    {
        PromptForProjectOnStartup = _settings.PromptForProjectOnStartup;
        ReloadFilesChangedOutsideIde = _settings.ReloadFilesChangedOutsideIde;
    }

    public void SaveToSettings()
    {
        _settings.PromptForProjectOnStartup = PromptForProjectOnStartup;
        _settings.ReloadFilesChangedOutsideIde = ReloadFilesChangedOutsideIde;
    }

    public void RestoreDefaults()
    {
        PromptForProjectOnStartup = SettingsDefaults.PromptForProjectOnStartup;
        ReloadFilesChangedOutsideIde = SettingsDefaults.ReloadFilesChangedOutsideIde;
    }
}
