using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Serilog;

namespace HexIDE.IDE;

public sealed class WindowStateService : IWindowStateService
{
    private readonly ISettingsService _settings;
    private readonly string _dockLayoutPath;
    private readonly string _namedLayoutsPath;
    private readonly object _writeLock = new();

    public WindowStateService(ISettingsService settings) : this(settings, UserDataPath.Directory) { }

    internal static WindowStateService ForDirectory(ISettingsService settings, string directory) => new(settings, directory);

    private WindowStateService(ISettingsService settings, string directory)
    {
        _settings = settings;
        _dockLayoutPath = Path.Combine(directory, "dock-layout.json");
        _namedLayoutsPath = Path.Combine(directory, "named-layouts.json");
    }

    public IReadOnlyDictionary<string, LayoutManifest> LoadNamedLayouts()
    {
        try
        {
            var path = _namedLayoutsPath;
            if (!File.Exists(path)) return new Dictionary<string, LayoutManifest>();
            var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            var layouts = new Dictionary<string, LayoutManifest>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries ?? [])
                if (!string.IsNullOrWhiteSpace(entry.Key) && LayoutManifestJson.Deserialize(entry.Value) is { } manifest)
                    layouts.TryAdd(entry.Key, manifest);
            return layouts;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "WindowStateService: Failed to load named layouts");
            return new Dictionary<string, LayoutManifest>();
        }
    }

    public void SaveNamedLayouts(IReadOnlyDictionary<string, LayoutManifest> layouts)
    {
        var entries = layouts.ToDictionary(p => p.Key, p => LayoutManifestJson.Serialize(p.Value));
        lock (_writeLock)
            AtomicFileWriter.WriteAllText(_namedLayoutsPath, JsonSerializer.Serialize(entries));
    }

    public void SaveWindowState(Window mainWindow)
    {
        var state = mainWindow.WindowState switch
        {
            WindowState.Maximized => "Maximized",
            WindowState.Minimized => "Minimized",
            _ => "Normal"
        };
        var pos  = mainWindow.Position;
        var size = mainWindow.ClientSize;
        _settings.Window = new WindowData(state, pos.X, pos.Y, (int)size.Width, (int)size.Height);
    }

    public void RestoreWindowState(Window mainWindow)
    {
        var w = _settings.Window;
        if (w.State == "Maximized")
        {
            mainWindow.WindowState = WindowState.Maximized;
        }
        else if (w.State == "Normal")
        {
            mainWindow.WindowState = WindowState.Normal;
            mainWindow.Position    = new PixelPoint(w.X, w.Y);
            mainWindow.Width       = w.Width;
            mainWindow.Height      = w.Height;
        }
        // Minimized: open as Normal — don't launch minimized
    }

    public void SaveLayoutManifest(LayoutManifest manifest) =>
        SaveLayoutManifestJson(LayoutManifestJson.Serialize(manifest));

    public void SaveLayoutManifestJson(string json)
    {
        try
        {
            // Lock so a background (debounced) write and the synchronous on-close write never collide.
            lock (_writeLock)
                AtomicFileWriter.WriteAllText(_dockLayoutPath, json);
            Log.Debug("WindowStateService: Saved layout manifest to {Path}", _dockLayoutPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "WindowStateService: Failed to save layout manifest to {Path}", _dockLayoutPath);
        }
    }

    public LayoutManifest? LoadLayoutManifest()
    {
        if (!File.Exists(_dockLayoutPath))
            return null;
        try
        {
            var manifest = LayoutManifestJson.Deserialize(File.ReadAllText(_dockLayoutPath));
            if (manifest is null)
                Log.Warning("WindowStateService: dock-layout.json at {Path} was unreadable; using defaults", _dockLayoutPath);
            return manifest;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "WindowStateService: Failed to load layout manifest from {Path}, using defaults", _dockLayoutPath);
            return null;
        }
    }

    public void ResetLayoutManifest()
    {
        try
        {
            if (File.Exists(_dockLayoutPath))
                File.Delete(_dockLayoutPath);
            Log.Debug("WindowStateService: Reset (deleted) layout manifest at {Path}", _dockLayoutPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "WindowStateService: Failed to reset layout manifest at {Path}", _dockLayoutPath);
        }
    }
}
