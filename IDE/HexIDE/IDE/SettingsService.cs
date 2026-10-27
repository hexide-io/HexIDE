using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;

namespace HexIDE.IDE;

/// <summary>
/// Persists IDE settings to <c>%AppData%/HexIDE/settings.json</c>.
/// Uses CommunityToolkit.Mvvm for <see cref="System.ComponentModel.INotifyPropertyChanged"/>.
/// </summary>
public partial class SettingsService : ObservableObject, ISettingsService
{
    private readonly string _filePath;
    private readonly HashSet<string> _changedProperties = new();
    private bool _loading;
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    // Defaults derive from SettingsDefaults (the single source of truth).

    // ── Editor defaults ─────────────────────────────────────────
    private bool _requireVariableDeclaration = SettingsDefaults.RequireVariableDeclaration;
    private bool _autoListMembers = SettingsDefaults.AutoListMembers;
    private bool _autoQuickInfo = SettingsDefaults.AutoQuickInfo;
    private bool _autoIndent = SettingsDefaults.AutoIndent;
    private int _tabWidth = SettingsDefaults.TabWidth;
    private bool _formatOnSave = SettingsDefaults.FormatOnSave;

    // ── General defaults ────────────────────────────────────────
    private bool _showGrid = SettingsDefaults.ShowGrid;
    private int _gridWidth = SettingsDefaults.GridWidth;
    private int _gridHeight = SettingsDefaults.GridHeight;
    private bool _alignToGrid = SettingsDefaults.AlignToGrid;

    // ── Environment defaults ────────────────────────────────────
    private bool _promptForProjectOnStartup = SettingsDefaults.PromptForProjectOnStartup;
    private string _activeTheme = SettingsDefaults.ActiveTheme;
    private string _activeKeymap = SettingsDefaults.ActiveKeymap;
    private string _activeLanguage = SettingsDefaults.ActiveLanguage;
    private bool _loadUnsignedAddins = SettingsDefaults.LoadUnsignedAddins;
    private bool _reloadFilesChangedOutsideIde = SettingsDefaults.ReloadFilesChangedOutsideIde;

    private string? _revocationListUrl = SettingsDefaults.RevocationListUrl;

    // ── Window state default ────────────────────────────────────
    private WindowData _window = WindowData.Default;

    // ── Toolbar visibility defaults ─────────────────────────────
    private bool _isStandardToolbarVisible = SettingsDefaults.IsStandardToolbarVisible;
    private bool _isEditToolbarVisible = SettingsDefaults.IsEditToolbarVisible;
    private bool _isDebugToolbarVisible = SettingsDefaults.IsDebugToolbarVisible;
    private bool _isFormEditorToolbarVisible = SettingsDefaults.IsFormEditorToolbarVisible;

    // ── Editor extras defaults ──────────────────────────────────
    private bool _showIndentGuides = SettingsDefaults.ShowIndentGuides;
    private bool _isMinimapVisible = SettingsDefaults.IsMinimapVisible;
    private bool _colorBracketsByDepth = SettingsDefaults.ColorBracketsByDepth;
    private bool _isStickyScrollEnabled = SettingsDefaults.IsStickyScrollEnabled;
    private int _stickyScrollDepth = SettingsDefaults.StickyScrollDepth;

    // ── Editor properties ───────────────────────────────────────

    public bool RequireVariableDeclaration
    {
        get => _requireVariableDeclaration;
        set => SetProperty(ref _requireVariableDeclaration, value);
    }

    public bool AutoListMembers
    {
        get => _autoListMembers;
        set => SetProperty(ref _autoListMembers, value);
    }

    public bool AutoQuickInfo
    {
        get => _autoQuickInfo;
        set => SetProperty(ref _autoQuickInfo, value);
    }

    public bool AutoIndent
    {
        get => _autoIndent;
        set => SetProperty(ref _autoIndent, value);
    }

    public int TabWidth
    {
        get => _tabWidth;
        set => SetProperty(ref _tabWidth, Math.Clamp(value, 1, 32));
    }

    public bool FormatOnSave
    {
        get => _formatOnSave;
        set => SetProperty(ref _formatOnSave, value);
    }

    // ── General properties ──────────────────────────────────────

    public bool ShowGrid
    {
        get => _showGrid;
        set => SetProperty(ref _showGrid, value);
    }

    public int GridWidth
    {
        get => _gridWidth;
        set => SetProperty(ref _gridWidth, Math.Clamp(value, 1, 64));
    }

