using HexIDE.IDE;

namespace HexIDE.Events;

/// <summary>
/// Published once a project read from its file is fully open: loaded, its baselines taken, and its load notices shown.
/// </summary>
/// <remarks>
/// <see cref="IProjectManager.ProjectLoaded"/> is raised earlier, as the project is added and before its baselines exist.
/// Crash recovery offers a project's unsaved work here instead (hexide-io/HexIDE#499), since work restored before the
/// baselines were taken would be counted as saved.
/// </remarks>
public sealed class ProjectOpenedEvent(ProjectDefinition project) : IEvent
{
    public ProjectDefinition Project { get; } = project;
}
