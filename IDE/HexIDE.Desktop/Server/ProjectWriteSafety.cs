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

    public static string? Prepare(DocumentIdentity document, IProjectFileFormat format, out string note, WriteAttempt? attempt = null)
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
                var created = false;
                try
                {
                    using var input = File.OpenRead(candidate);
                    using var output = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    created = true;
                    input.CopyTo(output);
                    output.Flush(flushToDisk: true);
                }
                catch
                {
                    if (created) File.Delete(backup);
                    throw;
                }
                attempt?.Copies.Add((candidate, backup));
                note += $" Preserved the previous bytes of '{candidate}' at '{backup}'. To recover, copy that file back to '{candidate}'.";
                if (attempt is null)
                    Prune(candidate);
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Refused($"Refused '{path}': could not protect the write: {ex.Message}. Nothing was changed." + (attempt is null ? note : attempt.FailureNote));
        }
    }

    private static void Prune(string path)
    {
        foreach (var old in new DirectoryInfo(Path.GetDirectoryName(path)!)
                     .GetFiles(Path.GetFileName(path) + ".hexide-recovery-*")
                     .OrderByDescending(file => file.LastWriteTimeUtc).Skip(RecoveryFilesKept))
            old.Delete();
    }

    internal sealed class WriteAttempt : IDisposable
    {
        internal List<(string Path, string Backup)> Copies { get; } = [];
        public bool Written { get; set; }

        public string FailureNote
        {
            get
            {
                Dispose();
                return string.Concat(Copies.Where(copy => File.Exists(copy.Backup)).Select(copy =>
                    $" Preserved the previous bytes of '{copy.Path}' at '{copy.Backup}'. To recover, copy that file back to '{copy.Path}'."));
            }
        }

        public void Dispose()
        {
            foreach (var (path, backup) in Copies)
            {
                // Tidying is best effort. It runs after the write, so a locked or unreadable file must not turn a
                // finished write into a failed call. A copy that can't be removed is kept, and FailureNote names it.
                try
                {
                    if (!File.Exists(backup)) continue;
                    // A save may throw after replacing one of the files. Keep that file's previous bytes.
                    if (!Written && File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(File.ReadAllBytes(backup)))
                        File.Delete(backup);
                    else
                        Prune(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
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
