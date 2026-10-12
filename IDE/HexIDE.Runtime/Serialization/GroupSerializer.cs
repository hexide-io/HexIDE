using System;
using System.Collections.Generic;
using System.IO;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Serialization;

public class GroupSerializer
{
    /// <param name="pathOf">
    /// Where a project will be when this text is written, for a caller checking a save before it moves
    /// anything; null for a project staying where it is. Without it, each project is named where it is now.
    /// </param>
    public string Serialize(string groupPath, IReadOnlyList<ProjectDefinition> projects,
        ProjectDefinition? startupProject, IReadOnlyList<string>? unknownLines = null,
        Func<object, string?>? pathOf = null)
    {
        string? PathOf(ProjectDefinition project) => pathOf?.Invoke(project) ?? project.AbsolutePath;

        var dir = Path.GetDirectoryName(groupPath)!;
        var w = new StringWriter { NewLine = "\r\n" };
        w.WriteLine("VBGROUP 5.00");
        if (startupProject is not null && PathOf(startupProject) is { } startupPath)
            w.WriteLine($"StartupProject={SerializedProject.ToProjectFilePath(Path.GetRelativePath(dir, startupPath))}");
        foreach (var p in projects)
            if (PathOf(p) is { } projectPath)
                w.WriteLine($"Project={SerializedProject.ToProjectFilePath(Path.GetRelativePath(dir, projectPath))}");
        if (unknownLines != null)
            foreach (var line in unknownLines)
                w.WriteLine(line);
        return w.ToString();
    }
}
