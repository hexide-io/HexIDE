using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using HexIDE.Forms.ViewModels;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.VisualDesigner;
using Serilog;

namespace HexIDE.IDE;

/// <summary>
/// Watches the directories of loaded projects for external changes to component files
/// (<c>.frm/.ctl/.pag/.bas/.cls</c> and their binary companions) and reloads them. Phase 1: clean files
/// reload silently; files with unsaved edits are skipped (the conflict dialog arrives in Phase 2).
/// <para>
/// FileSystemWatcher callbacks (background threads) only feed the <see cref="ChangeCoalescer"/>. A polling
/// timer drains coalesced batches, reads/compares against the <see cref="IFileBaselineStore"/> to drop the
/// IDE's own writes and mtime-only touches, then marshals the model/view refresh to the UI thread.
/// </para>
/// </summary>
public sealed class FileWatcherService : IFileWatcherService
{
    private const int PollIntervalMs = 100;

    private static readonly HashSet<string> SourceExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".frm", ".ctl", ".pag", ".bas", ".cls" };
    private static readonly HashSet<string> CompanionExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".frx", ".ctx", ".pgx" };

    private readonly IProjectManager projectManager;
    private readonly IDocumentDockService dock;
    private readonly IProjectRunnerService projectRunner;
    private readonly ISettingsService settings;
    private readonly IFileBaselineStore baselineStore;

    private readonly ChangeCoalescer coalescer;
    private readonly DirtyDetector dirtyDetector;
    private readonly FileReloader reloader;
    private readonly ConflictGate conflictGate;

    private readonly object watchersLock = new();
    private readonly Dictionary<string, (FileSystemWatcher Watcher, int RefCount)> watchers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Timers.Timer timer;
    private volatile bool disposed;

    public FileWatcherService(
        IProjectManager projectManager,
        IDocumentDockService dock,
        IProjectRunnerService projectRunner,
        ISettingsService settings,
        IFileBaselineStore baselineStore,
        IProjectService projectService,
        IEventBus eventBus,
        IWindowManager windowManager,
        IStatusBarService statusBar,
        ILocalizationService localization,
        IClock clock)
    {
        this.projectManager = projectManager;
        this.dock = dock;
        this.projectRunner = projectRunner;
        this.settings = settings;
        this.baselineStore = baselineStore;

        coalescer = new ChangeCoalescer(clock);
        dirtyDetector = new DirtyDetector(baselineStore, projectService);
        reloader = new FileReloader(projectService, eventBus, statusBar, localization);
        conflictGate = new ConflictGate(windowManager, ReloadConflictsAsync, KeepConflictsAsync);

        projectManager.ProjectLoaded += OnProjectLoaded;
        projectManager.ProjectUnloaded += OnProjectUnloaded;
        settings.PropertyChanged += OnSettingsChanged;

        timer = new System.Timers.Timer(PollIntervalMs) { AutoReset = false };
        timer.Elapsed += OnTimerElapsed;

        if (settings.ReloadFilesChangedOutsideIde)
            AttachAll();

        timer.Start();
    }

    // ── Watcher attach / detach ─────────────────────────────────

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ISettingsService.ReloadFilesChangedOutsideIde))
            return;
        if (settings.ReloadFilesChangedOutsideIde)
            AttachAll();
        else
            DetachAll();
    }

    private void OnProjectLoaded(ProjectDefinition project)
    {
        if (settings.ReloadFilesChangedOutsideIde)
            AttachProject(project);
    }

    private void OnProjectUnloaded(ProjectDefinition project)
    {
        var dir = DirectoryOf(project);
        if (dir is null)
            return;
        lock (watchersLock)
        {
            if (!watchers.TryGetValue(dir, out var entry))
                return;
            if (entry.RefCount <= 1)
            {
                DisposeWatcher(entry.Watcher);
                watchers.Remove(dir);
            }
            else
            {
                watchers[dir] = (entry.Watcher, entry.RefCount - 1);
            }
        }
    }

    private void AttachAll()
    {
        foreach (var project in projectManager.LoadedProjects)
            AttachProject(project);
    }

    private void AttachProject(ProjectDefinition project)
    {
        var dir = DirectoryOf(project);
        if (dir is null || !Directory.Exists(dir))
            return;
        lock (watchersLock)
        {
            if (watchers.TryGetValue(dir, out var entry))
            {
                watchers[dir] = (entry.Watcher, entry.RefCount + 1);
                return;
            }
            try
            {
                watchers[dir] = (CreateWatcher(dir), 1);
                Log.Information("FileWatcher: watching {Dir}", dir);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "FileWatcher: failed to watch {Dir}", dir);
            }
        }
    }

    private void DetachAll()
    {
        lock (watchersLock)
        {
            foreach (var (watcher, _) in watchers.Values)
                DisposeWatcher(watcher);
            watchers.Clear();
        }
    }

    private FileSystemWatcher CreateWatcher(string dir)
    {
        var watcher = new FileSystemWatcher(dir)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        watcher.Changed += OnFsChanged;
        watcher.Created += OnFsChanged;
        watcher.Renamed += OnFsRenamed;
        watcher.Deleted += OnFsDeleted;
        watcher.Error += OnFsError;
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private static void DisposeWatcher(FileSystemWatcher watcher)
    {
        try
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        catch { /* best-effort */ }
    }

    private static string? DirectoryOf(ProjectDefinition project)
    {
        var path = project.AbsolutePath;
        if (string.IsNullOrEmpty(path))
            return null;
        try { return Path.GetDirectoryName(Path.GetFullPath(path)); }
        catch { return null; }
    }

    // ── FileSystemWatcher callbacks (background threads) ─────────

    private void OnFsChanged(object? sender, FileSystemEventArgs e)
    {
        if (IsWatchedFile(e.FullPath))
            coalescer.Notify(e.FullPath);
    }

    private void OnFsRenamed(object? sender, RenamedEventArgs e)
    {
        // An atomic write (tmp → target) surfaces here; the old (.tmp) name has no baseline.
        baselineStore.Remove(e.OldFullPath);
        if (IsWatchedFile(e.FullPath))
            coalescer.Notify(e.FullPath);
    }

    private void OnFsDeleted(object? sender, FileSystemEventArgs e)
    {
        // Do NOT drop the baseline here: an atomic write (File.Move with replace) can surface as a
        // transient Deleted of the target immediately followed by its re-creation, and eagerly dropping
        // the baseline would defeat self-write suppression. Route it through the coalescer — the batch
        // processor drops the baseline only if the file is still gone once the burst settles.
        if (IsWatchedFile(e.FullPath))
            coalescer.Notify(e.FullPath);
    }

    private void OnFsError(object? sender, ErrorEventArgs e) =>
        Log.Warning(e.GetException(), "FileWatcher: watcher error");

    private static bool IsWatchedFile(string path)
    {
        var ext = Path.GetExtension(path);
        return SourceExtensions.Contains(ext) || CompanionExtensions.Contains(ext);
    }

    // ── Polling / batch processing ──────────────────────────────

    private void OnTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        try
        {
            if (disposed || !settings.ReloadFilesChangedOutsideIde)
                return;
            // Don't reload code/forms underneath a running interpreter — leave the batch pending; it
            // drains once the run ends.
            if (projectRunner.IsRunning)
                return;
            if (coalescer.TryDrain(out var batch))
                ProcessBatch(batch);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "FileWatcher: error draining change batch");
        }
        finally
        {
            if (!disposed)
            {
                try { timer.Start(); }
                catch (ObjectDisposedException) { /* racing dispose */ }
            }
        }
    }

    private void ProcessBatch(IReadOnlyCollection<string> batch)
    {
        var changedOwners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in batch)
        {
            if (!TryReadAllBytes(path, out var bytes))
            {
                if (!File.Exists(path))
                    baselineStore.Remove(path);
                continue;
            }
            if (baselineStore.Matches(path, bytes))
                continue; // the IDE's own write, or an mtime-only touch
            changedOwners.Add(ToOwnerSourcePath(path));
        }

        if (changedOwners.Count == 0)
            return;

        Dispatcher.UIThread.Post(async () =>
        {
            try { await ProcessChangedOwnersAsync(changedOwners); }
            catch (Exception ex) { Log.Warning(ex, "FileWatcher: UI batch processing failed"); }
        });
    }

    private async Task ProcessChangedOwnersAsync(HashSet<string> owners)
    {
        var conflicts = new List<WatchedFileTarget>();
        foreach (var owner in owners)
        {
            if (disposed)
                return;
            try
            {
                var target = LocateTarget(owner);
                if (target is null)
                    continue;

                switch (dirtyDetector.Classify(target))
                {
                    case ReloadDecision.CleanReload:
                        await reloader.ReloadAsync(target);
                        break;
                    case ReloadDecision.Conflict:
                        conflicts.Add(target);
                        break;
                    default:
                        Log.Debug("FileWatcher: skipping {Path} (Indeterminate)", owner);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "FileWatcher: failed to reload {Path}", owner);
            }
        }

        if (conflicts.Count > 0)
            conflictGate.Enqueue(conflicts);
    }

    // ── Conflict resolution (invoked by the ConflictGate after the user chooses) ─────────

    private async Task ReloadConflictsAsync(IReadOnlyList<WatchedFileTarget> targets)
    {
        foreach (var t in targets)
        {
            try
            {
                await reloader.ReloadAsync(t); // discard edits, re-read disk, refresh views, update baseline
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "FileWatcher: reload-all failed for {Path}", t.SourcePath);
            }
        }
    }

    private Task KeepConflictsAsync(IReadOnlyList<WatchedFileTarget> targets)
    {
        // Keep the in-memory edits; adopt the new disk content as the baseline so this same change won't
        // re-prompt (a later, distinct external change will).
        foreach (var t in targets)
        {
            RecordBaselineFromDisk(t.SourcePath);
            if (CompanionOf(t.SourcePath) is { } companion && File.Exists(companion))
                RecordBaselineFromDisk(companion);
        }
        return Task.CompletedTask;
    }

    private void RecordBaselineFromDisk(string path)
    {
        if (TryReadAllBytes(path, out var bytes))
            baselineStore.Record(path, bytes);
    }

    private static string? CompanionOf(string sourcePath) =>
        Path.GetExtension(sourcePath).ToLowerInvariant() switch
        {
            ".frm" => Path.ChangeExtension(sourcePath, ".frx"),
            ".ctl" => Path.ChangeExtension(sourcePath, ".ctx"),
            ".pag" => Path.ChangeExtension(sourcePath, ".pgx"),
            _ => null,
        };

    private WatchedFileTarget? LocateTarget(string sourcePath)
    {
        FormDefinition? form = null;
        ModuleDefinition? module = null;
        foreach (var project in projectManager.LoadedProjects)
        {
            foreach (var f in project.Forms)
            {
                if (PathsEqual(f.AbsolutePath, sourcePath)) { form = f; break; }
            }
            if (form is not null)
                break;
            foreach (var m in project.Modules)
            {
                if (PathsEqual(m.AbsolutePath, sourcePath)) { module = m; break; }
            }
            if (module is not null)
                break;
        }
        if (form is null && module is null)
            return null;

        var codeEditor = dock.OpenDocuments.OfType<CodeEditorViewModel>().FirstOrDefault(vm =>
            (form is not null && ReferenceEquals(vm.FormDefinition, form)) ||
            (module is not null && ReferenceEquals(vm.ModuleDefinition, module)));

        // A plain form's designer binds the form itself; a UserControl/PropertyPage designer binds the
        // module's FormPart. Match either so the designer is refreshed in both cases.
        var designer = dock.OpenDocuments.OfType<FormEditViewModel>().FirstOrDefault(vm =>
            (form is not null && ReferenceEquals(vm.FormDefinition, form)) ||
            (module?.FormPart is not null && ReferenceEquals(vm.FormDefinition, module.FormPart)));

        return new WatchedFileTarget(sourcePath, form, module, codeEditor, designer);
    }

    private static string ToOwnerSourcePath(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".frx" => Path.ChangeExtension(path, ".frm"),
            ".ctx" => Path.ChangeExtension(path, ".ctl"),
            ".pgx" => Path.ChangeExtension(path, ".pag"),
            _ => path,
        };

    private static bool PathsEqual(string? a, string? b)
    {
        if (a is null || b is null)
            return false;
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }

    private static bool TryReadAllBytes(string path, out byte[] bytes)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                bytes = File.ReadAllBytes(path);
                return true;
            }
            catch (FileNotFoundException) { break; }
            catch (DirectoryNotFoundException) { break; }
            catch (IOException) { Thread.Sleep(50); }            // file mid-write / briefly locked
            catch (UnauthorizedAccessException) { Thread.Sleep(50); }
        }
        bytes = Array.Empty<byte>();
        return false;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        projectManager.ProjectLoaded -= OnProjectLoaded;
        projectManager.ProjectUnloaded -= OnProjectUnloaded;
        settings.PropertyChanged -= OnSettingsChanged;
        try { timer.Stop(); timer.Dispose(); } catch { /* best-effort */ }
        DetachAll();
    }
}
