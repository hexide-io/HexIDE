using HexIDE.IDE;

namespace HexIDE.TwinBasic;

public sealed record ArchiveFolderResult(string Directory, bool Changed);

/// <summary>Explicit, one-way projection of a saved archive.</summary>
public sealed class ArchiveSourceFolder
{
    internal Action<string>? Checkpoint { get; set; }
    internal DistributionIdentity Identity { get; init; } = Distribution.Current;
    internal string? ChosenRoot { get; set; }

    public ArchiveFolderResult Synchronize(string containerPath, CancellationToken cancellationToken = default)
    {
        if (!containerPath.EndsWith(".twinproj", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A saved .twinproj project is required.", nameof(containerPath));
        var engine = new CompoundSourceFolder(TwinBasicFolderFormat.Instance)
        {
            Checkpoint = point => Checkpoint?.Invoke(point), Identity = Identity, ChosenRoot = ChosenRoot
        };
        var result = engine.Synchronize(containerPath, cancellationToken);
        return new(result.Directory, result.Changed);
    }
}
