using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Serilog;

namespace HexIDE.IDE;

public sealed class WindowStateService : IWindowStateService
{
    private readonly ISettingsService _settings;
    private readonly string _dockLayoutPath;
    private readonly string _namedLayoutsPath;
    private readonly object _writeLock = new();
    private readonly bool _recoveryMode = Static.IsRecoveryMode;
    private string? _sessionLayoutJson;
    private WindowData? _normalBounds;

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
            AtomicFileWriter.WriteRecoverableText(_namedLayoutsPath, JsonSerializer.Serialize(entries));
    }

    public void TrackWindowState(Window window)
    {
        if (_normalBounds is null)
            CaptureNormalBounds(window);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            SaveWindowState(window);
        };
        void ScheduleSave()
        {
            timer.Stop();
            timer.Start();
        }
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.WindowStateProperty)
                ScheduleSave();
        };
        // Native move/resize events can arrive before WindowState changes to Maximized.
        // Sample normal bounds only when the debounced save runs, after the transition settles.
        window.PositionChanged += (_, _) => ScheduleSave();
        window.Resized += (_, _) => ScheduleSave();
        window.Closed += (_, _) => timer.Stop();
    }

    private void CaptureNormalBounds(Window window)
    {
        if (window.WindowState == WindowState.Normal)
            _normalBounds = new WindowData("Normal", window.Position.X, window.Position.Y,
                (int)window.ClientSize.Width, (int)window.ClientSize.Height);
    }

    public void SaveWindowState(Window mainWindow)
    {
        CaptureNormalBounds(mainWindow);
        var state = mainWindow.WindowState switch
        {
            WindowState.Maximized => "Maximized",
            WindowState.Minimized => "Minimized",
            _ => "Normal"
        };
        // Non-normal client bounds describe the screen (or a minimized window), not its restore size.
        _settings.Window = (_normalBounds ?? _settings.Window) with { State = state };
    }

    public void RestoreWindowState(Window mainWindow)
    {
        var position = new PixelPoint(_settings.Window.X, _settings.Window.Y);
        var screen = mainWindow.Screens.ScreenFromPoint(position) ?? mainWindow.Screens.Primary;
        RestoreWindowState(mainWindow, screen?.WorkingArea, screen?.Scaling ?? 1,
            WindowFrameMeasurement.Measure(mainWindow));
    }

    internal void RestoreWindowState(Window mainWindow, PixelRect? workingArea, double scaling,
        (Size Outer, PixelRect OuterBounds, PixelRect DrawnBounds)? measuredFrame = null)
    {
        var w = _settings.Window;
        mainWindow.WindowState = WindowState.Normal;
        var position = new PixelPoint(w.X, w.Y);
        var width = (double)w.Width;
        var height = (double)w.Height;
        if (workingArea is { } area)
        {
            if (mainWindow.Screens.ScreenFromPoint(position) is null)
                position = area.Position;
            // Screen coordinates are physical pixels; window sizes (including the frame) are DIP.
            var frame = measuredFrame?.Outer ?? mainWindow.FrameSize ?? mainWindow.ClientSize;
            var leftBorder = 0;
            var topBorder = 0;
            if (measuredFrame is { } measured)
            {
                leftBorder = measured.DrawnBounds.X - measured.OuterBounds.X;
                topBorder = measured.DrawnBounds.Y - measured.OuterBounds.Y;
                frame = new Size(
                    frame.Width - (measured.OuterBounds.Width - measured.DrawnBounds.Width) / scaling,
                    frame.Height - (measured.OuterBounds.Height - measured.DrawnBounds.Height) / scaling);
            }
            var frameWidth = Math.Max(0, frame.Width - mainWindow.ClientSize.Width);
            var frameHeight = Math.Max(0, frame.Height - mainWindow.ClientSize.Height);
            width = Math.Max(mainWindow.MinWidth,
                Math.Min(width, Math.Floor(area.Width / scaling - frameWidth)));
            height = Math.Max(mainWindow.MinHeight,
                Math.Min(height, Math.Floor(area.Height / scaling - frameHeight)));
            var right = area.Right - (int)Math.Ceiling((width + frameWidth) * scaling) - leftBorder;
            var bottom = area.Bottom - (int)Math.Ceiling((height + frameHeight) * scaling) - topBorder;
            var left = width + frameWidth > area.Width / scaling ? area.X : area.X - leftBorder;
            var top = height + frameHeight > area.Height / scaling ? area.Y : area.Y - topBorder;
            position = new PixelPoint(
                Math.Clamp(position.X, left, Math.Max(left, right)),
                Math.Clamp(position.Y, top, Math.Max(top, bottom)));
        }
        mainWindow.Position = position;
        mainWindow.Width = width;
        mainWindow.Height = height;
        _normalBounds = new WindowData("Normal", position.X, position.Y, (int)width, (int)height);
        if (w.State == "Maximized")
        {
            // Width/Height reach the platform during layout. Apply them while Normal so the
            // native restore rectangle has the saved size before the maximize request.
            mainWindow.UpdateLayout();
            // Some platforms acknowledge the resize asynchronously (headless models X11).
            Dispatcher.UIThread.Post(() =>
            {
                if (mainWindow.IsVisible && mainWindow.WindowState == WindowState.Normal)
                    mainWindow.WindowState = WindowState.Maximized;
            }, DispatcherPriority.Background);
        }
        // Minimized: open as Normal — don't launch minimized
    }

    public void SaveLayoutManifest(LayoutManifest manifest) =>
        SaveLayoutManifestJson(LayoutManifestJson.Serialize(manifest));

    public void SaveLayoutManifestJson(string json)
    {
        if (_recoveryMode)
        {
            lock (_writeLock)
                _sessionLayoutJson = json;
            return;
        }
        try
        {
            // Lock so a background (debounced) write and the synchronous on-close write never collide.
            lock (_writeLock)
                AtomicFileWriter.WriteRecoverableText(_dockLayoutPath, json);
            Log.Debug("WindowStateService: Saved layout manifest to {Path}", _dockLayoutPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "WindowStateService: Failed to save layout manifest to {Path}", _dockLayoutPath);
        }
    }

    public LayoutManifest? LoadLayoutManifest()
    {
        if (_recoveryMode)
        {
            lock (_writeLock)
                return _sessionLayoutJson is { } json ? LayoutManifestJson.Deserialize(json) : null;
        }
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
        if (_recoveryMode)
        {
            lock (_writeLock)
                _sessionLayoutJson = null;
            return;
        }
        try
        {
            if (File.Exists(_dockLayoutPath))
                AtomicFileWriter.DeleteRecoverable(_dockLayoutPath);
            Log.Debug("WindowStateService: Reset (deleted) layout manifest at {Path}", _dockLayoutPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "WindowStateService: Failed to reset layout manifest at {Path}", _dockLayoutPath);
        }
    }
}
