using System.Collections.Immutable;

namespace HexIDE.TwinBasic;

/// <summary>
/// Explicit read-only project lifecycle. Calls must be serialized by the owner; this is not a concurrent
/// collection. I/O is synchronous, so a UI integration must schedule these operations off its UI thread.
/// It has no watchers, extraction directory, sidecars, compiler processes or write operation.
/// </summary>
public sealed class ArchiveProjectWorkspace
{
    private readonly Dictionary<string, ArchiveProjectSnapshot> projects = new(ArchiveEntryIdentity.ContainerPathComparer);

    public ImmutableArray<ArchiveProjectSnapshot> Projects => [.. projects.Values];

    /// <summary>An already-open path returns its current snapshot; only explicit Reload re-reads it.</summary>
    public ArchiveProjectSnapshot Open(string path, TwinBasicReadLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        var fullPath = FullPath(path);
        cancellationToken.ThrowIfCancellationRequested();
        if (projects.TryGetValue(fullPath, out var existing)) return existing;
        var snapshot = Load(fullPath, limits, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        projects.Add(fullPath, snapshot);
        return snapshot;
    }

    /// <summary>Publishes a complete replacement only after validation. On failure the old snapshot,
    /// its document objects and its bytes remain available. Existing identities locate retained names.</summary>
    public ArchiveProjectSnapshot Reload(string path, TwinBasicReadLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        var fullPath = FullPath(path);
        cancellationToken.ThrowIfCancellationRequested();
        if (!projects.ContainsKey(fullPath)) throw new InvalidOperationException("The archive project is not open.");
        var replacement = Load(fullPath, limits, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        projects[fullPath] = replacement;
        return replacement;
    }

    public bool Close(string path) => projects.Remove(FullPath(path));
    public void CloseAll() => projects.Clear();

    private static ArchiveProjectSnapshot Load(string fullPath, TwinBasicReadLimits? limits, CancellationToken token)
    {
        using var input = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ArchiveProjectSnapshot.Read(input, fullPath, limits, token);
    }

    private static string FullPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }
}
