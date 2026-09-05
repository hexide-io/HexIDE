using HexIDE.IDE;
using HexIDE.Lsp;

namespace HexIDE.Projects;

/// <summary>
/// Answers "where is the workspace" for the language layer, from whichever project is currently the
/// startup one.
///
/// <para>
/// Deliberately thin, and deliberately here rather than in the language layer. Reusing
/// <see cref="ProjectService.ProjectFilesDirectory"/> is the whole point: that is already the rule
/// deciding where a project's files live, including the fallback for a project that has never been saved,
/// and a second answer to the same question would drift from it.
/// </para>
///
/// <para>
/// <b>An unsaved project's directory is a shared temp path today</b> — keyed on the project's NAME, so
/// every new "Project1" resolves to the same place (hexide-io/HexIDE#260). That is wrong, and it is wrong
/// in the same way for a language server as it is for the files: the server is rooted somewhere that may
/// hold another project's leavings. Answering with it anyway is still better than answering with nothing,
/// because it is at least the directory the project's own files are in — and it stops being a special case
/// entirely once a new project gets a real location.
/// </para>
/// </summary>
public sealed class ProjectLspWorkspace(IProjectManager projectManager) : ILspWorkspace
{
    public string? Directory =>
        projectManager.StartupProject is { } project
            ? ProjectService.ProjectFilesDirectory(project)
            : null;
}
