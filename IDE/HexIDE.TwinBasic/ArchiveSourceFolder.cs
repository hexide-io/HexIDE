using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HexIDE.TwinBasic;

public sealed record ArchiveFolderResult(string Directory, bool Changed);

/// <summary>Explicit, one-way projection of a saved archive. All previous trees are retained for recovery.</summary>
public sealed class ArchiveSourceFolder
{
    private const int Limit = 64 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    internal Action<string>? Checkpoint { get; set; }

    public ArchiveFolderResult Synchronize(string containerPath, CancellationToken cancellationToken = default)
    {
        var requested = Path.GetFullPath(containerPath);
        var source = Path.Combine(ResolveProjectDirectory(Path.GetDirectoryName(requested)!), Path.GetFileName(requested));
        var name = Path.GetFileName(source);
        ValidateName(name);
        ValidateName(name + ".json");
        if (!name.EndsWith(".twinproj", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A saved .twinproj project is required.", nameof(containerPath));
        var bytes = ReadBounded(source);
        using var input = new MemoryStream(bytes, writable: false);
        var snapshot = ArchiveProjectSnapshot.Read(input, source, cancellationToken: cancellationToken);
        var manifest = CreateManifest(snapshot, Hash(bytes));
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, Json);
        if (manifestBytes.Length > Limit) throw new IOException("The folder manifest exceeds the synchronization byte limit.");
        var paths = new Locations(source);
        CheckAncestors(paths.Control);
        CheckAncestors(paths.Target);
        CheckAncestors(paths.Manifest);
        ValidateAttributes(paths);
        cancellationToken.ThrowIfCancellationRequested();

        // An existing tree is never adopted merely because its name looks right. A clone can be
        // adopted only when both its tracked manifest and its entire tree match this saved archive.
        if (!Exists(paths.Journal) && !File.Exists(paths.Baseline) && (Exists(paths.Target) || Exists(paths.Manifest)))
        {
            if (!File.Exists(paths.Manifest) || !ReadBounded(paths.Manifest).SequenceEqual(manifestBytes))
                throw Conflict("The folder has no matching synchronization baseline", paths.Target);
            VerifyTree(paths.Target, manifest);
        }

        const string ownership = "HexIDE archive source synchronization v1\n";
        var owner = Path.Combine(paths.Control, "owner");
        if (Exists(paths.Control))
        {
            if (!File.Exists(owner) || !ReadBounded(owner).SequenceEqual(Encoding.UTF8.GetBytes(ownership)))
                throw Conflict("The synchronization state directory is not owned by HexIDE", paths.Control);
        }
        else
        {
            Directory.CreateDirectory(paths.Control);
            WriteNew(owner, Encoding.UTF8.GetBytes(ownership));
        }
        CheckAncestors(paths.Control);
        var lockPath = Path.Combine(paths.Control, "lock");
        CheckAncestors(lockPath);
        using var lease = new FileStream(lockPath, FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        var ignore = Path.Combine(paths.Control, ".gitignore");
        if (!Exists(ignore)) WriteNew(ignore, "*\n"u8.ToArray());
        else if (!ReadBounded(ignore).AsSpan().SequenceEqual("*\n"u8))
            throw Conflict("The local synchronization ignore rule was changed", ignore);
        Recover(paths);
        byte[]? oldBytes = null;
        Manifest? old = null;
        if (File.Exists(paths.Baseline))
        {
            oldBytes = ReadBounded(paths.Baseline);
            old = ParseManifest(oldBytes);
            if (!File.Exists(paths.Manifest) || !ReadBounded(paths.Manifest).SequenceEqual(oldBytes))
                throw Conflict("The tracked manifest was changed or removed", paths.Manifest);
            VerifyTree(paths.Target, old);
        }
        else if (Exists(paths.Target))
        {
            // Recheck after obtaining the lease.
            if (!ReadBounded(paths.Manifest).SequenceEqual(manifestBytes))
                throw Conflict("The tracked manifest changed", paths.Manifest);
            VerifyTree(paths.Target, manifest);
            EnsureAttributes(paths);
            WriteAtomic(paths, paths.Baseline, manifestBytes);
            return new(paths.Target, false);
        }
        else if (Exists(paths.Manifest)) throw Conflict("An unrelated manifest occupies the destination", paths.Manifest);

        if (oldBytes != null && oldBytes.SequenceEqual(manifestBytes))
        {
            EnsureAttributes(paths);
            return new(paths.Target, false);
        }
        var transaction = Guid.NewGuid().ToString("N");
        var work = Path.Combine(paths.Control, transaction);
        var next = Path.Combine(work, "next");
        Directory.CreateDirectory(next);
        void Stage(ArchiveProjectEntry entry, string destination)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry is ArchiveProjectFolder folder)
            {
                Directory.CreateDirectory(destination);
                foreach (var child in folder.Children) Stage(child, Path.Combine(destination, child.Name));
            }
            else
                WriteNew(destination, ((ArchiveProjectDocument)entry).File.Content.AsSpan());
        }
        Stage(snapshot.Root, next);
        WriteNew(Path.Combine(work, "new.json"), manifestBytes);
        if (oldBytes != null) WriteNew(Path.Combine(work, "old.json"), oldBytes);
        VerifyTree(next, manifest);
        Checkpoint?.Invoke("staged");
        cancellationToken.ThrowIfCancellationRequested();
        if (!Hash(ReadBounded(source)).Equals(manifest.ContainerHash, StringComparison.Ordinal))
            throw Conflict("The saved archive changed during synchronization; retry", source);
        CheckAncestors(paths.Target);
        CheckAncestors(paths.Manifest);
        CheckAncestors(paths.Control);
        if (old != null)
        {
            VerifyTree(paths.Target, old);
            if (!ReadBounded(paths.Manifest).SequenceEqual(oldBytes!))
                throw Conflict("The tracked manifest changed during synchronization", paths.Manifest);
        }
        else if (Exists(paths.Target) || Exists(paths.Manifest))
            throw Conflict("The destination appeared during synchronization", paths.Target);

        Directory.CreateDirectory(Path.GetDirectoryName(paths.Target)!);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.Manifest)!);
        EnsureAttributes(paths);
        WriteNew(paths.Journal, JsonSerializer.SerializeToUtf8Bytes(new Journal(transaction, Hash(manifestBytes),
            oldBytes == null ? null : Hash(oldBytes)), Json));
        // Cancellation stops before the journal. After it, finish the transaction or leave recovery evidence.
        Checkpoint?.Invoke("journaled");
        if (old != null) Directory.Move(paths.Target, Path.Combine(work, "previous"));
        Checkpoint?.Invoke("old-moved");
        if (old != null) VerifyTree(Path.Combine(work, "previous"), old);
        Directory.Move(next, paths.Target);
        Checkpoint?.Invoke("new-moved");
        VerifyTree(paths.Target, manifest);
        if (old != null) VerifyTree(Path.Combine(work, "previous"), old);
        WriteAtomic(paths, paths.Manifest, manifestBytes);
        Checkpoint?.Invoke("manifest-written");
        WriteAtomic(paths, paths.Baseline, manifestBytes);
        File.Delete(paths.Journal);
        return new(paths.Target, true);
    }

    private static void Recover(Locations paths)
    {
        if (!Exists(paths.Journal)) return;
        CheckAncestors(paths.Journal);
        var journal = Deserialize<Journal>(ReadBounded(paths.Journal));
        if (!Guid.TryParseExact(journal.Transaction, "N", out _)) throw Conflict("Invalid transaction name", paths.Journal);
        var work = Path.Combine(paths.Control, journal.Transaction);
        CheckAncestors(work);
        var nextBytes = ReadBounded(Path.Combine(work, "new.json"));
        if (Hash(nextBytes) != journal.NewHash) throw Conflict("Recovery metadata changed", work);
        var next = ParseManifest(nextBytes);
        byte[]? oldBytes = journal.OldHash == null ? null : ReadBounded(Path.Combine(work, "old.json"));
        if (oldBytes != null && Hash(oldBytes) != journal.OldHash) throw Conflict("Recovery metadata changed", work);
        // Do not overwrite a manifest edited after interruption.
        foreach (var metadata in new[] { paths.Manifest, paths.Baseline })
        {
            if (!Exists(metadata)) continue;
            var hash = Hash(ReadBounded(metadata));
            if (hash != journal.NewHash && hash != journal.OldHash) throw Conflict("The manifest changed after interruption", paths.Manifest);
        }
        var previous = Path.Combine(work, "previous");
        if (!Exists(paths.Target))
        {
            if (!Directory.Exists(Path.Combine(work, "next")))
                throw Conflict("The installed folder was removed after interruption", paths.Target);
            // Restore the old tree, including any late external edit. It will then fail the normal
            // baseline check rather than be lost. The newly staged tree remains in the transaction folder.
            if (oldBytes != null)
            {
                CheckTreeLinks(previous);
                Directory.Move(previous, paths.Target);
                WriteAtomic(paths, paths.Manifest, oldBytes);
                WriteAtomic(paths, paths.Baseline, oldBytes);
            }
            File.Delete(paths.Journal);
            return;
        }
        if (Exists(Path.Combine(work, "next")))
        {
            // The old tree was never moved. Verify it before abandoning the staged transaction.
            if (oldBytes == null) throw Conflict("A destination appeared after interruption", paths.Target);
            VerifyTree(paths.Target, ParseManifest(oldBytes));
            File.Delete(paths.Journal);
            return;
        }
        VerifyTree(paths.Target, next);
        if (oldBytes != null) VerifyTree(previous, ParseManifest(oldBytes));
        WriteAtomic(paths, paths.Manifest, nextBytes);
        WriteAtomic(paths, paths.Baseline, nextBytes);
        File.Delete(paths.Journal);
    }

    private static Manifest CreateManifest(ArchiveProjectSnapshot snapshot, string hash)
    {
        var entries = new List<Entry>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(ArchiveProjectEntry entry, string[] path)
        {
            if (path.Length > 0)
            {
                ValidateName(entry.Name);
                if (!names.Add(string.Join('/', path).Normalize(NormalizationForm.FormC)))
                    throw Conflict("Archive paths collide on a portable filesystem", string.Join('/', path));
            }
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

    private static Manifest ParseManifest(byte[] bytes)
    {
        var manifest = Deserialize<Manifest>(bytes);
        if (manifest.Format != 1 || manifest.Entries == null || manifest.Entries.Length is < 1 or > 100_000)
            throw new IOException("Unsupported or oversized folder manifest.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in manifest.Entries)
        {
            if (entry == null || entry.Path == null || entry.Path.Length > 128 || entry.Length < 0 || entry.Length > Limit ||
                entry.Revisions == null || entry.Name == null ||
                (entry.Directory ? entry.Hash != null || entry.Length != 0 || entry.Revisions.Length != 0 : !IsHash(entry.Hash)))
                throw new IOException("Invalid manifest entry.");
            foreach (var segment in entry.Path) ValidateName(segment);
            if (entry.Path.Length == 0)
            {
                if (paths.Count != 0 || !entry.Directory) throw new IOException("Invalid manifest root.");
            }
            else if (!directories.Contains(string.Join('/', entry.Path[..^1])) || entry.Name != entry.Path[^1])
                throw new IOException("Invalid manifest hierarchy.");
            if (!paths.Add(string.Join('/', entry.Path).Normalize(NormalizationForm.FormC)))
                throw new IOException("Duplicate manifest path.");
            if (entry.Directory) directories.Add(string.Join('/', entry.Path));
        }
        if (!directories.Contains("") || !IsHash(manifest.ContainerHash)) throw new IOException("Invalid manifest root or hash.");
        return manifest;
    }

    private static bool IsHash(string? hash) => hash is { Length: 64 } && hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static T Deserialize<T>(byte[] bytes)
    {
        try { return JsonSerializer.Deserialize<T>(bytes) ?? throw new IOException("Missing synchronization metadata."); }
        catch (JsonException ex) { throw new IOException("Invalid synchronization metadata.", ex); }
    }

    private static void VerifyTree(string root, Manifest manifest)
    {
        var actual = CheckTreeLinks(root);
        var expected = manifest.Entries.Select(e => string.Join('/', e.Path)).ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(expected)) throw Conflict("Files or folders were added, removed or renamed; synchronization stopped", root);
        foreach (var entry in manifest.Entries)
        {
            var path = MemberPath(root, entry.Path);
            if (entry.Directory)
            {
                if (!Directory.Exists(path)) throw Conflict("A directory was replaced", path);
            }
            else if (!File.Exists(path) || new FileInfo(path).Length != entry.Length || Hash(ReadBounded(path)) != entry.Hash)
                throw Conflict("A file was edited; synchronization stopped. Folder edits are not imported into the archive", path);
        }
    }

    private static HashSet<string> CheckTreeLinks(string root)
    {
        CheckAncestors(root);
        if (!Directory.Exists(root)) throw Conflict("The managed directory is missing", root);
        var found = new HashSet<string>(StringComparer.Ordinal) { "" };
        void Visit(string directory, int depth)
        {
            if (depth > 128) throw Conflict("Folder depth exceeds the limit", directory);
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                CheckAncestors(path);
                if (found.Count >= 100_000) throw Conflict("Folder entry count exceeds the limit", root);
                found.Add(Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'));
                if (Directory.Exists(path)) Visit(path, depth + 1);
            }
        }
        Visit(root, 0);
        return found;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrEmpty(name) || name is "." or ".." || name.EndsWith('.') || name.EndsWith(' ') ||
            name.Any(c => c < 32 || "\\/:*?\"<>|".Contains(c)) || Encoding.UTF8.GetByteCount(name) > 255)
            throw Conflict("Archive name cannot be represented as a portable filename", name);
        if (name.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
            name.Equals(".gitattributes", StringComparison.OrdinalIgnoreCase) ||
            name.Equals(".gitignore", StringComparison.OrdinalIgnoreCase))
            throw Conflict("Archive entry conflicts with Git repository controls", name);
        var stem = name.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem.Length == 4 && "123456789¹²³".Contains(stem[3]))
            throw Conflict("Archive name is reserved by the filesystem", name);
    }

    private static string MemberPath(string root, string[] segments) => Path.Combine([root, .. segments]);
    private static string ResolveProjectDirectory(string directory)
    {
        // Project-location aliases (including macOS /var -> /private/var) are valid.
        // Resolve each ancestor before deriving managed paths; links in the archive itself
        // or beneath the resulting projection/state directories remain forbidden.
        var root = Path.GetPathRoot(directory)!;
        var current = root;
        foreach (var segment in directory[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            var info = new DirectoryInfo(current);
            if (!info.Exists) throw new DirectoryNotFoundException(current);
            current = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? current;
        }
        CheckAncestors(current);
        return current;
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget != null;
    private static void CheckAncestors(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw Conflict("Symbolic links and reparse points are not supported in managed paths", current);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static byte[] ReadBounded(string path)
    {
        CheckAncestors(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > Limit) throw Conflict("File exceeds the synchronization byte limit", path);
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw Conflict("File changed while reading", path);
        return bytes;
    }
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void WriteNew(string path, ReadOnlySpan<byte> bytes)
    {
        CheckAncestors(path);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }
    private static void WriteAtomic(Locations paths, string path, byte[] bytes)
    {
        CheckAncestors(path);
        var temporary = Path.Combine(paths.Control, Guid.NewGuid().ToString("N") + ".tmp");
        WriteNew(temporary, bytes);
        File.Move(temporary, path, overwrite: true);
    }
    // Git must preserve archive bytes even when core.autocrlf is enabled. The common attributes
    // file belongs to the projection, outside the exact archive hierarchy.
    private static void ValidateAttributes(Locations paths)
    {
        CheckAncestors(paths.Attributes);
        if (Exists(paths.Attributes) && !ReadBounded(paths.Attributes).AsSpan().SequenceEqual("* -text\n"u8))
            throw Conflict("The projection Git attributes differ from the byte-preserving default", paths.Attributes);
    }
    private static void EnsureAttributes(Locations paths)
    {
        ValidateAttributes(paths);
        if (!Exists(paths.Attributes)) WriteNew(paths.Attributes, "* -text\n"u8);
    }
    private static IOException Conflict(string reason, string path) => new($"{reason}: {path}. Existing data is retained.");

    private sealed record Manifest(int Format, short ContainerVersion, string ContainerHash, Entry[] Entries);
    private sealed record Entry(string[] Path, string Name, bool Directory, byte Category, uint Flags, ulong Revision,
        int Length, string? Hash, uint[] Revisions);
    private sealed record Journal(string Transaction, string NewHash, string? OldHash);
    private sealed class Locations
    {
        public Locations(string source)
        {
            var name = Path.GetFileName(source);
            var root = Path.Combine(Path.GetDirectoryName(source)!, ".hexide");
            Target = Path.Combine(root, ".twinproj", name);
            Manifest = Path.Combine(root, ".twinproj", ".manifests", name + ".json");
            Control = Path.Combine(root, ".twinproj-sync", name);
        }
        public string Target { get; }
        public string Manifest { get; }
        public string Control { get; }
        public string Attributes => Path.Combine(Path.GetDirectoryName(Target)!, ".gitattributes");
        public string Baseline => Path.Combine(Control, "baseline.json");
        public string Journal => Path.Combine(Control, "pending.json");
    }
}
