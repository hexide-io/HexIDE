using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace HexIDE.TwinBasic;

public abstract class ArchiveProjectEntry
{
    private protected ArchiveProjectEntry(ArchiveEntryIdentity identity, TwinBasicEntry source)
    {
        Identity = identity;
        Source = source;
    }

    public ArchiveEntryIdentity Identity { get; }
    public TwinBasicEntry Source { get; }
    public string Name => Source.Name;
}

public sealed class ArchiveProjectFolder : ArchiveProjectEntry
{
    internal ArchiveProjectFolder(ArchiveEntryIdentity identity, TwinBasicDirectory source,
        ImmutableArray<ArchiveProjectEntry> children) : base(identity, source) => Children = children;

    public ImmutableArray<ArchiveProjectEntry> Children { get; }
}

public enum ArchiveTextStatus { Available, UnsupportedType, InvalidUtf8, TooLarge }

/// <summary>Only Available has Text. A failed preview never substitutes empty text for the content.</summary>
public sealed record ArchiveText(ArchiveTextStatus Status, string? Text);

public sealed class ArchiveProjectDocument : ArchiveProjectEntry
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    internal ArchiveProjectDocument(ArchiveEntryIdentity identity, TwinBasicFile file) : base(identity, file)
        => File = file;

    public TwinBasicFile File { get; }
    public bool IsReadOnly => true;

    /// <summary>
    /// A bounded text preview, preserving line endings. Raw bytes (including any BOM) remain in File.
    /// Known text types only; designer files and resources are never guessed to be text from their bytes.
    /// No JSON parsing, VB6 header removal, rewriting or language-service attachment takes place here.
    /// </summary>
    public ArchiveText ReadText(int maxBytes = 4 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        // Use the archive name, never System.IO.Path: a backslash is an ordinary archive character.
        var dot = Name.LastIndexOf('.');
        var extension = dot < 0 ? "" : Name[dot..].ToLowerInvariant();
        if (File.Category != 4 && Name != ".meta" &&
            extension is not (".twin" or ".txt" or ".md" or ".json" or ".xml"))
            return new(ArchiveTextStatus.UnsupportedType, null);
        if (File.Content.Length > maxBytes) return new(ArchiveTextStatus.TooLarge, null);
        var bytes = File.Content.AsSpan();
        if (bytes.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) bytes = bytes[3..];
        try { return new(ArchiveTextStatus.Available, Utf8.GetString(bytes)); }
        catch (DecoderFallbackException) { return new(ArchiveTextStatus.InvalidUtf8, null); }
    }
}

/// <summary>
/// A complete, immutable project view. Content and metadata come from one read, never from live member
/// paths. Physical folder projection, editor command guards and compiler routing belong to later consumers.
/// </summary>
public sealed class ArchiveProjectSnapshot
{
    private readonly ImmutableDictionary<ArchiveEntryIdentity, ArchiveProjectEntry> entries;

    private ArchiveProjectSnapshot(string containerPath, TwinBasicContainer container,
        ArchiveProjectFolder root, ImmutableDictionary<ArchiveEntryIdentity, ArchiveProjectEntry> entries)
    {
        ContainerPath = containerPath;
        Container = container;
        Root = root;
        this.entries = entries;
    }

    public string ContainerPath { get; }
    public string Name => Root.Name;
    public TwinBasicContainer Container { get; }
    public ArchiveProjectFolder Root { get; }
    public bool IsReadOnly => true;
    public int EntryCount => entries.Count;

    public bool TryGetEntry(ArchiveEntryIdentity identity, [NotNullWhen(true)] out ArchiveProjectEntry? entry) =>
        entries.TryGetValue(identity, out entry);

    /// <summary>Reads from the current stream position, leaving it open. Container path is an origin
    /// identity only; member names are never resolved against it. Symlink aliases are not canonicalized.</summary>
    public static ArchiveProjectSnapshot Read(Stream input, string containerPath,
        TwinBasicReadLimits? limits = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerPath);
        var fullPath = Path.GetFullPath(containerPath);
        var container = TwinBasicContainerReader.Read(input, limits, cancellationToken);
        var entries = ImmutableDictionary.CreateBuilder<ArchiveEntryIdentity, ArchiveProjectEntry>();
        ArchiveProjectEntry Build(TwinBasicEntry source, ImmutableArray<string> segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var identity = new ArchiveEntryIdentity(fullPath, segments);
            ArchiveProjectEntry result;
            if (source is TwinBasicDirectory directory)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                var children = ImmutableArray.CreateBuilder<ArchiveProjectEntry>(directory.Children.Length);
                foreach (var child in directory.Children)
                {
                    if (!names.Add(child.Name)) throw new ArchiveProjectAmbiguityException(identity, child.Name);
                    children.Add(Build(child, segments.Add(child.Name)));
                }
                result = new ArchiveProjectFolder(identity, directory, children.MoveToImmutable());
            }
            else result = new ArchiveProjectDocument(identity, (TwinBasicFile)source);
            entries.Add(identity, result);
            return result;
        }

        var root = (ArchiveProjectFolder)Build(container.Root, []);
        cancellationToken.ThrowIfCancellationRequested();
        return new(fullPath, container, root, entries.ToImmutable());
    }
}

/// <summary>The raw reader can inspect this container, but it cannot provide unambiguous named documents.</summary>
public sealed class ArchiveProjectAmbiguityException(ArchiveEntryIdentity parent, string name)
    : IOException($"Duplicate archive entry name '{name}' in '{parent.ContainerPath}'.")
{
    public ArchiveEntryIdentity Parent { get; } = parent;
    public string EntryName { get; } = name;
}
