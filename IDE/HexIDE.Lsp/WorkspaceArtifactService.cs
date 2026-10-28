using HexIDE.Conversations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace HexIDE.Lsp;

/// <summary>Owns all writes; providers only produce content. A changed file relinquishes ownership.</summary>
public sealed class WorkspaceArtifactService : IWorkspaceArtifactService, IDisposable
{
    private readonly IWorkspaceArtifactWorkspace _workspace;
    private readonly IWorkspaceArtifactProviderRegistry _providers;
    private readonly IWorkspaceArtifactConsent _consent;
    private readonly ILogger<WorkspaceArtifactService> _log;
    private readonly Dictionary<string, string> _hashes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LanguageServerRegistration> _active = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _lastNote = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _pending;
    private readonly TimeProvider _timeProvider;
    internal Task PendingRegeneration { get; private set; } = Task.CompletedTask;
    private bool _disposed;
    private readonly Dictionary<string, Func<WorkspaceArtifactContext, CancellationToken, Task>> _imports = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<CancellationToken, Task>> _folderUpdates = new(StringComparer.Ordinal);
    private readonly string? _stateFile;
    private readonly ConversationLog? _capture;
    private readonly IWorkspaceLibraryNames? _libraryNames;
    internal Action<Stream, byte[]> WriteContent { get; set; } = (stream, bytes) => stream.Write(bytes);

    /// <summary>Larger than any artefact a provider has reason to write, so a file over it is never HexIDE's.</summary>
    internal const long MaxArtifactLength = 8 * 1024 * 1024;

    public WorkspaceArtifactService(IWorkspaceArtifactWorkspace workspace,
        IWorkspaceArtifactProviderRegistry providers, IWorkspaceArtifactConsent consent,
        ILogger<WorkspaceArtifactService> log, string? stateFile = null, ConversationLog? capture = null,
        IWorkspaceLibraryNames? libraryNames = null, TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _workspace = workspace;
        _providers = providers;
        _consent = consent;
        _log = log;
        _stateFile = stateFile;
        _capture = capture;
        _libraryNames = libraryNames;
        try
        {
            if (stateFile is not null && File.Exists(stateFile))
                foreach (var pair in JsonSerializer.Deserialize(File.ReadAllBytes(stateFile),
                    ArtifactOwnershipJsonContext.Default.DictionaryStringString) ?? [])
                    _hashes[pair.Key] = pair.Value;
        }
        catch (Exception ex) { _log.LogWarning(ex, "Could not read workspace artefact ownership."); }
        _workspace.ProjectChanged += OnProjectChanged;
    }

