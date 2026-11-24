using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HexIDE.IDE;

internal sealed record CompoundFolderResult(string Directory, bool Changed)
{
    /// <summary>Old transaction folders that pruning kept, each with its reason, for the caller to log.</summary>
    internal IReadOnlyList<string> Kept { get; init; } = [];
}

internal sealed record CompoundFolderItem(string[] Path, byte[]? Content);
// Formats retain their own manifest field ordering and metadata. The engine reads only the
// common inventory fields, and keeps the supplied manifest bytes unchanged through transactions.
internal sealed record CompoundFolderSnapshot(byte[] Manifest, IReadOnlyList<CompoundFolderItem> Entries);

internal interface ICompoundFolderFormat
{
    CompoundFolderSnapshot Read(byte[] bytes, string source, CancellationToken cancellationToken);
    void ValidateManifest(byte[] bytes);
}

/// <summary>Explicit, one-way projection of a saved archive. Previous trees are retained for recovery, with verified old transactions pruned after successful changes.</summary>
internal sealed class CompoundSourceFolder(ICompoundFolderFormat format)
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> OpenFiles = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private const int Limit = 64 * 1024 * 1024;
    private const int RetainedTransactionCount = 10;
    private const int RetainedTransactionDays = 7;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    internal TimeProvider Clock { get; init; } = TimeProvider.System;

    internal Action<string>? Checkpoint { get; set; }
    internal DistributionIdentity Identity { get; init; } = Distribution.Current;
    internal string? ChosenRoot { get; set; }

    internal CompoundFolderResult Synchronize(string containerPath, CancellationToken cancellationToken = default)
    {
        var requested = Path.GetFullPath(containerPath);
        var source = Path.Combine(ResolveProjectDirectory(Path.GetDirectoryName(requested)!), Path.GetFileName(requested));
        var name = Path.GetFileName(source);
        ValidateName(name);
        ValidateName(name + ".json");
        if (new[] { ProjectLocalState.FolderMarker, ".manifests", ".sync", ".gitattributes", ".twinproj", ".twinproj-sync" }
            .Contains(name, StringComparer.OrdinalIgnoreCase))
            throw Conflict("Archive name conflicts with project-local state", name);
        var bytes = ReadBounded(source);
        var snapshot = format.Read(bytes, source, cancellationToken);
        var manifestBytes = snapshot.Manifest;
        if (manifestBytes.Length > Limit) throw new IOException("The folder manifest exceeds the synchronization byte limit.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in snapshot.Entries)
        {
            foreach (var segment in entry.Path) ValidateName(segment);
            if (entry.Path.Length > 0 && !names.Add(string.Join('/', entry.Path).Normalize(NormalizationForm.FormC)))
                throw Conflict("Archive paths collide on a portable filesystem", string.Join('/', entry.Path));
        }
        var manifest = ParseManifest(manifestBytes);
        var root = ProjectLocalState.Resolve(source, folder: true, ChosenRoot, Identity);
        var paths = new Locations(source, root);
        CheckAncestors(paths.Control);
        CheckAncestors(paths.Target);
        CheckAncestors(paths.Manifest);
        ValidateAttributes(paths);
        cancellationToken.ThrowIfCancellationRequested();
        Migrate(source, root, paths, manifest, manifestBytes);

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
            ProjectLocalState.WriteFolderMarker(root);
            CreateDirectory(paths.Control);
            WriteNew(owner, Encoding.UTF8.GetBytes(ownership));
        }
        CheckAncestors(paths.Control);
        ProjectLocalState.WriteFolderMarker(root);
        var lockPath = Path.Combine(paths.Control, "lock");
        CheckAncestors(lockPath);
        using var lease = new Lease(lockPath, FileMode.OpenOrCreate);
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
        CreateDirectory(next);
        foreach (var entry in snapshot.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = MemberPath(next, entry.Path);
            if (entry.Content == null)
            {
                CheckAncestors(destination);
                Directory.CreateDirectory(destination);
            }
            else WriteNew(destination, entry.Content);
        }
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

        CreateDirectory(Path.GetDirectoryName(paths.Target)!);
        CreateDirectory(Path.GetDirectoryName(paths.Manifest)!);
        EnsureAttributes(paths);
        WriteNew(paths.Journal, JsonSerializer.SerializeToUtf8Bytes(new Journal(transaction, Hash(manifestBytes),
            oldBytes == null ? null : Hash(oldBytes)), Json));
        // Cancellation stops before the journal. After it, finish the transaction or leave recovery evidence.
        Checkpoint?.Invoke("journaled");
        if (old != null) MoveDirectory(paths.Target, Path.Combine(work, "previous"));
        Checkpoint?.Invoke("old-moved");
        if (old != null) VerifyTree(Path.Combine(work, "previous"), old);
        MoveDirectory(next, paths.Target);
        Checkpoint?.Invoke("new-moved");
        VerifyTree(paths.Target, manifest);
        if (old != null) VerifyTree(Path.Combine(work, "previous"), old);
        WriteAtomic(paths, paths.Manifest, manifestBytes);
        Checkpoint?.Invoke("manifest-written");
        WriteAtomic(paths, paths.Baseline, manifestBytes);
        DeleteFile(paths.Journal);
        var kept = Prune(paths.Control, transaction);
        return new(paths.Target, true) { Kept = kept };
    }

    internal IReadOnlyList<string> Prune(string control, string completedTransaction)
    {
        // The sync has already succeeded. An I/O or access failure leaves that recovery data in place without
        // changing the result, and one candidate's failure doesn't stop the next. Each folder kept that way is
        // returned with its reason, for the caller to log.
        var kept = new List<string>();
        void Keep(string path, Exception ex) => kept.Add($"{path}: {ex.Message}");
        try
        {
            CheckAncestors(control);
            var journal = Path.Combine(control, "pending.json");
            if (Exists(journal)) return kept;
            var candidates = new List<(string Path, DateTime Modified)>();
            foreach (var folder in Directory.GetDirectories(control))
            {
                var name = Path.GetFileName(folder);
                try
                {
                    // A transaction this sync renamed for pruning, whose deletion stopped part-way: finish it.
                    if (name.EndsWith(PruningSuffix, StringComparison.Ordinal) && IsTransactionName(name[..^PruningSuffix.Length]))
                    {
                        CheckAncestors(folder);
                        DeleteTree(folder);
                        continue;
                    }
                    // Only a name the sync itself makes, which is a lower-case GUID in "N" format.
                    if (!IsTransactionName(name)) continue;
                    CheckAncestors(folder);
                    var metadata = Path.Combine(folder, "new.json");
                    ParseManifest(ReadBounded(metadata));
                    candidates.Add((folder, File.GetLastWriteTimeUtc(metadata)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Keep(folder, ex); }
            }
            var cutoff = Clock.GetUtcNow().UtcDateTime.AddDays(-RetainedTransactionDays);
            foreach (var candidate in candidates.OrderByDescending(c => c.Modified).Skip(RetainedTransactionCount))
            {
                if (candidate.Modified >= cutoff || Path.GetFileName(candidate.Path) == completedTransaction) continue;
                try
                {
                    if (Exists(journal)) return kept;
                    CheckTreeLinks(candidate.Path);
                    foreach (var (tree, metadata) in new[] { ("previous", "old.json"), ("next", "new.json") })
                    {
                        var path = Path.Combine(candidate.Path, tree);
                        if (Exists(path) || tree == "previous" && Exists(Path.Combine(candidate.Path, metadata)))
                            VerifyTree(path, ParseManifest(ReadBounded(Path.Combine(candidate.Path, metadata))));
                    }
                    // Renaming first makes each removal all or nothing to the next sync. A rename Windows refuses,
                    // because a file in the folder is open, changes nothing; a deletion that stops part-way leaves
                    // a renamed folder, which the next prune finishes.
                    var renamed = candidate.Path + PruningSuffix;
                    Directory.Move(candidate.Path, renamed);
                    DeleteTree(renamed);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Keep(candidate.Path, ex); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Keep(control, ex); }
        return kept;
    }

    private const string PruningSuffix = ".pruning";

    private static bool IsTransactionName(string name) =>
        Guid.TryParseExact(name, "N", out var id) && name == id.ToString("N");

    // Bottom-up, one entry at a time, and never through a link: Directory.Delete with recursive: true is not used.
    // CheckTreeLinks has refused links already, so a link found here appeared since. Stop there and keep the rest.
    private static void DeleteTree(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw Conflict("A link appeared in recovery data being pruned", directory);
        foreach (var path in Directory.EnumerateFileSystemEntries(directory).ToArray())
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw Conflict("A link appeared in recovery data being pruned", path);
            if ((attributes & FileAttributes.Directory) != 0) DeleteTree(path);
            else File.Delete(path);
        }
        Directory.Delete(directory, recursive: false);
    }

    private void Recover(Locations paths)
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
                MoveDirectory(previous, paths.Target);
                WriteAtomic(paths, paths.Manifest, oldBytes);
                WriteAtomic(paths, paths.Baseline, oldBytes);
            }
            DeleteFile(paths.Journal);
            return;
        }
        if (Exists(Path.Combine(work, "next")))
        {
            // The old tree was never moved. Verify it before abandoning the staged transaction.
            if (oldBytes == null) throw Conflict("A destination appeared after interruption", paths.Target);
            VerifyTree(paths.Target, ParseManifest(oldBytes));
            DeleteFile(paths.Journal);
            return;
        }
        VerifyTree(paths.Target, next);
        if (oldBytes != null) VerifyTree(previous, ParseManifest(oldBytes));
        WriteAtomic(paths, paths.Manifest, nextBytes);
        WriteAtomic(paths, paths.Baseline, nextBytes);
        DeleteFile(paths.Journal);
    }

    private Manifest ParseManifest(byte[] bytes)
    {
        format.ValidateManifest(bytes);
        var manifest = Deserialize<Manifest>(bytes);
        if (manifest.Format != 1 || manifest.Entries == null || manifest.Entries.Length is < 1 or > 100_000)
            throw new IOException("Unsupported or oversized folder manifest.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in manifest.Entries)
        {
            if (entry == null || entry.Path == null || entry.Path.Length > 128 || entry.Length < 0 || entry.Length > Limit ||
                entry.Name == null ||
                (entry.Directory ? entry.Hash != null || entry.Length != 0 : !IsHash(entry.Hash)))
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

    internal static void ValidateName(string name)
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
    private static string ResolveProjectDirectory(string directory, int remainingLinks = 40)
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
            var target = info.ResolveLinkTarget(returnFinalTarget: false);
            if (target != null)
            {
                if (remainingLinks == 0) throw Conflict("Project directory link resolution exceeds the limit", directory);
                // A link target can itself contain linked ancestors. Resolving only the final
                // target component leaves aliases such as /var embedded in the resulting path.
                current = ResolveProjectDirectory(target.FullName, remainingLinks - 1);
            }
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
        CheckAncestors(path);
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
    private static void CreateDirectory(string path)
    {
        CheckAncestors(path);
        Directory.CreateDirectory(path);
    }
    private static void MoveDirectory(string source, string destination)
    {
        CheckAncestors(source);
        CheckAncestors(destination);
        var prefix = Path.GetFullPath(source) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (OpenFiles.Keys.Any(path => path.StartsWith(prefix, comparison)))
            throw Conflict("A synchronization handle is open inside the directory", source);
        Directory.Move(source, destination);
    }
    private static void DeleteFile(string path)
    {
        CheckAncestors(path);
        File.Delete(path);
    }

    private void Migrate(string source, string root, Locations paths, Manifest current, byte[] currentBytes)
    {
        var old = new Locations(source, root, legacy: true);
        // This record stays outside every directory being transferred or removed.
        var recordName = Hash(Encoding.UTF8.GetBytes(Path.GetFileName(source).ToUpperInvariant()));
        var recordPath = Path.Combine(root, ".sync-" + recordName + ".migration.json");
        var recorded = Exists(recordPath);
        if (!recorded && !new[] { old.Target, old.Manifest, old.Control }.Any(Exists))
            return;
        foreach (var path in new[] { old.Target, old.Manifest, old.Control, paths.Target, paths.Manifest, paths.Control, recordPath })
            CheckAncestors(path);
        if (!recorded && new[] { paths.Target, paths.Manifest, paths.Control }.Any(Exists))
            throw Conflict("Both synchronization layouts exist", paths.Target);
        ValidateAttributes(old);
        Lease? oldLease = null;
        Lease? newLease = null;
        Lease? recordLease = null;
        try
        {
            Migration? migration = null;
            if (recorded)
            {
                recordLease = new Lease(recordPath, FileMode.Open);
                if (recordLease.Length > Limit) throw Conflict("Recovery metadata changed", recordPath);
                var bytes = new byte[checked((int)recordLease.Length)];
                recordLease.ReadExactly(bytes);
                migration = Deserialize<Migration>(bytes);
                if (migration.Version != 1 || !IsHash(migration.ManifestHash))
                    throw Conflict("Recovery metadata changed", recordPath);
            }
            foreach (var control in new[] { old.Control, paths.Control })
            {
                if (!Exists(control)) continue;
                CheckTreeLinks(control);
                var owner = Path.Combine(control, "owner");
                // A recorded move can stop between creating the new directory and writing its owner,
                // or between removing the old owner and its now-empty directory.
                if (recorded && !Exists(owner) && !Directory.EnumerateFileSystemEntries(control).Any()) continue;
                if (!File.Exists(owner) || !ReadBounded(owner).AsSpan().SequenceEqual("HexIDE archive source synchronization v1\n"u8))
                    throw Conflict("The synchronization state directory is not owned by HexIDE", control);
                var ignore = Path.Combine(control, ".gitignore");
                if (Exists(ignore) && !ReadBounded(ignore).AsSpan().SequenceEqual("*\n"u8))
                    throw Conflict("The local synchronization ignore rule was changed", ignore);
                var lockPath = Path.Combine(control, "lock");
                // Do not recreate a removed old lock while merely checking a recorded move.
                if (recorded && !Exists(lockPath)) continue;
                var lease = new Lease(lockPath, FileMode.Open);
                if (control == old.Control) oldLease = lease;
                else newLease = lease;
            }
            if (!recorded) Recover(old);
            var target = MigrationPath(old.Target, paths.Target);
            var manifestPath = MigrationPath(old.Manifest, paths.Manifest);
            var baselinePath = MigrationPath(old.Baseline, paths.Baseline);
            var hasBaseline = Exists(baselinePath);
            var expected = hasBaseline ? ReadBounded(baselinePath) : currentBytes;
            var hasProjection = Exists(target) || Exists(manifestPath) || hasBaseline;
            if (migration != null && (migration.ManifestHash != Hash(expected) || migration.HasProjection != hasProjection ||
                migration.HasBaseline != hasBaseline))
                throw Conflict("Recovery metadata changed", manifestPath);
            if (hasProjection)
            {
                if (!File.Exists(manifestPath) || !ReadBounded(manifestPath).SequenceEqual(expected))
                    throw hasBaseline ? Conflict("The tracked manifest was changed or removed", manifestPath)
                        : Conflict("The folder has no matching synchronization baseline", target);
                VerifyTree(target, hasBaseline ? ParseManifest(expected) : current);
            }
            Checkpoint?.Invoke("legacy-verified");
            if (!recorded)
            {
                CheckAncestors(recordPath);
                recordLease = new Lease(recordPath, FileMode.CreateNew);
                recordLease.Write(JsonSerializer.SerializeToUtf8Bytes(new Migration(1, Hash(expected), hasProjection, hasBaseline)));
                recordLease.Flush(flushToDisk: true);
            }
            Checkpoint?.Invoke("migration-recorded");
            CreateDirectory(Path.GetDirectoryName(paths.Manifest)!);
            Checkpoint?.Invoke("migration-manifest-directory");
            CreateDirectory(Path.GetDirectoryName(paths.Control)!);
            Checkpoint?.Invoke("migration-state-directory");
            CreateDirectory(paths.Control);
            Checkpoint?.Invoke("migration-control-created");
            var newOwner = Path.Combine(paths.Control, "owner");
            if (!Exists(newOwner)) WriteNew(newOwner, "HexIDE archive source synchronization v1\n"u8);
            Checkpoint?.Invoke("migration-owner-written");
            newLease ??= new Lease(Path.Combine(paths.Control, "lock"), FileMode.OpenOrCreate);
            Checkpoint?.Invoke("migration-new-lease");
            if (Exists(old.Target)) MoveDirectory(old.Target, paths.Target);
            Checkpoint?.Invoke("migration-tree-moved");
            if (Exists(old.Manifest)) MoveFile(old.Manifest, paths.Manifest);
            Checkpoint?.Invoke("migration-manifest-moved");
            if (Directory.Exists(old.Control))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(old.Control).Order(StringComparer.Ordinal))
                {
                    var name = Path.GetFileName(entry);
                    if (name is "owner" or "lock") continue;
                    var destination = Path.Combine(paths.Control, name);
                    if (Directory.Exists(entry)) MoveDirectory(entry, destination);
                    else MoveFile(entry, destination);
                    Checkpoint?.Invoke("migration-state-" + (Directory.Exists(destination) ? "transaction" : name));
                }
                oldLease?.Dispose();
                oldLease = null;
                DeleteFile(Path.Combine(old.Control, "lock"));
                Checkpoint?.Invoke("migration-old-lock-removed");
                DeleteFile(Path.Combine(old.Control, "owner"));
                Checkpoint?.Invoke("migration-old-owner-removed");
                RemoveEmpty(old.Control);
            }
            Checkpoint?.Invoke("migration-old-control-removed");
            EnsureAttributes(paths);
            Checkpoint?.Invoke("migration-attributes");
            RemoveLegacyDirectories(old);
            Checkpoint?.Invoke("migration-cleaned");
            recordLease!.Dispose();
            recordLease = null;
            DeleteFile(recordPath);
            Checkpoint?.Invoke("migration-record-removed");
        }
        finally
        {
            oldLease?.Dispose();
            newLease?.Dispose();
            recordLease?.Dispose();
        }
    }

    private static string MigrationPath(string old, string current)
    {
        if (Exists(old) && Exists(current)) throw Conflict("Both synchronization layouts exist", current);
        return Exists(old) ? old : current;
    }

    private static void MoveFile(string source, string destination)
    {
        CheckAncestors(source);
        CheckAncestors(destination);
        File.Move(source, destination);
    }

    private sealed class Lease : FileStream
    {
        internal Lease(string path, FileMode mode) : base(CheckedPath(path), mode, FileAccess.ReadWrite, FileShare.None)
        {
            OpenFiles.TryAdd(Path.GetFullPath(path), 0);
        }

        private static string CheckedPath(string path)
        {
            CheckAncestors(path);
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) OpenFiles.TryRemove(Path.GetFullPath(Name), out _);
        }
    }

    private sealed record Migration(int Version, string ManifestHash, bool HasProjection, bool HasBaseline);

    private static void RemoveLegacyDirectories(Locations old)
    {
        var manifests = Path.GetDirectoryName(old.Manifest)!;
        RemoveEmpty(manifests);
        var trees = Path.GetDirectoryName(old.Target)!;
        CheckAncestors(trees);
        if (Directory.Exists(trees) && Directory.EnumerateFileSystemEntries(trees).All(p => p == old.Attributes))
        {
            if (Exists(old.Attributes)) DeleteFile(old.Attributes);
            RemoveEmpty(trees);
        }
        RemoveEmpty(Path.GetDirectoryName(old.Control)!);
    }

    private static void RemoveEmpty(string path)
    {
        CheckAncestors(path);
        if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
        {
            CheckAncestors(path);
            Directory.Delete(path);
        }
    }

    private static IOException Conflict(string reason, string path) => new($"{reason}: {path}. Existing data is retained.");

    private sealed record Manifest(int Format, string ContainerHash, Entry[] Entries);
    private sealed record Entry(string[] Path, string Name, bool Directory, int Length, string? Hash);
    private sealed record Journal(string Transaction, string NewHash, string? OldHash);
    private sealed class Locations
    {
        public Locations(string source, string root, bool legacy = false)
        {
            var name = Path.GetFileName(source);
            Target = legacy ? Path.Combine(root, ".twinproj", name) : Path.Combine(root, name);
            Manifest = legacy ? Path.Combine(root, ".twinproj", ".manifests", name + ".json") : Path.Combine(root, ".manifests", name + ".json");
            Control = Path.Combine(root, legacy ? ".twinproj-sync" : ".sync", name);
        }
        public string Target { get; }
        public string Manifest { get; }
        public string Control { get; }
        public string Attributes => Path.Combine(Path.GetDirectoryName(Target)!, ".gitattributes");
        public string Baseline => Path.Combine(Control, "baseline.json");
        public string Journal => Path.Combine(Control, "pending.json");
    }
}
