using System;
using System.Collections.Generic;

namespace HexIDE.Runtime.Serialization;

public class GroupDeserializer
{
    public SerializedProjectGroup Deserialize(string source)
    {
        string? startupRelPath = null;
        var projectPaths = new List<string>();
        var unknownLines = new List<string>();

        foreach (var line in source.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.Equals("VBGROUP 5.00", StringComparison.OrdinalIgnoreCase))
                continue;
            if (trimmed.StartsWith("StartupProject=", StringComparison.OrdinalIgnoreCase))
                startupRelPath = trimmed[15..];
            else if (trimmed.StartsWith("Project=", StringComparison.OrdinalIgnoreCase))
                projectPaths.Add(trimmed[8..]);
            else
                unknownLines.Add(trimmed);
        }

        return new SerializedProjectGroup(startupRelPath, projectPaths, unknownLines);
    }
}

public record SerializedProjectGroup(
    string? StartupProjectRelativePath,
    IReadOnlyList<string> ProjectRelativePaths,
    IReadOnlyList<string> UnknownLines);
