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

    public ProjectDefinition NewProject(IAddinProjectTemplate projectTemplate, string name);
    void AddProject(ProjectDefinition project);
    void UnloadAllProjects();
    void UnloadProject(ProjectDefinition projectManager);
}