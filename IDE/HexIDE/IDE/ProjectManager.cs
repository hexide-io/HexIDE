using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HexIDE.Events;
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
        IEventBus eventBus)
    {
        this.editorService = editorService;
        this.eventBus = eventBus;
    }

    public void AddProject(ProjectDefinition proj)
    {
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
        foreach (var form in proj.Forms)
            eventBus.Publish(new FormUnloadedEvent(form));
        eventBus.Publish(new ProjectUnloadedEvent(proj));
        loadedProjects.Remove(proj);
        ProjectUnloaded?.Invoke(proj);
        if (startupProject == proj)
            StartupProject = loadedProjects.FirstOrDefault();
        if (loadedProjects.Count <= 1)
            CurrentGroup = null;
    }

    public ProjectDefinition NewProject(IAddinProjectTemplate projectTemplate, string name)
    {
        var definition = projectTemplate.Create(name);
        AddProject(definition);
        if (definition.Forms.Count > 0)
            editorService.EditForm(definition.Forms[0]);
        return definition;
    }
}