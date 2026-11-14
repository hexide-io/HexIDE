using System;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.IDE;

public sealed class ProjectFileSource
{
    public object Owner { get; }
    public string? Path => Owner switch
    {
        ProjectDefinition project => ChosenPath(project),
        ProjectGroupDefinition group => group.AbsolutePath,
        _ => null,
    };
    // A type without a project-file writer declares no encoding. Read UTF-8, refusing invalid bytes.
    public Vb6TextEncoding Encoding => Owner switch
    {
        ProjectDefinition { Offers.WritesProjectFile: false, ProjectFileWriter: null } => Vb6TextEncoding.Utf8WithBom,
        ProjectDefinition project => project.TextEncoding,
        ProjectGroupDefinition group => group.TextEncoding,
        _ => Vb6TextEncoding.Utf8WithBom,
    };

    public ProjectFileSource(ProjectDefinition project) => Owner = project;
    public ProjectFileSource(ProjectGroupDefinition group) => Owner = group;

    public static string? ChosenPath(ProjectDefinition project)
    {
        if (project.AbsolutePath is not { } path) return null;
        if (project.WorkingDirectory is not { } scratch) return path;
        var relative = System.IO.Path.GetRelativePath(scratch, path);
        return !System.IO.Path.IsPathRooted(relative) && relative != ".."
            && !relative.StartsWith(".." + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal) ? null : path;
    }
}
