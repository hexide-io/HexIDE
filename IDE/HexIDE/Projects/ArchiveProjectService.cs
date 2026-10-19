using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.TwinBasic;

namespace HexIDE.Projects;

/// <summary>UI-owned archive lifecycle. Only serialized file reads run on a worker thread.</summary>
public sealed class ArchiveProjectService
{
    private readonly ArchiveProjectWorkspace workspace = new();
    private readonly SemaphoreSlim gate = new(1);
    private readonly IDocumentDockService dock;
    private readonly IRecentProjectsService recent;
    private readonly ILocalizationService localization;
    private bool treeSelected;
    public IReadOnlyList<ArchiveProjectSnapshot> Projects { get; private set; } = [];
    public event Action? Changed;
    public event Action? FocusChanged;
    public bool IsActive => treeSelected || dock.ActiveDocument is ArchiveDocumentViewModel;

    public ArchiveProjectService(IDocumentDockService dock, IRecentProjectsService recent,
        ILocalizationService localization)
    {
        this.dock = dock;
        this.recent = recent;
        this.localization = localization;
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
        await gate.WaitAsync();
        try
        {
            var project = await Task.Run(() => workspace.Open(path));
            Publish();
            recent.Add(project.ContainerPath);
            SelectTreeEntry(true);
        }
        finally { gate.Release(); }
    }

    public async Task Reload(string path)
    {
        await gate.WaitAsync();
        try
        {
            var replacement = await Task.Run(() => workspace.Reload(path));
            foreach (var tab in Tabs(replacement.ContainerPath))
                if (replacement.TryGetEntry(tab.Entry.Identity, out var entry)) tab.Update(entry);
                else dock.CloseDocument(tab);
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
            if (path == null) workspace.CloseAll();
            else workspace.Close(path);
            foreach (var tab in Tabs(path)) dock.CloseDocument(tab);
            SelectTreeEntry(false);
            Publish();
        }
        finally { gate.Release(); }
    }

    public void OpenDocument(ArchiveProjectEntry entry)
    {
        // Stale tree nodes cannot reopen a closed project or an old snapshot.
        var project = Projects.FirstOrDefault(p =>
            ArchiveEntryIdentity.ContainerPathComparer.Equals(p.ContainerPath, entry.Identity.ContainerPath));
        if (project == null || !project.TryGetEntry(entry.Identity, out var current)) return;
        if (!dock.TryActivate<ArchiveDocumentViewModel>(vm => vm.Entry.Identity.Equals(current.Identity)))
            dock.OpenDocument(new ArchiveDocumentViewModel(current, localization));
    }

    private ArchiveDocumentViewModel[] Tabs(string? path) => dock.OpenDocuments
        .OfType<ArchiveDocumentViewModel>().Where(vm => path == null ||
            ArchiveEntryIdentity.ContainerPathComparer.Equals(vm.Entry.Identity.ContainerPath, path)).ToArray();

    private void Publish()
    {
        Projects = workspace.Projects;
        Changed?.Invoke();
    }
}
