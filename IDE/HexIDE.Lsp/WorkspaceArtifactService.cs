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
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _pending;
    private bool _disposed;
    private readonly string? _stateFile;
    private readonly ConversationLog? _capture;
    internal Action<Stream, byte[]> WriteContent { get; set; } = (stream, bytes) => stream.Write(bytes);

    public WorkspaceArtifactService(IWorkspaceArtifactWorkspace workspace,
        IWorkspaceArtifactProviderRegistry providers, IWorkspaceArtifactConsent consent,
        ILogger<WorkspaceArtifactService> log, string? stateFile = null, ConversationLog? capture = null)
    {
        _workspace = workspace;
        _providers = providers;
        _consent = consent;
        _log = log;
        _stateFile = stateFile;
        _capture = capture;
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

    public async Task PrepareAsync(LanguageServerRegistration server, CancellationToken cancellationToken)
    {
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
        var content = _providers.Find(spec.ProviderName)?.Produce(project with { Settings = spec.Settings });
        if (content is null) return;
        var consentRoot = Resolve(Path.GetFullPath(project.WorkspaceRoot));
        if (!await _consent.AllowAsync(server.Id, server.DisplayName, consentRoot, target))
        {
            _log.LogInformation("Workspace artefact {Path} denied for {Server}; launching without it.", target, server.Id);
            Note(server, $"Workspace artefact {spec.RelativePath} denied; launching without it.");
            return;
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
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        // Deny other readers/writers while checking ownership; allow replacement on Windows.
        using var owned = File.Exists(target)
            ? new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete) : null;
        if (owned is not null)
        {
            if (!_hashes.TryGetValue(target, out var previous)
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
            File.Move(temporary, target, overwrite: owned is not null);
        }
        finally { File.Delete(temporary); }
        _hashes[target] = Convert.ToHexString(SHA256.HashData(bytes));
        if (_stateFile is not null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_stateFile)!);
                File.WriteAllBytes(_stateFile, JsonSerializer.SerializeToUtf8Bytes(_hashes,
                    ArtifactOwnershipJsonContext.Default.DictionaryStringString));
            }
            catch (Exception ex) { _log.LogWarning(ex, "Could not save workspace artefact ownership."); }
        }
        // #456: regeneration deliberately sends no didChangeWatchedFiles in this phase.
    }

    private void Note(LanguageServerRegistration server, string reason)
    {
        _capture?.Record(server.Id, ConversationDirection.Local, ConversationEntryKind.Note,
            null, null, 0, detail: reason, fault: true);
    }

    private bool OwnsOrAbsent(string target) => !File.Exists(target)
        || (_hashes.TryGetValue(target, out var hash)
            && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))) == hash);

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

    private static string Resolve(string path)
    {
        var current = Path.GetPathRoot(path)!;
        foreach (var part in path[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is not null)
                current = info.ResolveLinkTarget(true)?.FullName ?? throw new IOException("Unresolved link.");
        }
        return current;
    }

    private void OnProjectChanged()
    {
        _pending?.Cancel();
        _pending = new CancellationTokenSource();
        _ = RegenerateAsync(_pending.Token);
    }

    private async Task RegenerateAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token);
            LanguageServerRegistration[] servers;
            await _gate.WaitAsync(token);
            try { servers = _active.Values.ToArray(); }
            finally { _gate.Release(); }
            foreach (var server in servers) await PrepareAsync(server, token);
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
