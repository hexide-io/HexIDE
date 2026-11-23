using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Projects;

internal static class ProjectOutputPath
{
    // A .vbp may override the output name:  ExeName32="MyApp.exe"  (value may be unquoted).
    private static readonly Regex ExeName32Regex = new(
        @"^\s*ExeName32\s*=\s*""?([^""\r\n]+?)""?\s*$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static string OutputExePath(ProjectDefinition project)
    {
        var directory = Path.GetDirectoryName(project.AbsolutePath!)!;
        var name = ResolveOutputExeName(project);
        if (name is not null)
        {
            // Read the quoted path exactly as the serializer does, then resolve on the host filesystem.
            foreach (var raw in project.UnknownPreSectionLines.Select(line => line.Item2)
                         .Concat((project.ExtensionTail ?? "").ReplaceLineEndings("\n").Split('\n')))
            {
                if (Vb6ProjectPaths.ItemLineFile(raw) is not { } file
                    || !file.Key.Equals(Vb6ProjectPaths.MakePathKey, StringComparison.OrdinalIgnoreCase))
                    continue;
                var folder = Vb6ProjectPaths.ToHostPath(raw.Substring(file.Start, file.Length));
                directory = Path.Combine(directory, folder);
                break;
            }
        }
        return Path.Combine(directory, name ?? project.Name + ".exe");
    }

    // The compiled EXE's file name: a .vbp may specify ExeName32="Foo.exe"; otherwise VB6 names the
    // output after the project. ExeName32 is a top-level .vbp key, preserved verbatim among the unparsed
    // lines (and, defensively, scanned in the preserved section tail too).
    private static string? ResolveOutputExeName(ProjectDefinition project)
    {
        foreach (var (_, raw) in project.UnknownPreSectionLines)
        {
            var m = ExeName32Regex.Match(raw);
            if (m.Success && m.Groups[1].Value.Trim() is { Length: > 0 } name)
                return name;
        }
        if (project.ExtensionTail is { } tail)
        {
            var m = ExeName32Regex.Match(tail);
            if (m.Success && m.Groups[1].Value.Trim() is { Length: > 0 } name)
                return name;
        }
        return null;
    }

}
