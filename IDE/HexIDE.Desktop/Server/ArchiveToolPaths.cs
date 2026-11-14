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

    public static bool ProjectMatches(ArchiveProjectSnapshot archive, string? project) =>
        project is null
        || ArchiveEntryIdentity.ContainerPathComparer.Equals(archive.ContainerPath, project)
        || string.Equals(Path.GetFileName(archive.ContainerPath), project, StringComparison.OrdinalIgnoreCase)
        || string.Equals(archive.Name, project, StringComparison.OrdinalIgnoreCase);
}
