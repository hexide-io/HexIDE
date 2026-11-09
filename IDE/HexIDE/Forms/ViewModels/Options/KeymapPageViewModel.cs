using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HexIDE.IDE;
using HexIDE.Keymaps;
using HexIDE.Localization;

namespace HexIDE.Forms.ViewModels.Options;

/// <summary>Stages shortcut edits until Options commits them on OK.</summary>
public partial class KeymapPageViewModel : ObservableObject, IOptionsPage
{
    private readonly ISettingsService _settings;
    private readonly IKeymapService _keymapService;
    private readonly ILocalizationService _localization;
    private readonly IWindowManager? _windows;
    private Dictionary<string, string> _overrides = [];
    private bool _dirty;
    private KeyGesture? _pending;
    private readonly string? _profileKeymap;

    public KeymapPageViewModel(ISettingsService settings, IKeymapService keymapService,
        ILocalizationService? localization = null, IWindowManager? windows = null)
    {
        _settings = settings;
        _keymapService = keymapService;
        _localization = localization ?? new LocalizationService();
        _windows = windows;
        if (keymapService.HasProfileKeymap)
            _profileKeymap = keymapService.ActiveKeymap;
        LoadFromSettings();
    }

    public string Title => "Keymap";
    public IReadOnlyList<string> AvailableKeymaps => _profileKeymap is null || _keymapService.AvailableKeymaps.Contains(_profileKeymap)
        ? _keymapService.AvailableKeymaps : [.._keymapService.AvailableKeymaps, _profileKeymap];
    [ObservableProperty] private string _selectedKeymap = "Default";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private KeymapCommandViewModel? _selectedCommand;
    [ObservableProperty] private bool _isCapturing;
    [ObservableProperty] private string _conflict = "";
    [ObservableProperty] private string _error = "";
    public bool HasConflict => Conflict.Length != 0;
    public IReadOnlyList<KeymapCommandViewModel> Commands { get; private set; } = [];
    public IEnumerable<KeymapCommandViewModel> FilteredCommands => Commands.Where(c =>
        c.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
        c.Id.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
        c.Gesture.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

    private string Text(string key) => _localization.GetString(key);
    partial void OnSearchTextChanged(string value) => OnPropertyChanged(nameof(FilteredCommands));
    partial void OnSelectedKeymapChanged(string value) => Rebuild();
    partial void OnSelectedCommandChanged(KeymapCommandViewModel? value) => CancelCapture();
    partial void OnConflictChanged(string value) => OnPropertyChanged(nameof(HasConflict));

    private void Rebuild()
    {
        var selected = SelectedCommand?.Id;
        IReadOnlyDictionary<string, IReadOnlyList<KeyGesture>> baseline;
        try { baseline = _keymapService.GetPackBindings(SelectedKeymap); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException
            or FormatException or ArgumentException or InvalidOperationException)
        {
            Error = string.Format(Text("Str.Archive.OpenErrorFormat"), SelectedKeymap, ex.Message);
            baseline = _keymapService.GetPackBindings("Default");
        }
        Commands = CommandKeyMapping.Table.Select(p =>
        {
            var name = CommandDisplayNames.DisplayName(p.Key, Text);
            var user = _overrides.TryGetValue(p.Key, out var gesture);
            return new KeymapCommandViewModel(p.Key, name,
                user ? DisplayGesture(gesture!) : baseline.TryGetValue(p.Key, out var keys) ? string.Join(", ", keys.Select(k => DisplayGesture(k.ToString()))) : "",
                Text(user ? "Str.Options.Keymap.User" : "Str.Options.Keymap.Pack"),
                user ? (gesture!.Length == 0 ? [] : [KeyGesture.Parse(gesture)]) :
                    baseline.TryGetValue(p.Key, out var values) ? values : []);
        }).ToArray();
        OnPropertyChanged(nameof(Commands));
        OnPropertyChanged(nameof(FilteredCommands));
        SelectedCommand = Commands.FirstOrDefault(c => c.Id == selected);
    }

    public void LoadFromSettings()
    {
        _overrides = new(_keymapService.LoadOverrides());
        SelectedKeymap = _profileKeymap ?? _settings.ActiveKeymap;
        _dirty = false;
        Rebuild();
    }

    public void SaveToSettings()
    {
        if (_dirty) _keymapService.SaveOverrides(_overrides);
        if (SelectedKeymap != _profileKeymap) _settings.ActiveKeymap = SelectedKeymap;
        if (_keymapService.ActiveKeymap != SelectedKeymap) _keymapService.Apply(SelectedKeymap);
        else if (_dirty) _keymapService.RefreshOverrides();
        _dirty = false;
    }

    public void RestoreDefaults()
    {
        SelectedKeymap = SettingsDefaults.ActiveKeymap;
        ResetAll();
    }

    [RelayCommand] private void Set() { if (SelectedCommand is not null) { CancelCapture(); IsCapturing = true; } }
    [RelayCommand] private void CancelCapture() { IsCapturing = false; _pending = null; Conflict = ""; }

    public void Capture(KeyGesture gesture)
    {
        if (!IsCapturing || SelectedCommand is null) return;
        IsCapturing = false;
        var conflicts = Commands.Where(c => c.Id != SelectedCommand.Id && c.Keys.Contains(gesture)).Select(c => c.Name).ToArray();
        if (conflicts.Length != 0)
        {
            _pending = gesture;
            Conflict = string.Format(Text("Str.Options.Keymap.Conflict"), string.Join(", ", conflicts));
        }
        else Assign(gesture.ToString());
    }

    [RelayCommand] private void Keep() { if (_pending is { } gesture) Assign(gesture.ToString()); }
    private void Assign(string gesture)
    {
        if (SelectedCommand is null) return;
        _overrides[SelectedCommand.Id] = gesture;
        _dirty = true;
        CancelCapture();
        Rebuild();
    }
    [RelayCommand] private void Clear() => Assign("");
    [RelayCommand] private void Reset()
    {
        if (SelectedCommand is null) return;
        _overrides.Remove(SelectedCommand.Id);
        _dirty = true;
        CancelCapture();
        Rebuild();
    }
    [RelayCommand] private void ResetAll() { _overrides.Clear(); _dirty = true; CancelCapture(); Rebuild(); }

    internal bool ImportJson(string json)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                (!document.RootElement.TryGetProperty("bindings", out var member) || member.ValueKind == System.Text.Json.JsonValueKind.Null))
            {
                Error = string.Format(Text("Str.Options.Keymap.InvalidImport"), Text("Str.Options.Keymap.MissingBindings"));
                return false;
            }
            var bindings = KeymapService.ParseOverrides(json);
            _overrides = bindings;
            _dirty = true;
            Error = "";
            CancelCapture();
            Rebuild();
            return true;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or FormatException or ArgumentException or InvalidOperationException or OverflowException)
        {
            Error = string.Format(Text("Str.Options.Keymap.InvalidImport"), ex.Message);
            return false;
        }
    }
    private string DisplayGesture(string gesture) => gesture
        .Replace("OemPlus", "+").Replace("OemMinus", "-").Replace("OemComma", ",")
        .Replace("OemPeriod", ".").Replace("OemQuestion", "/").Replace("OemSemicolon", ";")
        .Replace("OemQuotes", "'").Replace("OemOpenBrackets", "[").Replace("OemCloseBrackets", "]")
        .Replace("OemPipe", "\\").Replace("OemTilde", "`").Replace("Return", Text("Str.Options.Keymap.Enter"));

    internal string ExportJson() => KeymapService.SerializeOverrides(_overrides);

    [RelayCommand] private async Task Import()
    {
        if (_windows is null) return;
        var files = await _windows.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false, FileTypeFilter = [new FilePickerFileType(Text("Str.Options.Keymap.Header")) { Patterns = ["*.json"] }] });
        if (files is not { Count: > 0 }) return;
        try { ImportJson(await File.ReadAllTextAsync(files[0])); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Error = string.Format(Text("Str.Options.Keymap.InvalidImport"), ex.Message); }
    }
    [RelayCommand] private async Task Export()
    {
        if (_windows is null) return;
        var file = await _windows.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = "bindings.json", FileTypeChoices = [new FilePickerFileType(Text("Str.Options.Keymap.Header")) { Patterns = ["*.json"] }] });
        if (file is null) return;
        try { AtomicFileWriter.WriteAllText(file, ExportJson()); Error = ""; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Error = string.Format(Text("Str.Archive.OpenErrorFormat"), file, ex.Message); }
    }
}

public sealed record KeymapCommandViewModel(string Id, string Name, string Gesture, string Source, IReadOnlyList<KeyGesture> Keys)
{
    public string AccessibleName => string.Join(", ", new[] { Name, Gesture, Source }.Where(part => part.Length > 0));
}
