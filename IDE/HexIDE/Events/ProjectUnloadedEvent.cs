using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Events;

public class ProjectUnloadedEvent : IEvent
{
    public ProjectDefinition Project { get; }

    public ProjectUnloadedEvent(ProjectDefinition project)
    {
        Project = project;
    }
}