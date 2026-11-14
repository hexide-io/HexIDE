using System;
using System.Collections.Generic;

namespace HexIDE.Runtime.Serialization;

public class GroupDeserializer
{
    public SerializedProjectGroup Deserialize(string source)
    {
        string? startupRelPath = null;
        var startupPosition = 0;
        var projectPaths = new List<string>();
        var unknownLines = new List<string>();

        foreach (var line in source.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.Equals("VBGROUP 5.0", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("VBGROUP 5.00", StringComparison.OrdinalIgnoreCase))
                continue;
            if (trimmed.StartsWith("StartupProject=", StringComparison.OrdinalIgnoreCase))
            {
                startupRelPath = trimmed[15..];
                startupPosition = projectPaths.Count;
            }
            else if (trimmed.StartsWith("Project=", StringComparison.OrdinalIgnoreCase))
                projectPaths.Add(trimmed[8..]);
            else
                unknownLines.Add(trimmed);
        }

        // Older HexIDE files listed startup twice. Their Project= lines own the order (#579).
        if (startupRelPath != null && !projectPaths.Exists(path =>
                string.Equals(path, startupRelPath, StringComparison.OrdinalIgnoreCase)))
            projectPaths.Insert(startupPosition, startupRelPath);

        return new SerializedProjectGroup(startupRelPath, projectPaths, unknownLines);
    }
}