    public int GridHeight
    {
        get => _gridHeight;
        set => SetProperty(ref _gridHeight, Math.Clamp(value, 1, 64));
    }

    public bool AlignToGrid
    {
        get => _alignToGrid;
        set => SetProperty(ref _alignToGrid, value);
    }

    // ── Environment properties ──────────────────────────────────

    public bool PromptForProjectOnStartup
    {
        get => _promptForProjectOnStartup;
        set => SetProperty(ref _promptForProjectOnStartup, value);
    }

    public string ActiveTheme
    {
        get => _activeTheme;
        set => SetProperty(ref _activeTheme, value);
    }

    public string ActiveKeymap
    {
        get => _activeKeymap;
        set => SetProperty(ref _activeKeymap, value);
    }

    public string ActiveLanguage
    {
        get => _activeLanguage;
        set => SetProperty(ref _activeLanguage, value);
    }

    public bool LoadUnsignedAddins
    {
        get => _loadUnsignedAddins;
        set => SetProperty(ref _loadUnsignedAddins, value);
    }

    public bool ReloadFilesChangedOutsideIde
    {
        get => _reloadFilesChangedOutsideIde;
        set => SetProperty(ref _reloadFilesChangedOutsideIde, value);
    }

    public string? RevocationListUrl
    {
        get => _revocationListUrl;
        set => SetProperty(ref _revocationListUrl, value);
    }

    // ── Window state property ───────────────────────────────────

    public WindowData Window
    {
        get => _window;
        set { if (SetProperty(ref _window, value)) Save(); }
    }

    // ── Toolbar visibility properties ──────────────────────────

    public bool IsStandardToolbarVisible
    {
        get => _isStandardToolbarVisible;
        set { if (SetProperty(ref _isStandardToolbarVisible, value)) Save(); }
    }

    public bool IsEditToolbarVisible
    {
        get => _isEditToolbarVisible;
        set { if (SetProperty(ref _isEditToolbarVisible, value)) Save(); }
    }

    public bool IsDebugToolbarVisible
    {
        get => _isDebugToolbarVisible;
        set { if (SetProperty(ref _isDebugToolbarVisible, value)) Save(); }
    }

    public bool IsFormEditorToolbarVisible
    {
        get => _isFormEditorToolbarVisible;
        set { if (SetProperty(ref _isFormEditorToolbarVisible, value)) Save(); }
    }

    // ── Editor extras properties ────────────────────────────────

    public bool ShowIndentGuides
    {
        get => _showIndentGuides;
        set => SetProperty(ref _showIndentGuides, value);
    }

    public bool IsMinimapVisible
    {
        get => _isMinimapVisible;
        set { if (SetProperty(ref _isMinimapVisible, value)) Save(); }
    }

    public bool ColorBracketsByDepth
    {
        get => _colorBracketsByDepth;
        set { if (SetProperty(ref _colorBracketsByDepth, value)) Save(); }
    }

    public bool IsStickyScrollEnabled
    {
        get => _isStickyScrollEnabled;
        set => SetProperty(ref _isStickyScrollEnabled, value);
    }

    public int StickyScrollDepth
    {
        get => _stickyScrollDepth;
        set => SetProperty(ref _stickyScrollDepth, Math.Clamp(value, 1, 10));
    }

    // ── Constructor ─────────────────────────────────────────────

    public SettingsService() : this(DefaultFilePath()) { }

    public SettingsService(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    private static string DefaultFilePath()
    {
        var hexIdeDir = UserDataPath.Directory;
        Directory.CreateDirectory(hexIdeDir);
        return Path.Combine(hexIdeDir, "settings.json");
    }

    // ── Persistence ─────────────────────────────────────────────

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (!_loading && e.PropertyName is { } name)
            _changedProperties.Add(JsonNamingPolicy.CamelCase.ConvertName(name));
        base.OnPropertyChanged(e);
    }

