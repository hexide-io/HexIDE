using System.Collections.Immutable;

namespace HexIDE.TwinBasic;

/// <summary>
/// An archive address, not a filesystem path or language-server URI. Names are compared as distinct
/// ordinal segments, so a name containing a slash cannot alias two nested names. The root has no segments.
/// Stable across reloads of the same container; it does not follow archive member or container renames.
/// </summary>
public sealed class ArchiveEntryIdentity : IEquatable<ArchiveEntryIdentity>
{
    public static StringComparer ContainerPathComparer { get; } =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal ArchiveEntryIdentity(string containerPath, ImmutableArray<string> segments)
    {
        ContainerPath = containerPath;
        Segments = segments;
    }

    public string ContainerPath { get; }
    public ImmutableArray<string> Segments { get; }

    public bool Equals(ArchiveEntryIdentity? other) => other is not null &&
        ContainerPathComparer.Equals(ContainerPath, other.ContainerPath) &&
        Segments.AsSpan().SequenceEqual(other.Segments.AsSpan());

    public override bool Equals(object? obj) => obj is ArchiveEntryIdentity other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ContainerPath, ContainerPathComparer);
        foreach (var name in Segments) hash.Add(name, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}
