using System;
using System.IO;

namespace HexIDE.Debugging;

/// <summary>Matches adapter source files, including paths through linked parent directories.</summary>
public static class SourcePaths
{
    public static bool SameSourcePath(string uri, string? path)
    {
        if (path is null) return false;
        var actual = new Uri(uri).LocalPath;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(actual, path, comparison)) return true;
        try { return string.Equals(Resolve(actual), Resolve(path), comparison); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }

        // An adapter can canonicalize a linked working directory even when the source itself is not on disk.
        static string Resolve(string value)
        {
            var full = Path.GetFullPath(value);
            var parent = Path.GetDirectoryName(full);
            if (parent is null) return full;
            var resolved = Path.Combine(Resolve(parent), Path.GetFileName(full));
            FileSystemInfo info = Directory.Exists(resolved) ? new DirectoryInfo(resolved) : new FileInfo(resolved);
            var target = info.Exists ? info.ResolveLinkTarget(true) : null;
            return target is null ? resolved : Resolve(target.FullName);
        }
    }

    public static bool SameSourcePath(Uri first, Uri second) =>
        first.IsFile && second.IsFile
            ? SameSourcePath(first.AbsoluteUri, second.LocalPath)
            : string.Equals(first.OriginalString, second.OriginalString,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
