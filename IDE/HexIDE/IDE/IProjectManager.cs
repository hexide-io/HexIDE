using System;
using System.Collections.Generic;
using System.ComponentModel;
using HexIDE.Addins;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.IDE;

public interface IProjectManager : INotifyPropertyChanged
{
    public IReadOnlyList<ProjectDefinition> LoadedProjects { get; }
    public event Action<ProjectDefinition>? ProjectLoaded;
    public event Action<ProjectDefinition>? ProjectUnloaded;

    public ProjectDefinition? StartupProject { get; set; }
    public ProjectGroupDefinition? CurrentGroup { get; set; }

    public ProjectDefinition NewProject(IAddinProjectTemplate projectTemplate, string name, Action<ProjectDefinition>? beforeAdd = null);
    void AddProject(ProjectDefinition project);
    void UnloadAllProjects();
    void UnloadProject(ProjectDefinition projectManager);

    /// <summary>
    /// Keeps a project's place while it's unloaded and opened again from its file, as Reload does (#4009). The project
    /// opened next from the same path goes back to the same position, and is the startup project again if it was, and the
    /// group isn't dropped meanwhile. If none is opened before the returned handle is disposed, it ends as an unload does.
    /// </summary>
    IDisposable HoldPlace(ProjectDefinition project);
}