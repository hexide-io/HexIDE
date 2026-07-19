using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using HexIDE.IDE;
using HexIDE.Keymaps;

namespace HexIDE.Forms.ViewModels.Options;

/// <summary>
/// Environment &#8594; Keymap: keyboard shortcut pack selection. Commits on OK (no live
/// preview) — applying mid-edit would rebind the global command table on every change.
/// On OK the chosen pack is persisted and applied immediately (no restart needed).
/// </summary>
public partial class KeymapPageViewModel : ObservableObject, IOptionsPage
{
    private readonly ISettingsService _settings;
    private readonly IKeymapService _keymapService;

    public KeymapPageViewModel(ISettingsService settings, IKeymapService keymapService)
    {
        _settings = settings;
        _keymapService = keymapService;
        LoadFromSettings();
    }

    public string Title => "Keymap";

    public IReadOnlyList<string> AvailableKeymaps => _keymapService.AvailableKeymaps;

    [ObservableProperty] private string _selectedKeymap = "Default";

    public void LoadFromSettings() => SelectedKeymap = _settings.ActiveKeymap;

    public void SaveToSettings()
    {
        _settings.ActiveKeymap = SelectedKeymap;
        if (_keymapService.ActiveKeymap != SelectedKeymap)
            _keymapService.Apply(SelectedKeymap);
    }

    public void RestoreDefaults() => SelectedKeymap = SettingsDefaults.ActiveKeymap;
}
