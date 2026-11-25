using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using HexIDE.IO;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Projects;

internal static class ProjectOutputPath
{
    // A .vbp may override the output name:  ExeName32="MyApp.exe"  (value may be unquoted).
    private static readonly Regex ExeName32Regex = new(
        @"^\s*ExeName32\s*=\s*""?([^""\r\n]+?)""?\s*$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The field that would put the made program outside its folder, and what it holds, or null when the program's name is
    /// a plain file name (#5622). <c>ExeName32</c> names the program; with none, the project's <c>Name</c> does.
    /// </summary>
    /// <remarks>
    /// <c>ExeName32</c> is taken as a file name only. <c>VB6.EXE /make</c> accepts a path there when it builds in place,
    /// <c>..\x.exe</c> landing beside the project's folder, and keeps only the file name under <c>/outdir</c> (measured,
    /// SP6, #5622). HexIDE refuses one rather than write a program, and then start it, where the project points. The
    /// folder is <c>Path32</c>'s to name, relative or absolute, as VB6 allows.
    /// </remarks>
    internal static (string Field, string Value)? RefusedName(ProjectDefinition project) =>
        ResolveOutputExeName(project) is { } name
            ? ConfinedPath.IsFileName(name) ? null : ("ExeName32", name)
            : ConfinedPath.IsFileName(project.Name + ".exe") ? null : ("Name", project.Name);

    /// <summary>
    /// Where the made program goes: the project's folder, or the folder <c>Path32</c> names, and in it the file
    /// <c>ExeName32</c> names, or the project's name. Ask <see cref="RefusedName"/> first: this throws for a name it refuses.
    /// </summary>
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
        var fileName = name ?? project.Name + ".exe";
        return ConfinedPath.Join(directory, fileName)
               ?? throw new InvalidOperationException(
                   $"{project.Name}'s program would be written outside {directory}: \"{fileName}\" isn't a file name.");
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
