using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using HexIDE.Forms.ViewModels;
using HexIDE.Localization;
using Serilog;

namespace HexIDE.Recovery;

/// <summary>
/// Offers back the unsaved work of IDE runs that ended without a clean exit (hexide-io/HexIDE#499 task group 2).
/// </summary>
public interface IRecoveryOffers
{
    /// <summary>
    /// Finds the runs that ended without a clean exit, and offers the work of every project in them that was never
    /// saved, or saved only to a scratch folder. Says whether anything was restored. A saved project's work waits until
    /// that project is opened.
    /// </summary>
    Task<bool> OfferAtStartAsync();

    /// <summary>Lists every project whose work is still waiting, for File › Recover Unsaved Work.</summary>
    Task ShowPendingAsync();
}

/// <inheritdoc/>
/// <remarks>
/// <para>
/// <b>Offered where the work can be placed.</b> A project that never had a file is offered at start, since there is
/// nothing to open. A saved one is offered as it is opened, matched by its path, or by its project file once nothing is
/// left at the old path: a moved project is still found, and a copy beside the original is not. File › Recover Unsaved
/// Work lists whatever is still waiting, so nothing is stranded behind a project that is never opened again.
/// </para>
/// <para>
/// <b>Restored work comes back unsaved.</b> The project is opened from disk as usual, then each backed-up document
/// takes its backup, with every baseline left as the file on disk is. So the documents read edited, and their files
/// change only when they are saved. A document whose backup is what it already holds is left alone.
/// </para>
/// <para>
/// <b>A saved project's own settings are not restored.</b> Its backed-up project file says which documents it held,
/// and a document missing since is added back. A changed startup object or reference is not re-applied.
/// </para>
/// </remarks>
public sealed class RecoveryOffers : IRecoveryOffers
{
    private readonly IRecoveryService recovery;
    private readonly IProjectService projects;
    private readonly IProjectManager projectManager;
    private readonly IWindowManager windowManager;
    private readonly ILocalizationService localization;
    private readonly IDocumentDockService dock;
    private readonly IFileBaselineStore baselineStore;
    private readonly IClock clock;
    private readonly FileReloader reloader;
    private readonly List<Waiting> waiting = [];
    private bool detected;

    public RecoveryOffers(IRecoveryService recovery, IProjectService projects, IProjectManager projectManager,
        IWindowManager windowManager, ILocalizationService localization, IDocumentDockService dock,
        IFileBaselineStore baselineStore, IClock clock, IEventBus eventBus, IStatusBarService statusBar,
        IHeaderRefresher headerRefresher)
    {
        this.recovery = recovery;
        this.projects = projects;
        this.projectManager = projectManager;
        this.windowManager = windowManager;
        this.localization = localization;
        this.dock = dock;
        this.baselineStore = baselineStore;
        this.clock = clock;
        reloader = new FileReloader(projects, eventBus, statusBar, localization, headerRefresher);
        eventBus.Subscribe<ProjectOpenedEvent>(e => OfferForAsync(e.Project).ListenErrors());
    }

    /// <summary>The work still waiting, for a test to read.</summary>
    internal int WaitingCount => waiting.Count;

    public async Task<bool> OfferAtStartAsync()
    {
        Detect();
        var atStart = waiting.Where(w => w.Record.Path is null || w.Record.Scratch).ToList();

        // A scratch project counts as unsaved whatever it holds (#2678), so a killed --newproject run leaves a
        // backup even when nothing was edited. One its files on disk already hold, byte for byte, is not offered.
        foreach (var w in atStart.Where(w => w.Record.Scratch && FilesHold(w)).ToList())
        {
            Log.Information("Crash recovery dropped the backup of {Project}: its scratch files already hold it", w.Record.Name);
            Resolve(w);
            atStart.Remove(w);
        }
        if (atStart.Count == 0)
            return false;
        return await OfferAsync(atStart, opened: null) > 0;
    }

