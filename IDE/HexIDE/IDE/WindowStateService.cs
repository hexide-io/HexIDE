using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Serilog;

namespace HexIDE.IDE;

public sealed class WindowStateService : IWindowStateService
{
    private readonly ISettingsService _settings;
    private readonly string _dockLayoutPath;
    private readonly object _writeLock = new();

    public WindowStateService(ISettingsService settings)
    {
        _settings = settings;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _dockLayoutPath = Path.Combine(appData, "HexIDE", "dock-layout.json");
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
