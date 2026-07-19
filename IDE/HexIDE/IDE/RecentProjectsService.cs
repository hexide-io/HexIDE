using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace HexIDE.IDE;

/// <summary>
/// Persists recently opened project paths to a JSON file in the user's AppData directory.
/// </summary>
public class RecentProjectsService : IRecentProjectsService
{
    public event Action? Changed;

    private readonly string _filePath;
    private readonly List<string> _recent = new();
    private bool _suppressDialog;
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    public int MaxEntries { get; } = 10;

    public bool SuppressNewProjectDialog
    {
        get => _suppressDialog;
        set
        {
            if (_suppressDialog == value) return;
            _suppressDialog = value;
            Save();
        }
    }

    public RecentProjectsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var hexIdeDir = Path.Combine(appData, "HexIDE");
        Directory.CreateDirectory(hexIdeDir);
        _filePath = Path.Combine(hexIdeDir, "recent-projects.json");
        Load();
    }

    public void Add(string absolutePath)
    {
        var normalized = Path.GetFullPath(absolutePath);
        _recent.RemoveAll(p => string.Equals(p, normalized, StringComparison.OrdinalIgnoreCase));
        _recent.Insert(0, normalized);

        while (_recent.Count > MaxEntries)
            _recent.RemoveAt(_recent.Count - 1);

        Save();
        Changed?.Invoke();
    }

    public void Remove(string absolutePath)
    {
        var normalized = Path.GetFullPath(absolutePath);
        _recent.RemoveAll(p => string.Equals(p, normalized, StringComparison.OrdinalIgnoreCase));
        Save();
        Changed?.Invoke();
    }

    public IReadOnlyList<string> GetRecent() => _recent.AsReadOnly();

    public void Clear()
    {
        _recent.Clear();
        Save();
        Changed?.Invoke();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return;

            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<SettingsData>(json);
            if (data != null)
            {
                _recent.Clear();
                if (data.RecentProjects != null)
                    _recent.AddRange(data.RecentProjects.Take(MaxEntries));
                _suppressDialog = data.SuppressNewProjectDialog;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load recent projects from {Path}", _filePath);
        }
    }

    private void Save()
    {
        try
        {
            var data = new SettingsData
            {
                RecentProjects = _recent,
                SuppressNewProjectDialog = _suppressDialog
            };
            var json = JsonSerializer.Serialize(data, s_jsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to save recent projects to {Path}", _filePath);
        }
    }

    private class SettingsData
    {
        [JsonPropertyName("recentProjects")]
        public List<string>? RecentProjects { get; set; }

        [JsonPropertyName("suppressNewProjectDialog")]
        public bool SuppressNewProjectDialog { get; set; }
    }
}
