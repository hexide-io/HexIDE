using CommunityToolkit.Mvvm.ComponentModel;
using HexIDE.IDE;

namespace HexIDE.Forms.ViewModels.Options;

/// <summary>
/// The dev-mode-gated Developer options page. Only added to the Options tree when the session is in
/// developer mode (see <see cref="IDeveloperModeService"/>). Edits persisted capability settings that
/// are themselves inert unless dev mode is active.
/// </summary>
public partial class DeveloperPageViewModel : ObservableObject, IOptionsPage
{
    private readonly ISettingsService _settings;

    public DeveloperPageViewModel(ISettingsService settings)
    {
        _settings = settings;
        LoadFromSettings();
    }

    public string Title => "Developer";

    [ObservableProperty] private bool _loadUnsignedAddins;

    public void LoadFromSettings() => LoadUnsignedAddins = _settings.LoadUnsignedAddins;

    public void SaveToSettings() => _settings.LoadUnsignedAddins = LoadUnsignedAddins;

    public void RestoreDefaults() => LoadUnsignedAddins = SettingsDefaults.LoadUnsignedAddins;
}
