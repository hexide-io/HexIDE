using HexIDE.IDE;
using System.Security.Cryptography;
using System.Text.Json;

namespace HexIDE.TwinBasic;

internal sealed class TwinBasicFolderFormat : ICompoundFolderFormat
{
    internal static TwinBasicFolderFormat Instance { get; } = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public CompoundFolderSnapshot Read(byte[] bytes, string source, CancellationToken cancellationToken)
    {
        using var input = new MemoryStream(bytes, writable: false);
        var snapshot = ArchiveProjectSnapshot.Read(input, source, cancellationToken: cancellationToken);
        var manifest = CreateManifest(snapshot, Hash(bytes));
        var entries = new List<CompoundFolderItem>();
        void Add(ArchiveProjectEntry entry, string[] path)
        {
            entries.Add(new(path, (entry as ArchiveProjectDocument)?.File.Content.ToArray()));
            if (entry is ArchiveProjectFolder folder)
                foreach (var child in folder.Children) Add(child, [.. path, child.Name]);
        }
        Add(snapshot.Root, []);
        return new(JsonSerializer.SerializeToUtf8Bytes(manifest, Json), entries);
    }

    public void ValidateManifest(byte[] bytes)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<Manifest>(bytes)
                ?? throw new IOException("Missing synchronization metadata.");
            if (manifest.Format == 1 && manifest.Entries is { Length: >= 1 and <= 100_000 } &&
                manifest.Entries.Any(e => e == null || e.Revisions == null || e.Directory && e.Revisions.Length != 0))
                throw new IOException("Invalid manifest entry.");
        }
        catch (JsonException ex) { throw new IOException("Invalid synchronization metadata.", ex); }
    }

    private static Manifest CreateManifest(ArchiveProjectSnapshot snapshot, string hash)
    {
        var entries = new List<Entry>();
        void Add(ArchiveProjectEntry entry, string[] path)
        {
            var file = entry as ArchiveProjectDocument;
            entries.Add(new(path, entry.Name, file == null, entry.Source.Category, entry.Source.Flags,
                entry.Source.Revision, file?.File.Content.Length ?? 0,
                file == null ? null : Hash(file.File.Content.AsSpan()), file?.File.Revisions.ToArray() ?? []));
            if (entry is ArchiveProjectFolder folder)
                foreach (var child in folder.Children) Add(child, [.. path, child.Name]);
        }
        Add(snapshot.Root, []);
        return new(1, snapshot.Container.Version, hash, entries.ToArray());
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed record Manifest(int Format, short ContainerVersion, string ContainerHash, Entry[] Entries);
    private sealed record Entry(string[] Path, string Name, bool Directory, byte Category, uint Flags, ulong Revision,
        int Length, string? Hash, uint[] Revisions);
}
