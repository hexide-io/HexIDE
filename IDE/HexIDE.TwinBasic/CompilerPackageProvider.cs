namespace HexIDE.TwinBasic;

/// <summary>
/// Resolves an opaque compiler package identifier to raw package bytes. The caller owns the returned
/// stream. Implementations must honor cancellation and return promptly from stream disposal.
/// No identifier is implicitly interpreted as a host path, URL or executable command.
/// </summary>
public interface ICompilerPackageProvider
{
    ValueTask<Stream?> OpenReadAsync(string packageId, CancellationToken cancellationToken = default);
}

/// <summary>Exact, case-sensitive allowlist of package identifiers to explicitly selected absolute files.</summary>
public sealed class CompilerPackageFileProvider : ICompilerPackageProvider
{
    private readonly Dictionary<string, string> files = new(StringComparer.Ordinal);

    public CompilerPackageFileProvider(IReadOnlyDictionary<string, string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        foreach (var (id, path) in files)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Contains('\0') || !Path.IsPathFullyQualified(path))
                throw new ArgumentException("Package mappings require nonempty identifiers and absolute paths.", nameof(files));
            this.files.Add(id, Path.GetFullPath(path));
        }
    }

    public ValueTask<Stream?> OpenReadAsync(string packageId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<Stream?>(files.TryGetValue(packageId, out var path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan)
            : null);
    }
}
