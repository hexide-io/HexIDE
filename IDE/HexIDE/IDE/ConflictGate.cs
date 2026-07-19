using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using HexIDE.Forms.ViewModels;
using Serilog;

namespace HexIDE.IDE;

/// <summary>
/// Surfaces externally-changed files that have unsaved edits via a single consolidated conflict dialog,
/// deferred until the HexIDE window is active (so the user is never yanked out of another app — VS2026
/// behaviour). Conflicts that arrive while the window is backgrounded (or while a dialog is already
/// showing) are queued and coalesced by path; the latest disk state for a path wins.
/// <para>All members run on the UI thread.</para>
/// </summary>
public sealed class ConflictGate
{
    private readonly IWindowManager windowManager;
    private readonly Func<IReadOnlyList<WatchedFileTarget>, Task> onReloadAll;
    private readonly Func<IReadOnlyList<WatchedFileTarget>, Task> onKeepAll;

    private readonly Dictionary<string, WatchedFileTarget> pending = new(StringComparer.OrdinalIgnoreCase);
    private bool showing;
    private bool subscribedToActivated;

    public ConflictGate(
        IWindowManager windowManager,
        Func<IReadOnlyList<WatchedFileTarget>, Task> onReloadAll,
        Func<IReadOnlyList<WatchedFileTarget>, Task> onKeepAll)
    {
        this.windowManager = windowManager;
        this.onReloadAll = onReloadAll;
        this.onKeepAll = onKeepAll;
    }

    /// <summary>Queues conflicted targets and shows the dialog if the window is active and none is open.</summary>
    public void Enqueue(IReadOnlyList<WatchedFileTarget> conflicts)
    {
        foreach (var c in conflicts)
            pending[c.SourcePath] = c; // coalesce by path; newest target wins
        TryShow();
    }

    private void TryShow()
    {
        if (showing || pending.Count == 0)
            return;

        if (!MainWindowActive())
        {
            EnsureActivatedHook();
            Log.Debug("FileWatcher: {Count} conflict(s) deferred until the window is active", pending.Count);
            return;
        }

        showing = true;
        var batch = pending.Values.ToList();
        pending.Clear();
        Log.Information("FileWatcher: showing conflict dialog for {Count} file(s)", batch.Count);
        _ = ShowAndHandleAsync(batch);
    }

    private async Task ShowAndHandleAsync(List<WatchedFileTarget> batch)
    {
        try
        {
            var vm = new FileConflictViewModel();
            foreach (var t in batch)
                vm.Add(DisplayName(t));

            await windowManager.ShowDialog(vm);

            Log.Information("FileWatcher: conflict resolved — {Choice} for {Count} file(s)",
                vm.ReloadChosen ? "reload-all" : "keep-all", batch.Count);

            if (vm.ReloadChosen)
                await onReloadAll(batch);
            else
                await onKeepAll(batch);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "FileWatcher: conflict dialog handling failed");
        }
        finally
        {
            showing = false;
            TryShow(); // flush anything queued while the dialog was open
        }
    }

    private static string DisplayName(WatchedFileTarget t) =>
        t.Form?.Name ?? t.Module?.Name ?? Path.GetFileName(t.SourcePath);

    private static Window? MainWindow() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    // No desktop window (single-view / headless) → treat as active so the dialog shows immediately.
    // A minimized window counts as inactive even though Avalonia keeps IsActive == true for it.
    private static bool MainWindowActive()
    {
        if (MainWindow() is not { } w)
            return true;
        return w.WindowState != WindowState.Minimized && w.IsActive;
    }

    private void EnsureActivatedHook()
    {
        if (subscribedToActivated)
            return;
        var window = MainWindow();
        if (window is null)
            return;
        window.Activated += OnMainWindowActivated;
        subscribedToActivated = true;
    }

    private void OnMainWindowActivated(object? sender, EventArgs e) => TryShow();
}
