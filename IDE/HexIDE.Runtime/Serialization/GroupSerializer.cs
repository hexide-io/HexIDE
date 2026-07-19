using System.Collections.Generic;
using System.IO;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Serialization;

public class GroupSerializer
{
    public string Serialize(string groupPath, IReadOnlyList<ProjectDefinition> projects,
        ProjectDefinition? startupProject, IReadOnlyList<string>? unknownLines = null)
    {
        var dir = Path.GetDirectoryName(groupPath)!;
        var w = new StringWriter { NewLine = "\r\n" };
        w.WriteLine("VBGROUP 5.00");
        if (startupProject?.AbsolutePath != null)
            w.WriteLine($"StartupProject={Path.GetRelativePath(dir, startupProject.AbsolutePath)}");
        foreach (var p in projects)
            if (p.AbsolutePath != null)
                w.WriteLine($"Project={Path.GetRelativePath(dir, p.AbsolutePath)}");
        if (unknownLines != null)
            foreach (var line in unknownLines)
                w.WriteLine(line);
        return w.ToString();
    }
}
