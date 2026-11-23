namespace HexIDE.Tests.Infrastructure;

internal static class ResolvedTemp
{
    public static string Root { get; } = Resolve(Path.GetTempPath());

    public static string CreateDirectory(string prefix) =>
        Directory.CreateDirectory(Path.Combine(Root, prefix + Guid.NewGuid())).FullName;

    public static string Resolve(string path) => OperatingSystem.IsWindows() ? path : Resolve(path, 0);

    // Mirrors WorkspaceArtifactService.Resolve, including links inside a link's final target.
    private static string Resolve(string path, int depth)
    {
        if (depth > 40) throw new IOException("Too many levels of links.");
        var current = Path.GetPathRoot(path)!;
        foreach (var part in path[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is not null)
                current = Resolve(info.ResolveLinkTarget(true)?.FullName ?? throw new IOException("Unresolved link."),
                    depth + 1);
        }
        return current;
    }
}
