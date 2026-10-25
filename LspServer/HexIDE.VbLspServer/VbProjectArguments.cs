// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// The Conditional Compilation Arguments of the project a document belongs to, read from the .vbp that lists it (#1112).

using System.Text;
using HexIDE.Language;

namespace HexIDE.VbLspServer;

/// <summary>
/// Finds the <c>.vbp</c> that lists a document and reads its <c>CondComp</c> line, so the server chooses the branches the
/// project's build would. It reads the project from disk rather than taking arguments from the client, so it serves every
/// client alike, and so a group of projects, each with its own arguments, needs nothing more.
/// <para>
/// A project is looked for beside the document and in up to <see cref="Levels"/> folders above it. A document no project
/// lists, or that two list with different arguments, or whose project's <c>CondComp</c> vb6.exe would refuse, has
/// <see cref="VbCompilationContext.NoProject"/>: only the built-ins are then known.
/// </para>
/// <para>
/// A <c>.vbp</c> is a Windows-native format: its item paths are backslash-separated on every host, so they are split on the
/// backslash and walked a segment at a time from the project's folder, never handed to <see cref="System.IO.Path"/> whole,
/// which on Linux would read <c>sub\Mod1.bas</c> as one file name.
/// </para>
/// </summary>
internal sealed class VbProjectArguments
{
    /// <summary>How many folders above the document's are searched for a project.</summary>
    internal const int Levels = 3;

    private static readonly string[] ItemKeys = ["Module", "Class", "Form", "UserControl", "PropertyPage", "UserDocument", "Designer"];

    private readonly Dictionary<string, (DateTime Written, long Length, Project Project)> cache = new(StringComparer.Ordinal);

    private sealed record Project(IReadOnlyList<string> Items, IReadOnlyDictionary<string, short>? Arguments,
        IReadOnlyCollection<string> Names);

    /// <summary>The context for the document at <paramref name="uri"/>, a <c>file:</c> URI; anything else has no project.</summary>
    public VbCompilationContext For(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || !parsed.IsFile)
            return VbCompilationContext.NoProject;
        return ForPath(parsed.LocalPath);
    }

    /// <summary>The context for the document at the host path <paramref name="documentPath"/>.</summary>
    public VbCompilationContext ForPath(string documentPath)
    {
        var document = Path.GetFullPath(documentPath);
        var folder = Path.GetDirectoryName(document);
        for (var level = 0; level <= Levels && folder is not null; level++, folder = Path.GetDirectoryName(folder))
        {
            IReadOnlyDictionary<string, short>? found = null;
            var listed = 0;
            var agree = true;
            IReadOnlyCollection<string> names = [];
            foreach (var projectPath in ProjectsIn(folder))
            {
                if (Read(projectPath) is not { } project || !project.Items.Any(i => SamePath(i, document)))
                    continue;
                listed++;
                names = project.Names;
                if (project.Arguments is null || (found is not null && !Same(found, project.Arguments)))
                    agree = false;
                found ??= project.Arguments;
            }
            if (listed > 0)
                return agree && found is not null
                    ? new VbCompilationContext(found) { BaselineNames = listed == 1 ? names : [] }
                    : VbCompilationContext.NoProject;
        }
        return VbCompilationContext.NoProject;
    }

    private static IEnumerable<string> ProjectsIn(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*.vbp").ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // The project, read once for each version of the file on disk.
    private Project? Read(string projectPath)
    {
        try
        {
            var info = new FileInfo(projectPath);
            if (cache.TryGetValue(projectPath, out var cached) && cached.Written == info.LastWriteTimeUtc && cached.Length == info.Length)
                return cached.Project;
            var project = Parse(projectPath, File.ReadAllText(projectPath, Encoding.Latin1));
            cache[projectPath] = (info.LastWriteTimeUtc, info.Length, project);
            return project;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Each item's host path, and the arguments, or null where vb6.exe would refuse the CondComp line.
    private static Project Parse(string projectPath, string text)
    {
        var folder = Path.GetDirectoryName(projectPath)!;
        var items = new List<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyDictionary<string, short>? arguments = ConditionalCompilation.TryParseArguments(null, out var none) ? none : null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith('['))
                break;   // the add-in sections that follow the project's own keys
            var equals = line.IndexOf('=');
            if (equals <= 0)
                continue;
            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (key.Equals("CondComp", StringComparison.OrdinalIgnoreCase))
                arguments = ConditionalCompilation.TryParseArguments(Unquoted(value), out var parsed) ? parsed : null;
            else if (ItemKeys.Any(k => k.Equals(key, StringComparison.OrdinalIgnoreCase)))
            {
                // Module= and Class= name the item before a semicolon; the others give the path alone.
                var semicolon = value.IndexOf(';');
                if (semicolon > 0 && (key.Equals("Module", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("Class", StringComparison.OrdinalIgnoreCase)))
                    names.Add(value[..semicolon].Trim());
                var relative = (semicolon >= 0 && (key.Equals("Module", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("Class", StringComparison.OrdinalIgnoreCase)) ? value[(semicolon + 1)..] : value).Trim();
                if (Resolve(folder, relative) is { } resolved)
                    items.Add(resolved);
            }
        }
        return new Project(items, arguments, names);
    }

    private static string Unquoted(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;

    /// <summary>
    /// A <c>.vbp</c> item path, backslash-separated as VB6 writes it, resolved against the project's host folder one segment
    /// at a time. A rooted Windows path, such as <c>C:\x.bas</c>, has no meaning on another host and resolves to nothing there.
    /// </summary>
    internal static string? Resolve(string projectFolder, string relative)
    {
        if (relative.Length == 0)
            return null;
        var rooted = relative.Length >= 2 && relative[1] == ':' || relative.StartsWith('\\');
        if (rooted)
            return OperatingSystem.IsWindows() ? Path.GetFullPath(relative) : null;
        var path = projectFolder;
        foreach (var segment in relative.Split('\\', '/'))
        {
            if (segment is "" or ".")
                continue;
            if (segment == "..")
            {
                path = Path.GetDirectoryName(path);
                if (path is null)
                    return null;
                continue;
            }
            path = Path.Combine(path, segment);
        }
        return Path.GetFullPath(path);
    }

    // VB6 compares names without case, and a project moved from Windows may not match the files' case on another host.
    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool Same(IReadOnlyDictionary<string, short> a, IReadOnlyDictionary<string, short> b) =>
        a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var v) && v == p.Value);
}
