using System;
using System.IO;

namespace HexIDE.IO;

/// <summary>
/// A file a project names, joined onto the folder it must stay in (#5622): a member's file in Make's staging folder, or a
/// made program in its project's output folder. The name comes from the project, so a project from elsewhere chooses it.
/// </summary>
internal static class ConfinedPath
{
    /// <summary>
    /// Whether <paramref name="name"/> is a plain file name: not empty, not <c>.</c> or <c>..</c>, not rooted, and holding
    /// no <c>\</c>, <c>/</c> or <c>:</c>.
    /// </summary>
    /// <remarks>
    /// A backslash is refused on every host, not only on Windows: VB6's files are Windows-native, so a project carried to
    /// Windows reads one as a separator. A colon names a drive (<c>C:x</c>) or a stream on Windows.
    /// </remarks>
    internal static bool IsFileName(string? name) =>
        !string.IsNullOrEmpty(name)
        && name is not "." and not ".."
        && name.IndexOfAny(['\\', '/', ':']) < 0
        && !Path.IsPathRooted(name);

    /// <summary>
    /// <paramref name="fileName"/> in <paramref name="folder"/>, as a full path, or null when <paramref name="fileName"/>
    /// isn't a plain file name (<see cref="IsFileName"/>) or the full path would land anywhere but directly in the folder.
    /// </summary>
    internal static string? Join(string folder, string fileName)
    {
        if (!IsFileName(fileName))
            return null;
        var inside = Path.GetFullPath(folder);
        var full = Path.GetFullPath(Path.Join(inside, fileName));
        return Path.GetDirectoryName(full) is { } parent
               && HostFilePath.Comparer.Equals(Path.TrimEndingDirectorySeparator(parent), Path.TrimEndingDirectorySeparator(inside))
            ? full
            : null;
    }
}
