using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HexIDE.Events;
using HexIDE.Lsp;
using Serilog;
using HexIDE.Addins;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using PropertyChanged.SourceGenerator;

namespace HexIDE.IDE;

public partial class ProjectManager : IProjectManager
{
    private readonly IEditorService editorService;
    private readonly IEventBus eventBus;
    private List<ProjectDefinition> loadedProjects = new();

    public IReadOnlyList<ProjectDefinition> LoadedProjects => loadedProjects;
    public event Action<ProjectDefinition>? ProjectLoaded;
    public event Action<ProjectDefinition>? ProjectUnloaded;

    [Notify] private ProjectDefinition? startupProject;
    [Notify] private ProjectGroupDefinition? currentGroup;

    public ProjectManager(IEditorService editorService,
        IEventBus eventBus,
        Func<ILspClient>? lspClient = null)
    {
        this.editorService = editorService;
        this.eventBus = eventBus;
        // Open only after loading has established sidecars and the unedited baselines.
        eventBus.Subscribe<ProjectOpenedEvent>(e => editorService.RestoreOpenDocuments(e.Project));
        PropertyChanged += async (_, args) =>
        {
            if (args.PropertyName != nameof(StartupProject) || lspClient is null) return;
            try { await lspClient().WorkspaceChangedAsync(); }
            catch (Exception ex) { Log.Warning(ex, "Could not move the language-server workspace"); }
        };
    }

    public void AddProject(ProjectDefinition proj)
    {
        if (heldPlace is { Unloaded: true } held && proj.AbsolutePath is { } path && HexIDE.IO.HostFilePath.Equals(path, held.Path))
        {
            loadedProjects.Insert(Math.Min(held.Index, loadedProjects.Count), proj);
            heldPlace = null;
        }
        else
            loadedProjects.Add(proj);
        ProjectLoaded?.Invoke(proj);
        StartupProject ??= proj;
    }

    public void UnloadAllProjects()
    {
        for (int i = loadedProjects.Count - 1; i >= 0; --i)
            UnloadProject(loadedProjects[i]);
        CurrentGroup = null;
    }

    public void UnloadProject(ProjectDefinition proj)
    {
        editorService.SaveOpenDocuments(proj);
        foreach (var form in proj.Forms)
            eventBus.Publish(new FormUnloadedEvent(form));
        eventBus.Publish(new ProjectUnloadedEvent(proj));
        loadedProjects.Remove(proj);
        ProjectUnloaded?.Invoke(proj);
        var holding = heldPlace?.Project == proj;
        if (holding)
            heldPlace!.Unloaded = true;
        // A held startup project gets its place back when it's opened again, so nothing else is made startup meanwhile.
        if (startupProject == proj)
            StartupProject = holding ? null : loadedProjects.FirstOrDefault();
        if (loadedProjects.Count <= 1 && !holding)
            CurrentGroup = null;
    }

    private HeldPlace? heldPlace;

    private sealed class HeldPlace(ProjectDefinition project, string path, int index)
    {
        public ProjectDefinition Project { get; } = project;
        public string Path { get; } = path;
        public int Index { get; } = index;
        public bool Unloaded { get; set; }
    }

    public IDisposable HoldPlace(ProjectDefinition project)
    {
        if (project.AbsolutePath is not { } path)
            return new HexIDE.Utils.ActionDisposable(() => { });
        var held = new HeldPlace(project, path, loadedProjects.IndexOf(project));
        heldPlace = held;
        return new HexIDE.Utils.ActionDisposable(() =>
        {
            if (heldPlace != held)
                return;
            // Not opened again: end as an unload always has.
            heldPlace = null;
            if (!held.Unloaded)
                return;
            if (loadedProjects.Count <= 1)
                CurrentGroup = null;
            StartupProject ??= loadedProjects.FirstOrDefault();
        });
    }

    public ProjectDefinition NewProject(IAddinProjectTemplate projectTemplate, string name, Action<ProjectDefinition>? beforeAdd = null)
    {
        var definition = projectTemplate.Create(name);
        beforeAdd?.Invoke(definition);
        AddProject(definition);
        if (definition.Forms.Count > 0)
            editorService.EditForm(definition.Forms[0]);
        return definition;
    }
}