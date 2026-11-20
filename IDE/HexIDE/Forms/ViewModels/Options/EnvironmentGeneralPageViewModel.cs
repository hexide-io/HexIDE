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
    [ObservableProperty] private bool _rDCoreWin64OnlySetOnWindows;
    [ObservableProperty] private bool _reloadFilesChangedOutsideIde;

    public void LoadFromSettings()
    {
        RDCoreWin64OnlySetOnWindows = _settings.RDCoreWin64OnlySetOnWindows;
        PromptForProjectOnStartup = _settings.PromptForProjectOnStartup;
        ReloadFilesChangedOutsideIde = _settings.ReloadFilesChangedOutsideIde;
    }

    public void SaveToSettings()
    {
        _settings.RDCoreWin64OnlySetOnWindows = RDCoreWin64OnlySetOnWindows;
        _settings.PromptForProjectOnStartup = PromptForProjectOnStartup;
        _settings.ReloadFilesChangedOutsideIde = ReloadFilesChangedOutsideIde;
    }

    public void RestoreDefaults()
    {
        RDCoreWin64OnlySetOnWindows = SettingsDefaults.RDCoreWin64OnlySetOnWindows;
        PromptForProjectOnStartup = SettingsDefaults.PromptForProjectOnStartup;
        ReloadFilesChangedOutsideIde = SettingsDefaults.ReloadFilesChangedOutsideIde;
    }
}
