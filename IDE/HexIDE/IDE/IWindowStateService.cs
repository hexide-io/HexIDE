using System.Collections.Generic;
using Avalonia.Controls;

namespace HexIDE.IDE;

/// <summary>The four canonical dock regions a built-in tool window can occupy. Floating and pinning
/// are disabled, so every arrangement resolves to one of these. See
/// openspec/specs/ide-state-persistence/spec.md — the tool-window arrangement requirement.</summary>
public enum DockRegion
{
    Left,
    Right,
    Bottom,
    Document,
}

/// <summary>One tool window's persisted placement. <see cref="Proportion"/> is the tool's share within
/// its region's vertical stack (right/bottom); <c>null</c> lets Dock distribute evenly. Left and document
/// tools ignore it.</summary>
public sealed record ToolLayoutState(string Key, bool Open, DockRegion Region, int Order, double? Proportion);

/// <summary>The IDE dock layout, captured as a flat per-tool manifest (the canonical-region model).
/// A present <c>dock-layout.json</c> overrides <see cref="Default"/>; Reset Window Layout restores it.</summary>
public sealed record LayoutManifest(
    int Version,
    double LeftProportion,
    double RightProportion,
    IReadOnlyList<ToolLayoutState> Tools)
{
    public const int CurrentVersion = 2;
    public const double DefaultLeftProportion = 0.06;
    public const double DefaultRightProportion = 0.2155;

    /// <summary>The factory default layout, expressed as data — the single source of every tool's "home"
    /// region/order and the default open-state. Used for first run, corrupt-file fallback, Reset, and as the
    /// home lookup for restoring a closed-then-reopened tool to its slot.</summary>
    public static LayoutManifest Default { get; } = new(
        CurrentVersion, DefaultLeftProportion, DefaultRightProportion, new ToolLayoutState[]
        {
            new("toolbox",           true,  DockRegion.Left,     0, null),
            new("project",           true,  DockRegion.Right,    0, null),
            new("properties",        true,  DockRegion.Right,    1, null),
            new("formLayout",        true,  DockRegion.Right,    2, null),
            new("immediate",         false, DockRegion.Bottom,   0, 0.3),
            new("locals",            false, DockRegion.Bottom,   1, 0.3),
            new("watches",           false, DockRegion.Bottom,   2, 0.3),
            new("callStack",         false, DockRegion.Bottom,   3, 0.3),
            new("colorPalette",      false, DockRegion.Bottom,   4, 0.3),
            new("objectBrowser",     false, DockRegion.Document, 0, null),
            new("translationEditor", false, DockRegion.Document, 1, null),
        });
}

public interface IWindowStateService
{
    void SaveWindowState(Window mainWindow);
    void RestoreWindowState(Window mainWindow);

    /// <summary>Persist the dock layout manifest to <c>dock-layout.json</c> (atomically).</summary>
    void SaveLayoutManifest(LayoutManifest manifest);

    /// <summary>Persist a pre-serialized manifest atomically — lets the eager save dedupe on the JSON it
    /// already computed and hand the write to a background thread.</summary>
    void SaveLayoutManifestJson(string json);

    /// <summary>Load the persisted manifest, migrating the legacy v1 (two-proportion) file. Returns
    /// <c>null</c> when no file exists or it is unreadable — the caller falls back to
    /// <see cref="LayoutManifest.Default"/>.</summary>
    LayoutManifest? LoadLayoutManifest();

    /// <summary>Delete the persisted manifest so the next layout build uses defaults (backs the
    /// Reset Window Layout command).</summary>
    void ResetLayoutManifest();
}
