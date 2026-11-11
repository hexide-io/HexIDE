using System;
using System.Runtime.CompilerServices;

namespace HexIDE.Projects;

public partial class ProjectService
{
    /// <summary>
    /// Renders a project's own file. Every render of one goes through here (#3698): its type's writer where it has one, and
    /// VB6's format otherwise. <c>ContributedProjectFileTests</c> fails the build if VB6's format renders a project file
    /// anywhere else, so a project an add-in opened never has its file written as a <c>.vbp</c>.
    /// </summary>
    /// <remarks>
    /// A writer that failed when the project opened left no baseline to compare with, so its file isn't written that session:
    /// each render after that is the same refusal, with the add-in's words.
    /// </remarks>
    private string RenderProjectFile(ProjectDefinition project, string path, Func<object, string?>? pathOf = null,
        bool includeRelatedDocuments = true)
    {
        if (project.ProjectFileWriter is not { } writer)
            return format.RenderProject(project, path, pathOf, includeRelatedDocuments);
        if (projectFilesRefusedAtOpen.TryGetValue(project, out var refusal))
            throw refusal;
        return writer.RenderProject(project, path, pathOf);
    }

    private readonly ConditionalWeakTable<ProjectDefinition, RenderRefusal> projectFilesRefusedAtOpen = new();
}
