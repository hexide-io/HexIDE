using CommunityToolkit.Mvvm.ComponentModel;
using HexIDE.IDE;

namespace HexIDE.Forms.ViewModels.Options;

/// <summary>
/// Advanced &#8594; LSP: optional WebSocket endpoint for the VB6 language server.
/// Blank means the default stdio subprocess transport. Takes effect on the next connection.
/// </summary>
public partial class AdvancedLspPageViewModel : ObservableObject, IOptionsPage
{
    private readonly ISettingsService _settings;

    public AdvancedLspPageViewModel(ISettingsService settings)
    {
        _settings = settings;
        LoadFromSettings();
    }

    public string Title => "LSP";

    [ObservableProperty] private string? _lspWebSocketUrl;

    public void LoadFromSettings() => LspWebSocketUrl = _settings.LspWebSocketUrl;

    public void SaveToSettings()
        => _settings.LspWebSocketUrl = string.IsNullOrWhiteSpace(LspWebSocketUrl) ? null : LspWebSocketUrl.Trim();

    public void RestoreDefaults() => LspWebSocketUrl = SettingsDefaults.LspWebSocketUrl;
}
