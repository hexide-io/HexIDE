using CommunityToolkit.Mvvm.ComponentModel;
using HexIDE.IDE;

namespace HexIDE.Forms.ViewModels.Options;

/// <summary>Editor &#8594; General: code-editing behaviour toggles.</summary>
public partial class EditorGeneralPageViewModel : ObservableObject, IOptionsPage
{
    private readonly ISettingsService _settings;

    public EditorGeneralPageViewModel(ISettingsService settings)
    {
        _settings = settings;
        LoadFromSettings();
    }

    public string Title => "General";

    [ObservableProperty] private bool _requireVariableDeclaration;
    [ObservableProperty] private bool _autoListMembers;
    [ObservableProperty] private bool _autoQuickInfo;
    [ObservableProperty] private bool _autoIndent;
    [ObservableProperty] private bool _formatOnSave;
    [ObservableProperty] private bool _isMinimapVisible;

    public void LoadFromSettings()
    {
        RequireVariableDeclaration = _settings.RequireVariableDeclaration;
        AutoListMembers = _settings.AutoListMembers;
        AutoQuickInfo = _settings.AutoQuickInfo;
        AutoIndent = _settings.AutoIndent;
        FormatOnSave = _settings.FormatOnSave;
        IsMinimapVisible = _settings.IsMinimapVisible;
    }

    public void SaveToSettings()
    {
        _settings.RequireVariableDeclaration = RequireVariableDeclaration;
        _settings.AutoListMembers = AutoListMembers;
        _settings.AutoQuickInfo = AutoQuickInfo;
        _settings.AutoIndent = AutoIndent;
        _settings.FormatOnSave = FormatOnSave;
        _settings.IsMinimapVisible = IsMinimapVisible;
    }

    public void RestoreDefaults()
    {
        RequireVariableDeclaration = SettingsDefaults.RequireVariableDeclaration;
        AutoListMembers = SettingsDefaults.AutoListMembers;
        AutoQuickInfo = SettingsDefaults.AutoQuickInfo;
        AutoIndent = SettingsDefaults.AutoIndent;
        FormatOnSave = SettingsDefaults.FormatOnSave;
        IsMinimapVisible = SettingsDefaults.IsMinimapVisible;
    }
}
