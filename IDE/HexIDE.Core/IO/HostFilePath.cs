using System;
using System.IO;

namespace HexIDE.IO;

/// <summary>Lexical file path identity on this host; does not resolve symbolic links.</summary>
public static class HostFilePath
{
    /// <summary>The case rule for paths already normalized with <see cref="Path.GetFullPath(string)"/>.</summary>
    public static StringComparer Comparer { get; } =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static bool Equals(string left, string right, bool? ignoreCase = null) =>
        (ignoreCase switch
        {
            true => StringComparer.OrdinalIgnoreCase,
            false => StringComparer.Ordinal,
            null => Comparer,
        }).Equals(Path.GetFullPath(left), Path.GetFullPath(right));
}
