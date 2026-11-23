using System.Text.Json;
using System.Text.RegularExpressions;
using HexIDE.IO;

namespace HexIDE.IDE;

/// <summary>Shared project-local names and discovery across distributions (#5149).</summary>
internal static partial class ProjectLocalState
{
    internal const string StockFolder = ".hexide";
    internal const string StockSidecar = ".user.hexproj";
    internal const string FolderMarker = "project-state.json";
    internal const string FolderKind = "hexide.project-state";
    internal const string SidecarKind = "hexide.user-sidecar";
    private const int MaximumBytes = 4 * 1024 * 1024;

    [GeneratedRegex(@"\A\.[a-z0-9][a-z0-9._-]{0,31}\z", RegexOptions.CultureInvariant)]
    private static partial Regex FolderPattern();
    [GeneratedRegex(@"\A\.user\.[a-z0-9-]{1,32}\z", RegexOptions.CultureInvariant)]
    private static partial Regex SidecarPattern();

    internal static bool ValidFolder(string name) => name is not "." and not ".."
        && FolderPattern().IsMatch(name) && !name.EndsWith('.') && !name.Contains("..", StringComparison.Ordinal)
        && !name.StartsWith(".hexide-", StringComparison.OrdinalIgnoreCase)
        && !new[] { ".git", ".svn", ".hg", ".bzr" }.Contains(name, StringComparer.OrdinalIgnoreCase);
    internal static bool ValidSidecar(string name) => SidecarPattern().IsMatch(name);

    internal static string OwnPath(string project, bool folder, DistributionIdentity? identity = null)
    {
        identity ??= Distribution.Current;
        return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(project))!,
            folder ? identity.ProjectFolder : Path.GetFileNameWithoutExtension(project) + identity.UserSidecar);
    }

    internal static IReadOnlyList<string> Find(string project, bool folder, DistributionIdentity? identity = null)
    {
        identity ??= Distribution.Current;
        var directory = Path.GetDirectoryName(Path.GetFullPath(project))!;
        if (!Directory.Exists(directory)) return [];
        var stem = Path.GetFileNameWithoutExtension(project);
        var own = folder ? identity.ProjectFolder : identity.UserSidecar;
        var stock = folder ? StockFolder : StockSidecar;
        var found = new List<string>();
        foreach (var path in folder ? Directory.EnumerateDirectories(directory) : Directory.EnumerateFiles(directory))
        {
            if (IsLink(path)) continue;
            var name = Path.GetFileName(path);
            if (!folder)
            {
                if (!name.StartsWith(stem, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) continue;
                name = name[stem.Length..];
            }
            var known = HostFilePath.Comparer.Equals(name, own) || HostFilePath.Comparer.Equals(name, stock);
            if (!known && !(folder ? ValidFolder(name) : ValidSidecar(name))) continue;
            if (known || HasMarker(folder ? Path.Combine(path, FolderMarker) : path, folder))
                found.Add(path);
        }
        found.Sort(HostFilePath.Comparer);
        return found;
    }

    private static bool HasMarker(string path, bool folder)
    {
        try
        {
            if (IsLink(path)) return false;
            var length = new FileInfo(path).Length;
            if (length is < 2 or > MaximumBytes) return false;
            using var stream = File.OpenRead(path);
            if (stream.Length > MaximumBytes) return false;
            var bytes = new byte[checked((int)length)];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1) return false;
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
            return json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("kind", out var kind)
                && kind.ValueKind == JsonValueKind.String && kind.GetString() == (folder ? FolderKind : SidecarKind)
                && (!folder || (json.RootElement.TryGetProperty("version", out var version)
                    && version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var value) && value == 1));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }

    internal static bool IsLink(string path) => new FileInfo(path).LinkTarget != null
        || (File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    internal static void EnsureRegularPath(string path)
    {
        if (IsLink(path)) throw new ProjectLocalStatePathException(path);
    }

    internal static string Resolve(string project, bool folder, string? choice = null, DistributionIdentity? identity = null)
    {
        var found = Find(project, folder, identity);
        string path;
        if (choice != null)
        {
            path = found.FirstOrDefault(p => HostFilePath.Equals(p, choice))
                ?? throw new ProjectLocalStatePathException(choice);
        }
        else if (found.Count > 1) throw new ProjectLocalStateConflict(found);
        else path = found.Count == 1 ? found[0] : OwnPath(project, folder, identity);
        EnsureRegularPath(path);
        return path;
    }

    private sealed record FolderStateMarker(string kind, int version);

    internal static void WriteFolderMarker(string root)
    {
        var path = Path.Combine(root, FolderMarker);
        EnsureRegularPath(root);
        EnsureRegularPath(path);
        Directory.CreateDirectory(root);
        AtomicFileWriter.WriteRecoverableText(path, JsonSerializer.Serialize(new FolderStateMarker(FolderKind, 1)));
    }
}

internal sealed class ProjectLocalStateConflict(IReadOnlyList<string> candidates)
    : IOException("Multiple project-local state locations were found: " + string.Join(", ", candidates))
{
    internal IReadOnlyList<string> Candidates { get; } = candidates;
}

internal sealed class ProjectLocalStatePathException(string statePath)
    : IOException($"Project-local state is unavailable or uses a link or reparse point: {statePath}")
{
    internal string StatePath { get; } = statePath;
}
