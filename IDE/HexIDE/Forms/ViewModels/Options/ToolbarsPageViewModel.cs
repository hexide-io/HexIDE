using CommunityToolkit.Mvvm.ComponentModel;
using HexIDE.IDE;

namespace HexIDE.Forms.ViewModels.Options;

/// <summary>Environment &#8594; Toolbars: toolbar visibility toggles (mirrors the View menu).</summary>
public partial class ToolbarsPageViewModel : ObservableObject, IOptionsPage
{
    private readonly ISettingsService _settings;

    public ToolbarsPageViewModel(ISettingsService settings)
    {
        _settings = settings;
        LoadFromSettings();
    }

    public string Title => "Toolbars";

    [ObservableProperty] private bool _standardToolbarVisible;
    [ObservableProperty] private bool _editToolbarVisible;
    [ObservableProperty] private bool _debugToolbarVisible;
    [ObservableProperty] private bool _formEditorToolbarVisible;

    public void LoadFromSettings()
    {
        StandardToolbarVisible = _settings.IsStandardToolbarVisible;
        EditToolbarVisible = _settings.IsEditToolbarVisible;
        DebugToolbarVisible = _settings.IsDebugToolbarVisible;
        FormEditorToolbarVisible = _settings.IsFormEditorToolbarVisible;
    }

    public void SaveToSettings()
    {
        _settings.IsStandardToolbarVisible = StandardToolbarVisible;
        _settings.IsEditToolbarVisible = EditToolbarVisible;
        _settings.IsDebugToolbarVisible = DebugToolbarVisible;
        _settings.IsFormEditorToolbarVisible = FormEditorToolbarVisible;
    }

    public void RestoreDefaults()
    {
        StandardToolbarVisible = SettingsDefaults.IsStandardToolbarVisible;
        EditToolbarVisible = SettingsDefaults.IsEditToolbarVisible;
        DebugToolbarVisible = SettingsDefaults.IsDebugToolbarVisible;
        FormEditorToolbarVisible = SettingsDefaults.IsFormEditorToolbarVisible;
    }
}
