using HexIDE.Runtime.ProjectElements;
using HexIDE.IDE;

namespace HexIDE.Desktop.Server;

internal static class ProjectWriteSafety
{
    private const int RecoveryFilesKept = 5;

    public static string? NameRefusal(string name) =>
        name.IndexOfAny(['/', '\\', ':']) >= 0 || name is "." or ".." || Path.IsPathRooted(name)
            ? $"Refused '{name}': use a document name from get_project_info, not a rooted or relative path. Nothing was changed."
            : null;

    public static string? Prepare(DocumentIdentity document, IProjectFileFormat format, out string note)
    {
        note = "";
        var path = document.AbsolutePath;
        if (path is null)
            path = ScriptedFileDialogs.NextSinglePath;
        // A cancel (including a multi-path answer) writes nothing.
        if (path is null)
            return null;
        string Refused(string reason) => reason + (document.AbsolutePath is null
            ? " The file-dialog answer is still pending; clear_file_dialog_answers discards it." : "");
        var root = document.Project.AbsolutePath is { } projectPath
            ? Path.GetDirectoryName(projectPath) : document.Project.WorkingDirectory;
        try
        {
            if (root is null)
                return Refused($"Refused '{path}': the project has no folder. Save Project in the IDE first. Nothing was changed.");
            var paths = new List<string> { path };
            if ((document.Form is not null || document.Module?.Kind is ModuleKind.UserControl or ModuleKind.PropertyPage)
                && (document.Project.FormFormat ?? format).CompanionPath(path) is { } companion)
                paths.Add(companion);
            foreach (var candidate in paths)
                if (Refusal(root, candidate) is { } refusal)
                    return Refused(refusal);
            foreach (var candidate in paths)
            {
                if (!File.Exists(candidate)) continue;
                var backup = candidate + ".hexide-recovery-" + Guid.NewGuid().ToString("N");
                using (var input = File.OpenRead(candidate))
                using (var output = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    input.CopyTo(output);
                    output.Flush(flushToDisk: true);
                }
                note += $" Preserved the previous bytes of '{candidate}' at '{backup}'. To recover, copy that file back to '{candidate}'.";
                // An automated session writes constantly, and these sit in the user's project folder.
                foreach (var old in new DirectoryInfo(Path.GetDirectoryName(candidate)!)
                             .GetFiles(Path.GetFileName(candidate) + ".hexide-recovery-*")
                             .OrderByDescending(file => file.LastWriteTimeUtc).Skip(RecoveryFilesKept))
                    old.Delete();
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Refused($"Refused '{path}': could not protect the write: {ex.Message}. Nothing was changed." + note);
        }
    }

    private static string? Refusal(string root, string path)
    {
        if (path.Replace('\\', '/').Split('/').Contains(".."))
            return $"Refused '{path}': parent traversal is not allowed. Nothing was changed.";
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(fullRoot, fullPath);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return $"Refused '{path}': outside project folder '{fullRoot}'. Nothing was changed.";
        // Refuse links inside the project, dangling ones included, before reading or writing anything. A link above the
        // project folder only leads to the project: macOS's /var is one, and so is a redirected Documents folder.
        var part = fullPath;
        foreach (var _ in relative.Split(Path.DirectorySeparatorChar))
        {
            if ((File.Exists(part) || Directory.Exists(part) || new FileInfo(part).LinkTarget is not null)
                && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                return $"Refused '{path}': linked path '{part}' is not allowed. Nothing was changed.";
            part = Path.GetDirectoryName(part)!;
        }
        return null;
    }
}