    public void Save()
    {
        if (_loading || _changedProperties.Count == 0)
            return;

        try
        {
            var data = new SettingsData
            {
                RequireVariableDeclaration = _requireVariableDeclaration,
                AutoListMembers = _autoListMembers,
                AutoQuickInfo = _autoQuickInfo,
                AutoIndent = _autoIndent,
                TabWidth = _tabWidth,
                FormatOnSave = _formatOnSave,
                ShowGrid = _showGrid,
                GridWidth = _gridWidth,
                GridHeight = _gridHeight,
                AlignToGrid = _alignToGrid,
                PromptForProjectOnStartup = _promptForProjectOnStartup,
                ActiveTheme = _activeTheme,
                ActiveKeymap = _activeKeymap,
                ActiveLanguage = _activeLanguage,
                LoadUnsignedAddins = _loadUnsignedAddins,
                ReloadFilesChangedOutsideIde = _reloadFilesChangedOutsideIde,
                RevocationListUrl = _revocationListUrl,
                IsStandardToolbarVisible = _isStandardToolbarVisible,
                IsEditToolbarVisible = _isEditToolbarVisible,
                IsDebugToolbarVisible = _isDebugToolbarVisible,
                IsFormEditorToolbarVisible = _isFormEditorToolbarVisible,
                ShowIndentGuides = _showIndentGuides,
                IsMinimapVisible = _isMinimapVisible,
                ColorBracketsByDepth = _colorBracketsByDepth,
                IsStickyScrollEnabled = _isStickyScrollEnabled,
                StickyScrollDepth = _stickyScrollDepth,
                Window = new SettingsData.WindowDataJson
                {
                    State = _window.State,
                    X = _window.X,
                    Y = _window.Y,
                    Width = _window.Width,
                    Height = _window.Height
                }
            };
            // Preserve fields changed outside this instance, including settings from newer versions.
            var latest = File.Exists(_filePath)
                ? JsonNode.Parse(File.ReadAllText(_filePath))!.AsObject()
                : new JsonObject();
            var current = JsonSerializer.SerializeToNode(data)!.AsObject();
            // Only keys the file actually holds: a property that raises a change but isn't persisted must not
            // write a null over whatever the file says under that name.
            foreach (var name in _changedProperties)
                if (current.ContainsKey(name))
                    latest[name] = current[name]?.DeepClone();
            AtomicFileWriter.WriteAllText(_filePath, latest.ToJsonString(s_jsonOptions));
            _changedProperties.Clear();
            Log.Debug("SettingsService: Saved to {Path}", _filePath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SettingsService: Failed to save to {Path}", _filePath);
        }
    }

    public void Load()
    {
        _loading = true;
        try
        {
            if (!File.Exists(_filePath))
            {
                Log.Debug("SettingsService: No settings file found, using defaults");
                return;
            }

            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<SettingsData>(json);
            if (data == null)
            {
                Log.Warning("SettingsService: Deserialized null from {Path}, using defaults", _filePath);
                return;
            }

            RequireVariableDeclaration = data.RequireVariableDeclaration;
            AutoListMembers = data.AutoListMembers;
            AutoQuickInfo = data.AutoQuickInfo;
            AutoIndent = data.AutoIndent;
            TabWidth = data.TabWidth;
            FormatOnSave = data.FormatOnSave;
            ShowGrid = data.ShowGrid;
            GridWidth = data.GridWidth;
            GridHeight = data.GridHeight;
            AlignToGrid = data.AlignToGrid;
            PromptForProjectOnStartup = data.PromptForProjectOnStartup;
            ActiveTheme = data.ActiveTheme;
            ActiveKeymap = data.ActiveKeymap;
            ActiveLanguage = data.ActiveLanguage;
            LoadUnsignedAddins = data.LoadUnsignedAddins;
            ReloadFilesChangedOutsideIde = data.ReloadFilesChangedOutsideIde;
            RevocationListUrl = data.RevocationListUrl;
            _isStandardToolbarVisible = data.IsStandardToolbarVisible;
            _isEditToolbarVisible = data.IsEditToolbarVisible;
            _isDebugToolbarVisible = data.IsDebugToolbarVisible;
            _isFormEditorToolbarVisible = data.IsFormEditorToolbarVisible;
            ShowIndentGuides = data.ShowIndentGuides;
            _isMinimapVisible = data.IsMinimapVisible;
            _colorBracketsByDepth = data.ColorBracketsByDepth;
            IsStickyScrollEnabled = data.IsStickyScrollEnabled;
            StickyScrollDepth = data.StickyScrollDepth;
            if (data.Window is { } w)
                _window = new WindowData(w.State, w.X, w.Y, w.Width, w.Height);

            Log.Debug("SettingsService: Loaded from {Path}", _filePath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SettingsService: Failed to load from {Path}, using defaults", _filePath);
        }
        finally
        {
            _loading = false;
            _changedProperties.Clear();
        }
    }

    // ── JSON shape ──────────────────────────────────────────────

    private class SettingsData
    {
        [JsonPropertyName("requireVariableDeclaration")]
        public bool RequireVariableDeclaration { get; set; } = SettingsDefaults.RequireVariableDeclaration;

        [JsonPropertyName("autoListMembers")]
        public bool AutoListMembers { get; set; } = SettingsDefaults.AutoListMembers;

        [JsonPropertyName("autoQuickInfo")]
        public bool AutoQuickInfo { get; set; } = SettingsDefaults.AutoQuickInfo;

        [JsonPropertyName("autoIndent")]
        public bool AutoIndent { get; set; } = SettingsDefaults.AutoIndent;

        [JsonPropertyName("tabWidth")]
        public int TabWidth { get; set; } = SettingsDefaults.TabWidth;

        [JsonPropertyName("formatOnSave")]
        public bool FormatOnSave { get; set; } = SettingsDefaults.FormatOnSave;

        [JsonPropertyName("showGrid")]
        public bool ShowGrid { get; set; } = SettingsDefaults.ShowGrid;

        [JsonPropertyName("gridWidth")]
        public int GridWidth { get; set; } = SettingsDefaults.GridWidth;

        [JsonPropertyName("gridHeight")]
        public int GridHeight { get; set; } = SettingsDefaults.GridHeight;

        [JsonPropertyName("alignToGrid")]
        public bool AlignToGrid { get; set; } = SettingsDefaults.AlignToGrid;

        [JsonPropertyName("promptForProjectOnStartup")]
        public bool PromptForProjectOnStartup { get; set; } = SettingsDefaults.PromptForProjectOnStartup;

        [JsonPropertyName("activeTheme")]
        public string ActiveTheme { get; set; } = SettingsDefaults.ActiveTheme;

        [JsonPropertyName("activeKeymap")]
        public string ActiveKeymap { get; set; } = SettingsDefaults.ActiveKeymap;

        [JsonPropertyName("activeLanguage")]
        public string ActiveLanguage { get; set; } = SettingsDefaults.ActiveLanguage;

        [JsonPropertyName("loadUnsignedAddins")]
        public bool LoadUnsignedAddins { get; set; } = SettingsDefaults.LoadUnsignedAddins;

        [JsonPropertyName("reloadFilesChangedOutsideIde")]
        public bool ReloadFilesChangedOutsideIde { get; set; } = SettingsDefaults.ReloadFilesChangedOutsideIde;

        [JsonPropertyName("revocationListUrl")]
        public string? RevocationListUrl { get; set; } = SettingsDefaults.RevocationListUrl;

        [JsonPropertyName("isStandardToolbarVisible")]
        public bool IsStandardToolbarVisible { get; set; } = SettingsDefaults.IsStandardToolbarVisible;

        [JsonPropertyName("isEditToolbarVisible")]
        public bool IsEditToolbarVisible { get; set; } = SettingsDefaults.IsEditToolbarVisible;

        [JsonPropertyName("isDebugToolbarVisible")]
        public bool IsDebugToolbarVisible { get; set; } = SettingsDefaults.IsDebugToolbarVisible;

        [JsonPropertyName("isFormEditorToolbarVisible")]
        public bool IsFormEditorToolbarVisible { get; set; } = SettingsDefaults.IsFormEditorToolbarVisible;

        [JsonPropertyName("showIndentGuides")]
        public bool ShowIndentGuides { get; set; } = SettingsDefaults.ShowIndentGuides;

        [JsonPropertyName("isMinimapVisible")]
        public bool IsMinimapVisible { get; set; } = SettingsDefaults.IsMinimapVisible;

        [JsonPropertyName("colorBracketsByDepth")]
        public bool ColorBracketsByDepth { get; set; } = SettingsDefaults.ColorBracketsByDepth;
        [JsonPropertyName("isStickyScrollEnabled")]
        public bool IsStickyScrollEnabled { get; set; } = SettingsDefaults.IsStickyScrollEnabled;

        [JsonPropertyName("stickyScrollDepth")]
        public int StickyScrollDepth { get; set; } = SettingsDefaults.StickyScrollDepth;

        [JsonPropertyName("window")]
        public WindowDataJson? Window { get; set; }

        public sealed class WindowDataJson
        {
            [JsonPropertyName("state")]  public string State  { get; set; } = "Maximized";
            [JsonPropertyName("x")]      public int    X      { get; set; } = 100;
            [JsonPropertyName("y")]      public int    Y      { get; set; } = 100;
            [JsonPropertyName("width")]  public int    Width  { get; set; } = 1280;
            [JsonPropertyName("height")] public int    Height { get; set; } = 800;
        }
    }
}
