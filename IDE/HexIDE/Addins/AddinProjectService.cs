using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Addins;

public sealed class AddinProjectService(IProjectManager projectManager) : IProjectAccess
{
    public AddinProjectInfo? GetActiveProject() =>
        projectManager.StartupProject is { } p ? Describe(p) : null;

    public IReadOnlyList<AddinFileInfo> GetFiles() =>
        projectManager.StartupProject is { } p ? FilesOf(p) : [];

    public IReadOnlyList<AddinProjectInfo> GetProjects() =>
        [.. projectManager.LoadedProjects.Select(Describe)];

    private static AddinProjectInfo Describe(ProjectDefinition project) =>
        new(project.Name, project.AbsolutePath ?? string.Empty, FilesOf(project));

    /// <summary>
    /// A project's documents, each naming the project it belongs to.
    /// </summary>
    /// <remarks>
    /// A UserControl or PropertyPage appears once, as its module — one file, one entry. It used to appear
    /// as a module and, for a name, whatever its designer half answered to.
    /// </remarks>
    private static IReadOnlyList<AddinFileInfo> FilesOf(ProjectDefinition project) =>
        [.. DocumentLookup.DocumentsOf(project).Select(document =>
            new AddinFileInfo(
                document.Name,
                document.AbsolutePath ?? string.Empty,
                Kind(document),
                document.Project.Name))];

    private static AddinDocumentKind Kind(DocumentIdentity document) =>
        document.Module?.Kind switch
        {
            ModuleKind.UserControl => AddinDocumentKind.UserControl,
            ModuleKind.StandardModule or ModuleKind.ClassModule => AddinDocumentKind.Module,
            // There is no PropertyPage kind, and calling one a module would be a guess with consequences.
            ModuleKind.PropertyPage => AddinDocumentKind.Other,
            _ => AddinDocumentKind.Form,
        };
}
