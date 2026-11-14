using HexIDE.TwinBasic;

namespace HexIDE.Desktop.Server;

/// <summary>Automation paths inside a container, not filesystem paths or language-server URIs.</summary>
internal static class ArchiveToolPaths
{
    public static string PathOf(ArchiveProjectEntry entry) =>
        "/" + string.Join("/", entry.Identity.Segments.Select(Uri.EscapeDataString));

    public static IEnumerable<ArchiveProjectEntry> Entries(ArchiveProjectFolder folder)
    {
        foreach (var entry in folder.Children)
        {
            yield return entry;
            if (entry is ArchiveProjectFolder child)
                foreach (var descendant in Entries(child))
                    yield return descendant;
        }
    }

    /// <summary>How many entries a miss names, across every open archive, before it counts the rest.</summary>
    public const int MissListLimit = 20;

    /// <summary>
    /// The open archives and the first <see cref="MissListLimit"/> of their entry paths, for a name that matched
    /// nothing. Bounded, so an archive of hundreds of sources does not flood the reply; get_project_info lists them all.
    /// </summary>
    public static string MissInventory(IReadOnlyList<ArchiveProjectSnapshot> archives)
    {
        if (archives.Count == 0) return "";
        var parts = new List<string>();
        var listed = 0;
        var rest = 0;
        foreach (var archive in archives)
        {
            var paths = Entries(archive.Root).Select(PathOf).ToArray();
            var take = Math.Min(paths.Length, MissListLimit - listed);
            listed += take;
            rest += paths.Length - take;
            parts.Add(take > 0 ? $"{archive.ContainerPath}: {string.Join(", ", paths.Take(take))}" : archive.ContainerPath);
        }
        var more = rest == 0 ? "" : $"; and {rest} more {(rest == 1 ? "entry" : "entries")}, which get_project_info lists";
        return $" Open archives: {string.Join("; ", parts)}{more}.";
    }

    public static bool ProjectMatches(ArchiveProjectSnapshot archive, string? project) =>
        project is null
        || ArchiveEntryIdentity.ContainerPathComparer.Equals(archive.ContainerPath, project)
        || string.Equals(Path.GetFileName(archive.ContainerPath), project, StringComparison.OrdinalIgnoreCase)
        || string.Equals(archive.Name, project, StringComparison.OrdinalIgnoreCase);
}