    // Whether every file w's backup holds is on disk already, as the bytes its save would write.
    private static bool FilesHold(Waiting w)
    {
        UnsavedProject backup;
        try
        {
            backup = w.Run.Read(w.Record);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        if (backup.Path is not { } projectPath
            || backup.ProjectFile is { } projectFile && !Holds(projectPath, projectFile.Text, backup.Encoding))
            return false;
        foreach (var document in backup.Documents)
        {
            if (document.Content is not { } content)
                continue;
            if (document.Path is not { } path || !Holds(path, content.Text, document.Encoding))
                return false;
            if (content is { CompanionFileName: { } companionName, Companion: { } companion }
                && !Holds(Path.Combine(Path.GetDirectoryName(path) ?? "", companionName), companion))
                return false;
        }
        return true;
    }

    private static bool Holds(string path, string text, Vb6TextEncoding encoding)
    {
        byte[] bytes;
        try
        {
            bytes = Vb6TextFile.Encode(text, encoding);
        }
        catch (EncoderFallbackException)
        {
            return false; // a save would refuse it, so the file can't hold it
        }
        return Holds(path, bytes);
    }

    private static bool Holds(string path, byte[] bytes)
    {
        try
        {
            return File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public async Task ShowPendingAsync()
    {
        Detect();
        if (waiting.Count == 0)
        {
            await windowManager.MessageBox(localization.GetString("Str.Dialog.Recovery.Nothing"),
                localization.GetString("Str.Dialog.Recovery.Title"), icon: MessageBoxIcon.Information);
            return;
        }
        await OfferAsync([.. waiting], opened: null);
    }

    /// <summary>Offers the work that belongs to <paramref name="project"/>, which has just been opened from its file.</summary>
    internal async Task OfferForAsync(ProjectDefinition project)
    {
        Detect();
        if (project.AbsolutePath is not { } path || waiting.Count == 0)
            return;
        var hash = baselineStore.TryGet(path)?.Hash;
        var matches = waiting.Where(w => w.Record.Path is { } old
            && (SamePath(old, path) || !File.Exists(old) && hash is not null && w.Record.ProjectFileHash == hash)).ToList();

        // A backup that says nothing the opened project doesn't already is not offered, and goes. A tick inside a
        // load leaves one, with every document captured as unsaved and each identical to its file.
        foreach (var w in matches.Where(w => !AddsTo(project, w)).ToList())
        {
            Log.Information("Crash recovery dropped the backup of {Project}: its files on disk already hold it", w.Record.Name);
            Resolve(w);
            matches.Remove(w);
        }
        if (matches.Count > 0)
            await OfferAsync(matches, project);
    }

    // Whether restoring w into project would change anything: a document it holds differently, or one it lacks.
    private bool AddsTo(ProjectDefinition project, Waiting w)
    {
        UnsavedProject backup;
        try
        {
            backup = w.Run.Read(w.Record);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true; // offered, and the restore says it can't be read
        }
        return backup.Documents.Any(document => Find(project, document) switch
        {
            null => document.Content is not null || document.Path is { } path && File.Exists(path),
            FormDefinition form => document.Content is { } content && !projects.RecoveredMatches(form, content),
            ModuleDefinition module => document.Content is { } content && !projects.RecoveredMatches(module, content),
            _ => false,
        });
    }

    /// <summary>Finds and claims the runs that ended uncleanly, once a session.</summary>
    internal void Detect()
    {
        if (detected)
            return;
        detected = true;
        if (recovery.RunFolder is not { } own || Path.GetDirectoryName(own) is not { } root)
            return;
        try
        {
            foreach (var run in PendingRun.Find(root, own, PendingRun.IsAlive, clock.UtcNow))
                foreach (var project in run.Projects)
                    waiting.Add(new Waiting(run, project));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Crash recovery could not look for unsaved work in {Root}", root);
        }
    }

    // Shows the dialog, and answers how many projects were restored from it.
    private async Task<int> OfferAsync(IReadOnlyList<Waiting> offered, ProjectDefinition? opened)
    {
        var restored = 0;
        var dialog = new RecoveryDialogViewModel(localization,
            offered.OrderByDescending(w => w.Run.SavedAt).Select(w => Describe(w, opened)),
            async item =>
            {
                if (!await RestoreAsync((Waiting)item.Key, opened))
                    return false;
                restored++;
                return true;
            },
            item => DiscardAsync((Waiting)item.Key));
        await windowManager.ShowDialog(dialog);
        return restored;
    }

    private RecoveryItemViewModel Describe(Waiting w, ProjectDefinition? opened)
    {
        var record = w.Record;
        var origin = record.Path is null || record.Scratch
            ? localization.GetString("Str.Dialog.Recovery.NeverSaved")
            : opened?.AbsolutePath is { } now && !SamePath(now, record.Path)
                ? string.Format(localization.GetString("Str.Dialog.Recovery.MovedFrom"), Path.GetDirectoryName(record.Path))
                : string.Format(localization.GetString("Str.Dialog.Recovery.From"), Path.GetDirectoryName(record.Path));
        var backedUp = string.Format(localization.GetString("Str.Dialog.Recovery.BackedUp"),
            w.Run.SavedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "");
        var changed = record.Documents.Where(ChangedSince).Select(d => d.Name).ToList();
        return new RecoveryItemViewModel(record.Name, origin, backedUp,
            changed.Count > 0
                ? string.Format(localization.GetString("Str.Dialog.Recovery.ChangedOnDisk"), string.Join(", ", changed))
                : null,
            w);
    }

    // A backed-up document whose file has changed on disk since the backup: restoring puts the backup in the IDE
    // over a newer file, which the next save would then replace (task 2.4).
    private static bool ChangedSince(RecoveryRun.DocumentRecord document)
    {
        if (document is not { File: not null, Path: { } path, DiskHash: { } hash } || !File.Exists(path))
            return false;
        try
        {
            return FileHasher.Hash(File.ReadAllBytes(path)) != hash;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private async Task<bool> RestoreAsync(Waiting w, ProjectDefinition? opened)
    {
        var record = w.Record;
        UnsavedProject backup;
        try
        {
            backup = w.Run.Read(record);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Crash recovery could not read the backup of {Project}", record.Name);
            await Failed(record.Name);
            return false;
        }

        // Off the list before anything opens, or the opening would offer the same work again.
        waiting.Remove(w);
        var restored = false;
        try
        {
            if (opened is not null)
                restored = await OverlayAsync(opened, backup);
            else if (record.Path is { } path && File.Exists(path))
            {
                if (LoadedAt(path) is null)
                    await projects.OpenProject(path);
                restored = LoadedAt(path) is { } loaded && await OverlayAsync(loaded, backup);
            }
            else if (record.Path is null || record.Scratch)
                restored = await projects.RestoreUnsavedProject(backup) is not null;
            else
            {
                await windowManager.MessageBox(string.Format(localization.GetString("Str.Dialog.Recovery.NotFound"),
                    record.Name, Path.GetDirectoryName(record.Path)), localization.GetString("Str.Dialog.Recovery.Title"),
                    icon: MessageBoxIcon.Information);
                waiting.Add(w);
                return false;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "Crash recovery could not restore {Project}", record.Name);
        }
        catch (OperationCanceledException)
        {
            // An open the person cancelled, as the project being open already.
        }

        if (!restored)
        {
            waiting.Add(w);
            await Failed(record.Name);
            return false;
        }
        Resolve(w);
        return true;
    }

    // Puts each backed-up document into the open project, and adds back any it no longer holds.
    private async Task<bool> OverlayAsync(ProjectDefinition project, UnsavedProject backup)
    {
        var whole = true;
        foreach (var document in backup.Documents)
        {
            var target = Find(project, document);
            if (target is null && document.Path is { } path && File.Exists(path))
                target = await AddExistingAsync(project, document, path);
            if (document.Content is not { } content)
                continue;
            if (target is null)
            {
                whole &= projects.AddRecovered(project, document) is not null;
                continue;
            }

            var form = target as FormDefinition;
            var module = target as ModuleDefinition;
            if (form is not null ? projects.RecoveredMatches(form, content) : projects.RecoveredMatches(module!, content))
                continue;
            var replaced = false;
            await reloader.ReplaceAsync(
                WatchedFileTarget.For(form?.AbsolutePath ?? module?.AbsolutePath ?? document.Name, form, module, dock.OpenDocuments),
                () => Task.FromResult(replaced = form is not null
                    ? projects.ApplyRecovered(form, content)
                    : projects.ApplyRecovered(module!, content)),
                announce: false);
            whole &= replaced;
        }
        return whole;
    }

    private async Task<object?> AddExistingAsync(ProjectDefinition project, UnsavedDocument document, string path)
    {
        if (document.Kind == "Form")
            return (await projects.AddExistingForm(project, path)).Document;
        return Enum.TryParse<ModuleKind>(document.Kind, out var kind)
            ? (await projects.AddExistingModule(project, path, kind)).Document
            : null;
    }

    private static object? Find(ProjectDefinition project, UnsavedDocument document) =>
        document.Kind == "Form"
            ? project.Forms.FirstOrDefault(f => string.Equals(f.Name, document.Name, StringComparison.OrdinalIgnoreCase))
            : project.Modules.FirstOrDefault(m => string.Equals(m.Name, document.Name, StringComparison.OrdinalIgnoreCase)
                && m.Kind.ToString() == document.Kind);

    private async Task<bool> DiscardAsync(Waiting w)
    {
        var answer = await windowManager.MessageBox(
            string.Format(localization.GetString("Str.Dialog.Recovery.DiscardConfirm"), w.Record.Name),
            localization.GetString("Str.Dialog.Recovery.Title"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != MessageBoxResult.Yes)
            return false;
        Resolve(w);
        return true;
    }

    private void Resolve(Waiting w)
    {
        waiting.Remove(w);
        try
        {
            w.Run.Resolve(w.Record);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Crash recovery could not update {Folder} after {Project} was dealt with", w.Run.Folder, w.Record.Name);
        }
    }

    private Task Failed(string project) => windowManager.MessageBox(
        string.Format(localization.GetString("Str.Dialog.Recovery.Failed"), project),
        localization.GetString("Str.Dialog.Recovery.Title"), icon: MessageBoxIcon.Warning);

    private ProjectDefinition? LoadedAt(string path) =>
        projectManager.LoadedProjects.FirstOrDefault(p => p.AbsolutePath is { } loaded && SamePath(loaded, path));

    private static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private sealed record Waiting(PendingRun Run, RecoveryRun.ProjectRecord Record);
}