    public async Task TrackFoldersAsync(LanguageServerRegistration server, Func<CancellationToken, Task> update,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_disposed) return;
            _active[server.Id] = server;
            _folderUpdates[server.Id] = update;
            _ = _workspace.Projects; // Begin observing membership even without artefacts or imports.
        }
        finally { _gate.Release(); }
    }

    public async Task PrepareAsync(LanguageServerRegistration server, CancellationToken cancellationToken)
    {
        if (server.ProjectImport is not null) return;
        if (server.WorkspaceArtifactRefused)
            Note(server, "Workspace artefact declaration refused, so no file is generated: see the configuration "
                + "problems in Tools > Language Servers.");
        if (server.WorkspaceArtifact is not { } spec) return;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_disposed) return;
            _active[server.Id] = server;
            foreach (var project in _workspace.Projects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { await WriteAsync(server, spec, project); }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Workspace artefact for {Server} was not written; launching anyway.", server.Id);
                    Note(server, $"Workspace artefact {spec.RelativePath} not written; launching anyway: {ex.GetType().Name}.");
                }
            }
        }
        finally { _gate.Release(); }
    }

    public async Task ImportAsync(LanguageServerRegistration server,
        Func<WorkspaceArtifactContext, CancellationToken, Task> import, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_disposed) return;
            _active[server.Id] = server;
            _imports[server.Id] = import;
            foreach (var project in _workspace.Projects) await import(project, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private async Task WriteAsync(LanguageServerRegistration server, WorkspaceArtifactSpec spec,
        WorkspaceArtifactContext project)
    {
        var target = SafePath(project.WorkspaceRoot, spec.RelativePath);
        if (target is null)
        {
            _log.LogWarning("Refused workspace artefact path {Path} for {Server}.", spec.RelativePath, server.Id);
            Note(server, $"Refused workspace artefact path {spec.RelativePath}.");
            return;
        }
        if (!OwnsOrAbsent(target))
        {
            _log.LogInformation("Leaving existing workspace artefact {Path} alone for {Server}.", target, server.Id);
            Note(server, $"Leaving existing workspace artefact {spec.RelativePath} alone: not owned by HexIDE.");
            return;
        }
        if (_providers.Find(spec.ProviderName) is not { } provider) return;
        var content = provider.Produce(project with { Settings = spec.Settings });
        // Empty is nothing to describe too, so HexIDE never owns an empty file: see OwnsOrAbsent.
        if (string.IsNullOrEmpty(content))
        {
            if (File.Exists(target)) Relinquish(server, spec, project, target);
            return;
        }
        var consentRoot = Resolve(Path.GetFullPath(project.WorkspaceRoot));
        if (!await _consent.AllowAsync(server.Id, server.DisplayName, consentRoot, target, spec))
        {
            _log.LogInformation("Workspace artefact {Path} denied for {Server}; launching without it.", target, server.Id);
            Note(server, $"Workspace artefact {spec.RelativePath} denied; launching without it.");
            return;
        }
        // Only now may the provider read past the snapshot. A library's name is read by loading the library, from a path
        // the project file names, so a project nobody has allowed an artefact for must never get that far. The content
        // above, made without it, only decided whether there is anything to describe.
        if (_libraryNames is not null)
        {
            content = provider.Produce(project with { Settings = spec.Settings, LibraryNames = _libraryNames });
            if (string.IsNullOrEmpty(content)) return;
        }
        // Consent may have held a modal open: recheck both the links and the content before writing.
        if (SafePath(project.WorkspaceRoot, spec.RelativePath) != target)
        {
            Note(server, "Refused workspace artefact path: links changed during consent.");
            return;
        }
        if (!OwnsOrAbsent(target))
        {
            Note(server, $"Leaving existing workspace artefact {spec.RelativePath} alone: changed during consent.");
            return;
        }
        var bytes = Encoding.UTF8.GetBytes(content);
        if (bytes.Length > MaxArtifactLength)
        {
            Note(server, $"Workspace artefact {spec.RelativePath} not written: larger than any artefact HexIDE writes.");
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        // Deny other readers/writers while checking ownership; allow replacement on Windows.
        using var owned = File.Exists(target)
            ? new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete) : null;
        if (owned is not null)
        {
            if (!_hashes.TryGetValue(target, out var previous) || owned.Length is 0 or > MaxArtifactLength
                || Convert.ToHexString(SHA256.HashData(owned)) != previous)
            {
                Note(server, $"Leaving existing workspace artefact {spec.RelativePath} alone: ownership changed.");
                return;
            }
            owned.Position = 0;
            if (owned.Length == bytes.Length)
            {
                var existing = new byte[bytes.Length];
                owned.ReadExactly(existing);
                if (existing.AsSpan().SequenceEqual(bytes)) return;
            }
        }
        var temporary = Path.Combine(Path.GetDirectoryName(target)!, ".hexide-artifact-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                WriteContent(stream, bytes);
                stream.Flush(true);
            }
            // Windows refuses to rename over a file that still has an open handle, even one that shares delete, so
            // the ownership lock is let go only now: the race left is the replace call itself.
            var replacing = owned is not null;
            owned?.Dispose();
            File.Move(temporary, target, overwrite: replacing);
        }
        finally { File.Delete(temporary); }
        _hashes[target] = Convert.ToHexString(SHA256.HashData(bytes));
        _lastNote.Remove(server.Id);
        SaveOwnership();
        // #456: regeneration deliberately sends no didChangeWatchedFiles in this phase.
    }

    /// <summary>
    /// A provider with nothing to describe takes back what HexIDE wrote, and only that: no prompt, the same confinement
    /// and the same ownership check under a lock as a replace, so a delete reaches no further than a write could.
    /// </summary>
    private void Relinquish(LanguageServerRegistration server, WorkspaceArtifactSpec spec, WorkspaceArtifactContext project,
        string target)
    {
        // The provider ran since the path was checked, and a provider may read files: recheck the links before deleting.
        if (SafePath(project.WorkspaceRoot, spec.RelativePath) != target)
        {
            Note(server, $"Refused workspace artefact path {spec.RelativePath}.");
            return;
        }
        if (!OwnsOrAbsent(target))
        {
            Note(server, $"Leaving existing workspace artefact {spec.RelativePath} alone: ownership changed.");
            return;
        }
        using (var owned = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Delete))
        {
            if (!_hashes.TryGetValue(target, out var previous) || owned.Length is 0 or > MaxArtifactLength
                || Convert.ToHexString(SHA256.HashData(owned)) != previous)
            {
                Note(server, $"Leaving existing workspace artefact {spec.RelativePath} alone: ownership changed.");
                return;
            }
            File.Delete(target);
        }
        _hashes.Remove(target);
        SaveOwnership();
        _log.LogInformation("Removed workspace artefact {Path} for {Server}: its provider has nothing to describe.",
            target, server.Id);
        Note(server, $"Removed workspace artefact {spec.RelativePath}: its provider has nothing to describe.", fault: false);
    }

    private void SaveOwnership()
    {
        if (_stateFile is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_stateFile)!);
            File.WriteAllBytes(_stateFile, JsonSerializer.SerializeToUtf8Bytes(_hashes,
                ArtifactOwnershipJsonContext.Default.DictionaryStringString));
        }
        catch (Exception ex) { _log.LogWarning(ex, "Could not save workspace artefact ownership."); }
    }

    /// <summary>
    /// Records a reason on the server's connection, once: the same reason again for the same server, with no write in
    /// between, is the same news. The launch and the regeneration a project's load sets off straight after it would
    /// otherwise each record it (#2073).
    /// </summary>
    private void Note(LanguageServerRegistration server, string reason, bool fault = true)
    {
        lock (_lastNote)
        {
            if (_lastNote.TryGetValue(server.Id, out var last) && last == reason) return;
            _lastNote[server.Id] = reason;
        }
        _capture?.Record(server.Id, ConversationDirection.Local, ConversationEntryKind.Note,
            null, null, 0, detail: reason, fault: fault);
    }

    /// <summary>
    /// Nothing there, or a file still holding HexIDE's last write, decided without reading anything HexIDE can't have
    /// written. A directory is not owned. Nor is an empty file, which is also how a FIFO or a device presents and which
    /// opening could block on, nor one larger than any artefact: either would otherwise stall every server's launch behind
    /// the gate. A candidate is hashed through a stream, never read whole.
    /// </summary>
    private bool OwnsOrAbsent(string target)
    {
        if (Directory.Exists(target)) return false;
        var file = new FileInfo(target);
        if (!file.Exists) return true;
        if (!_hashes.TryGetValue(target, out var hash) || file.Length is 0 or > MaxArtifactLength) return false;
        using var stream = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream)) == hash;
    }

    internal static string? SafePath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.StartsWith('/') || relative.StartsWith('\\')
            || Path.IsPathRooted(relative) || relative.Contains(':')
            || relative.Split('/', '\\').Any(p => p.Trim() == "..")) return null;
        try
        {
            var realRoot = Resolve(Path.GetFullPath(root));
            var path = Path.GetFullPath(Path.Combine(realRoot, relative.Replace('\\', Path.DirectorySeparatorChar)));
            var resolved = Resolve(path);
            var prefix = Path.TrimEndingDirectorySeparator(realRoot) + Path.DirectorySeparatorChar;
            return resolved.StartsWith(prefix, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ? resolved : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// The path with every link in it resolved, the links inside a link's target included. On Linux and macOS a link's
    /// final target is the path its last link names, which can itself run through a link: macOS's temporary folder is
    /// under <c>/var</c>, a link to <c>/private/var</c>. Left there, a root and a path inside it came back resolved to
    /// different depths, and the containment check refused a file inside the workspace (#1933's macOS run). Windows
    /// already answers with the canonical path; resolving the target again changes nothing there.
    /// </summary>
    internal static string Resolve(string path) => Resolve(path, 0);

    private static string Resolve(string path, int depth)
    {
        if (depth > 40) throw new IOException("Too many levels of links.");
        var current = Path.GetPathRoot(path)!;
        foreach (var part in path[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is not null)
                current = Resolve(info.ResolveLinkTarget(true)?.FullName ?? throw new IOException("Unresolved link."),
                    depth + 1);
        }
        return current;
    }

    private void OnProjectChanged()
    {
        _pending?.Cancel();
        _pending = new CancellationTokenSource();
        PendingRegeneration = RegenerateAsync(_pending.Token);
    }

    private async Task RegenerateAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300), _timeProvider, token);
            (LanguageServerRegistration Server, Func<WorkspaceArtifactContext, CancellationToken, Task>? Import,
                Func<CancellationToken, Task>? Folders)[] servers;
            await _gate.WaitAsync(token);
            try { servers = _active.Values.Select(server => (server, _imports.GetValueOrDefault(server.Id), _folderUpdates.GetValueOrDefault(server.Id))).ToArray(); }
            finally { _gate.Release(); }
            foreach (var (server, import, folders) in servers)
            {
                if (folders is not null) await folders(token);
                if (server.ProjectImport is not null && import is not null)
                    await ImportAsync(server, import, token);
                else await PrepareAsync(server, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _log.LogWarning(ex, "Workspace artefact regeneration failed."); }
    }

    public void Dispose()
    {
        _disposed = true;
        _workspace.ProjectChanged -= OnProjectChanged;
        _pending?.Cancel();
        _pending?.Dispose();
    }
}

[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class ArtifactOwnershipJsonContext : JsonSerializerContext;
