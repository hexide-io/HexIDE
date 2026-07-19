using CommunityToolkit.Mvvm.ComponentModel;
using HexIDE.IDE;

namespace HexIDE.Forms.ViewModels.Options;

/// <summary>Form Designer &#8594; Grid: form-canvas grid settings.</summary>
public partial class FormDesignerGridPageViewModel : ObservableObject, IOptionsPage
{
    private readonly ISettingsService _settings;

    public FormDesignerGridPageViewModel(ISettingsService settings)
    {
        _settings = settings;
        LoadFromSettings();
    }

    public string Title => "Grid";

    [ObservableProperty] private bool _showGrid;
    [ObservableProperty] private int _gridWidth;
    [ObservableProperty] private int _gridHeight;
    [ObservableProperty] private bool _alignToGrid;

    public void LoadFromSettings()
    {
        ShowGrid = _settings.ShowGrid;
        GridWidth = _settings.GridWidth;
        GridHeight = _settings.GridHeight;
        AlignToGrid = _settings.AlignToGrid;
    }

    public void SaveToSettings()
    {
        _settings.ShowGrid = ShowGrid;
        _settings.GridWidth = GridWidth;
        _settings.GridHeight = GridHeight;
        _settings.AlignToGrid = AlignToGrid;
    }

    public void RestoreDefaults()
    {
        ShowGrid = SettingsDefaults.ShowGrid;
        GridWidth = SettingsDefaults.GridWidth;
        GridHeight = SettingsDefaults.GridHeight;
        AlignToGrid = SettingsDefaults.AlignToGrid;
    }
}
