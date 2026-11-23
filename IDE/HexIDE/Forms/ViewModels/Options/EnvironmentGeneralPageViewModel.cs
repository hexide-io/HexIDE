using CommunityToolkit.Mvvm.ComponentModel;
using HexIDE.IDE;
using HexIDE.Runtime.Debugging;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBreakOnAllErrors), nameof(IsBreakInClassModule), nameof(IsBreakOnUnhandledErrors))]
    private ErrorTrapping _errorTrapping;

    public bool IsBreakOnAllErrors
    {
        get => ErrorTrapping == ErrorTrapping.BreakOnAllErrors;
        set { if (value) ErrorTrapping = ErrorTrapping.BreakOnAllErrors; }
    }

    public bool IsBreakInClassModule
    {
        get => ErrorTrapping == ErrorTrapping.BreakInClassModule;
        set { if (value) ErrorTrapping = ErrorTrapping.BreakInClassModule; }
    }

    public bool IsBreakOnUnhandledErrors
    {
        get => ErrorTrapping == ErrorTrapping.BreakOnUnhandledErrors;
        set { if (value) ErrorTrapping = ErrorTrapping.BreakOnUnhandledErrors; }
    }

    public void LoadFromSettings()
    {
        RDCoreWin64OnlySetOnWindows = _settings.RDCoreWin64OnlySetOnWindows;
        PromptForProjectOnStartup = _settings.PromptForProjectOnStartup;
        ReloadFilesChangedOutsideIde = _settings.ReloadFilesChangedOutsideIde;
        ErrorTrapping = _settings.ErrorTrapping;
    }

    public void SaveToSettings()
    {
        _settings.RDCoreWin64OnlySetOnWindows = RDCoreWin64OnlySetOnWindows;
        _settings.PromptForProjectOnStartup = PromptForProjectOnStartup;
        _settings.ReloadFilesChangedOutsideIde = ReloadFilesChangedOutsideIde;
        _settings.ErrorTrapping = ErrorTrapping;
    }

    public void RestoreDefaults()
    {
        RDCoreWin64OnlySetOnWindows = SettingsDefaults.RDCoreWin64OnlySetOnWindows;
        PromptForProjectOnStartup = SettingsDefaults.PromptForProjectOnStartup;
        ReloadFilesChangedOutsideIde = SettingsDefaults.ReloadFilesChangedOutsideIde;
        ErrorTrapping = SettingsDefaults.ErrorTrapping;
    }
}
