using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Language;
using HexIDE.Lsp;
using HexIDE.Localization;
using HexIDE.TwinBasic;
using HexIDE.Utils;

namespace HexIDE.Projects;

/// <summary>UI-owned read-only container lifecycle. Serialized file operations run on a worker thread.</summary>
public sealed class ArchiveProjectService
{
    /// <summary>The project archives this service reads, which no project type may take (#3411 slice 10b).</summary>
    public static ProjectFileReservation Reservation { get; } = new("hexide.archives", "twinBASIC", ["*.twinproj"]);
    public static ProjectFileReservation OfficeReservation { get; } = new("hexide.office", "Microsoft Office", OfficeBrowserProject.Patterns);
    public static bool Covers(string path) => Reservation.Covers(path) || OfficeReservation.Covers(path);
    public static IReadOnlyList<string> FilePatterns { get; } = [.. Reservation.Patterns, .. OfficeReservation.Patterns];

    private readonly Dictionary<string, IContainerProject> projects = new(ArchiveEntryIdentity.ContainerPathComparer);
    private readonly SemaphoreSlim gate = new(1);
    private readonly IDocumentDockService dock;
    private readonly IRecentProjectsService recent;
    private readonly ILocalizationService localization;
    private readonly IWindowManager windows;
    private readonly IConditionalCodeReader conditionalCode;
    private readonly Func<ILspClient>? lspClient;
    private bool treeSelected;
    private readonly Dictionary<string, string> folderChoices = new(ArchiveEntryIdentity.ContainerPathComparer);
    public IReadOnlyList<IContainerProject> Projects { get; private set; } = [];
    public event Action? Changed;
    public event Action? FocusChanged;
    public bool IsActive => treeSelected || dock.ActiveDocument is ArchiveDocumentViewModel;

    public ArchiveProjectService(IDocumentDockService dock, IRecentProjectsService recent,
        ILocalizationService localization, IWindowManager windows, IConditionalCodeReader conditionalCode, Func<ILspClient>? lspClient = null)
    {
        this.lspClient = lspClient;
        this.dock = dock;
        this.recent = recent;
        this.localization = localization;
        this.windows = windows;
        this.conditionalCode = conditionalCode;
        dock.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(dock.ActiveDocument)) return;
            treeSelected = false;
            FocusChanged?.Invoke();
        };
    }

    public void SelectTreeEntry(bool selected)
    {
        treeSelected = selected;
        FocusChanged?.Invoke();
    }

    public async Task Open(string path)
    {
        var locked = false;
        await gate.WaitAsync();
        try
        {
            path = Path.GetFullPath(path);
            if (!projects.TryGetValue(path, out var project))
            {
                project = await Task.Run(() => Read(path));
                projects.Add(path, project);
            }
            locked = project.IsLocked;
            Publish();
            recent.Add(project.ContainerPath);
            SelectTreeEntry(true);
        }
        finally { gate.Release(); }
        if (locked) await windows.MessageBox(localization.GetString("Str.Office.LockedMessage"), icon: MessageBoxIcon.Information);
    }

    public async Task Reload(string path)
    {
        await gate.WaitAsync();
        try
        {
            path = Path.GetFullPath(path);
            if (!projects.ContainsKey(path)) throw new InvalidOperationException("The container is not open.");
            var replacement = await Task.Run(() => Read(path));
            projects[path] = replacement;
            foreach (var tab in Tabs(replacement.ContainerPath))
                if (replacement.TryGetEntry(tab.Entry.Identity, out var entry))
                    tab.Update(entry, replacement.BuildType);
                else dock.CloseDocument(tab);
            dock.RecentlyClosed?.RefreshArchive(replacement);
            Publish();
        }
        finally { gate.Release(); }
    }

    public async Task Close(string? path = null)
    {
        if (path != null) path = Path.GetFullPath(path);
        await gate.WaitAsync();
        try
        {
            if (path == null) projects.Clear();
            else projects.Remove(path);
            foreach (var tab in Tabs(path)) dock.CloseDocument(tab);
            dock.RecentlyClosed?.CloseArchive(path);
            SelectTreeEntry(false);
            Publish();
        }
        finally { gate.Release(); }
    }

    public async Task SynchronizeFolder(string path)
    {
        path = Path.GetFullPath(path);
        string message;
        var icon = MessageBoxIcon.Information;
        await gate.WaitAsync();
        try
        {
            // Selection can outlive a closed tree node while waiting for another operation.
            if (!Projects.Any(p => p.CanSynchronizeFolder && ArchiveEntryIdentity.ContainerPathComparer.Equals(p.ContainerPath, path))) return;
            var engine = new ArchiveSourceFolder { ChosenRoot = folderChoices.GetValueOrDefault(path) };
            ArchiveFolderResult result;
            try { result = await Task.Run(() => engine.Synchronize(path)); }
            catch (ProjectLocalStateConflict conflict)
            {
                var choice = await ProjectLocalStateChoice.Ask(windows, localization, conflict.Candidates);
                if (choice == null) return;
                folderChoices[path] = choice;
                engine.ChosenRoot = choice;
                result = await Task.Run(() => engine.Synchronize(path));
            }
            message = string.Format(localization.GetString("Str.Archive.FolderReadyFormat"), result.Directory);
        }
        catch (ProjectLocalStatePathException ex)
        {
            var reason = string.Format(localization.GetString("Str.ProjectLocalState.UnavailableFormat"),
                ex.StatePath);
            message = string.Format(localization.GetString("Str.Archive.FolderErrorFormat"), reason);
            icon = MessageBoxIcon.Warning;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            message = string.Format(localization.GetString("Str.Archive.FolderErrorFormat"), ex.Message);
            icon = MessageBoxIcon.Warning;
        }
        finally { gate.Release(); }
        // Do not hold the lifecycle gate while the user reads the result.
        await windows.MessageBox(message, localization.GetString("Str.Archive.SyncFolder"), icon: icon);
    }

    public void OpenDocument(ContainerEntry entry)
    {
        // Stale tree nodes cannot reopen a closed project or an old snapshot.
        var project = Projects.FirstOrDefault(p =>
            ArchiveEntryIdentity.ContainerPathComparer.Equals(p.ContainerPath, entry.Identity.ContainerPath));
        if (project == null || !project.TryGetEntry(entry.Identity, out var current)) return;
        if (project.IsLocked)
        {
            windows.MessageBox(localization.GetString("Str.Office.LockedMessage"), icon: MessageBoxIcon.Information).ListenErrors();
            return;
        }
        if (current.Archive == null && !current.IsDocument) return;
        if (!dock.TryActivate<ArchiveDocumentViewModel>(vm => vm.Entry.Identity.Equals(current.Identity)))
            dock.OpenDocument(new ArchiveDocumentViewModel(current, localization, conditionalCode, project.BuildType, project.SupportsLanguageServers ? lspClient?.Invoke() : null));
    }

    private ArchiveDocumentViewModel[] Tabs(string? path) => dock.OpenDocuments
        .OfType<ArchiveDocumentViewModel>().Where(vm => path == null ||
            ArchiveEntryIdentity.ContainerPathComparer.Equals(vm.Entry.Identity.ContainerPath, path)).ToArray();

    private static IContainerProject Read(string path)
    {
        if (OfficeReservation.Covers(path)) return OfficeBrowserProject.Read(path);
        using var input = File.OpenRead(path);
        return new TwinBasicBrowserProject(ArchiveProjectSnapshot.Read(input, path));
    }

    private void Publish()
    {
        Projects = projects.Values.ToArray();
        Changed?.Invoke();
    }
}
