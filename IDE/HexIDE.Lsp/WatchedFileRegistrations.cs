using HexIDE.Lsp.Messages;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace HexIDE.Lsp;

/// <summary>One connection's registrations, disk watches and coalesced outgoing events.</summary>
internal sealed class WatchedFileRegistrations : IDisposable
{
    internal const string Method = "workspace/didChangeWatchedFiles";
    private sealed record Watch(string Root, Regex Pattern, int Kind, bool Absolute);
    private readonly object _gate = new();
    private readonly Dictionary<string, JsonElement> _options = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Watch[]> _registrations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FileSystemWatcher> _directories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Uri, int Type, string? DiskRoot)> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _watchStarts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private readonly bool _correctDiskCreates;
    private readonly Func<string[]> _roots;
    private readonly Func<DidChangeWatchedFilesParams, Task> _send;
    private readonly ILogger _log;
    private readonly Timer _timer;
    private bool _disposed;

    public WatchedFileRegistrations(Func<string[]> roots, Func<DidChangeWatchedFilesParams, Task> send, ILogger log, bool? correctDiskCreates = null)
    {
        _correctDiskCreates = correctDiskCreates ?? OperatingSystem.IsMacOS();
        _roots = roots;
        _send = send;
        _log = log;
        _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Register(JsonElement p)
    {
        var registrations = ReadEntries(p, "registrations");
        lock (_gate)
        {
            if (_disposed) return;
            Install(Resolve(registrations));
            foreach (var registration in registrations)
                _options[registration.GetProperty("id").GetString()!] = registration.Clone();
        }
    }

    public void UpdateWorkspaceFolders()
    {
        lock (_gate)
        {
            if (_disposed) return;
            // Nothing can tell the server this failed, unlike a registration, so what it had stays in force.
            try { Install(Resolve(_options.Values.ToArray())); }
            catch (Exception ex) { _log.LogWarning(ex, "Could not watch the changed workspace folders; keeping the previous watches"); }
        }
    }

    private Dictionary<string, Watch[]> Resolve(JsonElement[] registrations)
    {
        var parsed = new Dictionary<string, Watch[]>(StringComparer.Ordinal);
        foreach (var registration in registrations)
        {
            var id = registration.GetProperty("id").GetString() ?? throw Invalid("Missing registration id.");
            var watches = new List<Watch>();
            foreach (var watcher in registration.GetProperty("registerOptions").GetProperty("watchers").EnumerateArray())
            {
                var kind = watcher.TryGetProperty("kind", out var k) ? k.GetInt32() : 7;
                if (kind is < 0 or > 7) throw Invalid("Invalid WatchKind.");
                var glob = watcher.GetProperty("globPattern");
                if (glob.ValueKind == JsonValueKind.String)
                {
                    var pattern = glob.GetString()!;
                    var absolute = Path.IsPathRooted(pattern);
                    foreach (var root in absolute ? new[] { AbsoluteRoot(pattern) } : _roots())
                        watches.Add(Create(root, pattern, kind, absolute));
                }
                else
                {
                    var baseUri = glob.GetProperty("baseUri");
                    var uri = new Uri(baseUri.ValueKind == JsonValueKind.String
                        ? baseUri.GetString()! : baseUri.GetProperty("uri").GetString()!, UriKind.Absolute);
                    if (!uri.IsFile) throw Invalid("A watched-file base must be a file URI.");
                    watches.Add(Create(uri.LocalPath, glob.GetProperty("pattern").GetString()!, kind, false));
                }
            }
            parsed.Add(id, watches.ToArray());
        }
        return parsed;
    }

    private void Install(Dictionary<string, Watch[]> parsed)
    {
        // Start all required watches before acknowledging. A failed request installs nothing.
        var added = new List<string>();
        try
        {
            foreach (var root in parsed.Values.SelectMany(w => w).Select(w => w.Root).Distinct())
            {
                if (_directories.ContainsKey(root)) continue;
                _directories.Add(root, CreateWatcher(root));
                added.Add(root);
            }
        }
        catch
        {
            foreach (var root in added) { _directories[root].Dispose(); _directories.Remove(root); _watchStarts.Remove(root); }
            throw;
        }
        foreach (var entry in parsed) _registrations[entry.Key] = entry.Value;
        PruneDirectories();
    }

    public void Unregister(JsonElement p)
    {
        // The spelling is part of LSP 3.17's wire contract.
        var entries = ReadEntries(p, "unregisterations");
        lock (_gate)
        {
            foreach (var entry in entries)
            {
                var id = entry.GetProperty("id").GetString()!;
                _registrations.Remove(id);
                _options.Remove(id);
            }
            PruneDirectories();
        }
    }

    private static JsonElement[] ReadEntries(JsonElement p, string name)
    {
        var entries = p.GetProperty(name).EnumerateArray().ToArray();
        if (entries.Any(e => e.GetProperty("method").GetString() != Method))
            throw new LocalRpcException("Only watched-file dynamic registrations are supported.")
            { ErrorCode = (int)StreamJsonRpc.Protocol.JsonRpcErrorCode.MethodNotFound };
        return entries;
    }

    private static LocalRpcException Invalid(string message) => new(message)
        { ErrorCode = (int)StreamJsonRpc.Protocol.JsonRpcErrorCode.InvalidParams };

    // Ignores case on every platform: a VB6 file is as often Upper.BAS as lower.bas, an extra event costs a server
    // nothing, and a missed one loses the file.
    private static Watch Create(string root, string glob, int kind, bool absolute)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        ExistingWatchDirectory(root);
        return new(root,
            new Regex("\\A" + FileOperationFilters.Glob(glob) + "\\z", RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(100)), kind, absolute);
    }

    private static string ExistingWatchDirectory(string root)
    {
        var existing = root;
        while (!Directory.Exists(existing))
            existing = Path.GetDirectoryName(existing) ?? throw Invalid("No watched-file directory exists.");
        if (Path.TrimEndingDirectorySeparator(existing) == Path.GetPathRoot(existing))
            throw Invalid("A watched-file registration cannot recursively watch a filesystem root.");
        return existing;
    }

    private static string AbsoluteRoot(string glob)
    {
        var wildcard = glob.IndexOfAny(['*', '?', '[', '{']);
        var prefix = wildcard < 0 ? glob : glob[..wildcard];
        return Path.GetDirectoryName(prefix) ?? Path.GetPathRoot(glob)!;
    }

    private FileSystemWatcher CreateWatcher(string root)
    {
        var existing = ExistingWatchDirectory(root);
        var watcher = new FileSystemWatcher(existing) { IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.DirectoryName };
        watcher.Created += (_, e) => NotifyFromDisk(e.FullPath, 1, root);
        watcher.Changed += (_, e) => NotifyFromDisk(e.FullPath, 2, root);
        watcher.Deleted += (_, e) => NotifyFromDisk(e.FullPath, 3, root);
        watcher.Renamed += (_, e) => { NotifyFromDisk(e.OldFullPath, 3, root); NotifyFromDisk(e.FullPath, 1, root); };
        watcher.Error += (_, e) => _log.LogWarning(e.GetException(), "LSP watched-file watcher failed for {Root}", root);
        _watchStarts[root] = DateTime.UtcNow;
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private void PruneDirectories()
    {
        var roots = _registrations.Values.SelectMany(w => w).Select(w => w.Root).ToHashSet(StringComparer.Ordinal);
        foreach (var root in _directories.Keys.Where(r => !roots.Contains(r)).ToArray())
        {
            _directories[root].Dispose();
            _directories.Remove(root);
            _watchStarts.Remove(root);
        }
        _reported.RemoveWhere(path => !Matches(path, 1) && !Matches(path, 2) && !Matches(path, 3));
        foreach (var path in _pending.Where(e => !Matches(e.Key, e.Value.Type)).Select(e => e.Key).ToArray())
            _pending.Remove(path);
    }

    private bool Matches(string path, int type)
    {
        var bit = 1 << (type - 1);
        foreach (var watch in _registrations.Values.SelectMany(w => w))
        {
            if ((watch.Kind & bit) == 0) continue;
            var relative = Path.GetRelativePath(watch.Root, path).Replace(Path.DirectorySeparatorChar, '/');
            if (relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative)) continue;
            try
            {
                if (watch.Pattern.IsMatch(watch.Absolute ? path.Replace(Path.DirectorySeparatorChar, '/') : relative)) return true;
            }
            catch (RegexMatchTimeoutException ex) { _log.LogWarning(ex, "LSP watched-file glob timed out"); }
        }
        return false;
    }

    public void Notify(string path, int type) => Queue(path, type, null);

    internal void NotifyFromDisk(string path, int type, string root) => Queue(path, type, root);

    private void Queue(string path, int type, string? diskRoot)
    {
        if (type is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(type));
        path = Path.GetFullPath(path);
        lock (_gate)
        {
            if (_disposed || !Matches(path, type)) return;
            var wasEmpty = _pending.Count == 0;
            if (_pending.TryGetValue(path, out var previous) && previous.Type == 1 && type == 2)
            {
                type = 1;
                diskRoot = previous.DiskRoot;
            }
            // Where disk Created is corrected (macOS), the IDE's own report is never corrected (#3650): the disk's Created or
            // Changed for the same write, arriving in the same batch, leaves it as the IDE said it. A deletion still replaces
            // it, as the last event does. Elsewhere the last event wins, as before.
            if (_correctDiskCreates && previous.Uri is not null && previous.DiskRoot is null && diskRoot is not null && type != 3)
            {
                type = previous.Type;
                diskRoot = null;
            }
            _pending[path] = (new Uri(path).AbsoluteUri, type, diskRoot);
            // A fixed batching window, not a sliding debounce: continuous changes still drain.
            if (wasEmpty) _timer.Change(150, Timeout.Infinite);
        }
    }

    private void Flush()
    {
        try
        {
            lock (_gate)
            {
                if (_disposed) return;
                var changes = new List<WatchedFileEvent>();
                foreach (var (path, pending) in _pending)
                {
                    var type = pending.Type;
                    if (_correctDiskCreates && type == 1 && pending.DiskRoot is { } root)
                    {
                        // #3650: FSEvents reports Created after Changed on edits. A file moved in from
                        // outside the watched folders retains its birth time, so macOS reports it Changed.
                        try
                        {
                            var file = new FileInfo(path);
                            if (file.Exists)
                            {
                                var birth = file.CreationTimeUtc;
                                if (_reported.Contains(path) || (_watchStarts.TryGetValue(root, out var start) && birth < start))
                                    type = 2;
                            }
                        }
                        catch (IOException) { /* Keep the reported type when metadata cannot be read. */ }
                        catch (UnauthorizedAccessException) { /* Keep the reported type when metadata cannot be read. */ }
                    }
                    if (!Matches(path, type)) continue;
                    changes.Add(new WatchedFileEvent(pending.Uri, type));
                    if (type == 3) _reported.Remove(path);
                    else _reported.Add(path);
                }
                _pending.Clear();
                // Queue while holding the lifetime lock so disposal cannot replace this connection.
                if (changes.Count > 0) _ = SendAsync(new DidChangeWatchedFilesParams(changes.ToArray()));
            }
        }
        catch (Exception ex) { _log.LogWarning(ex, "Could not batch LSP watched-file changes"); }
    }

    private async Task SendAsync(DidChangeWatchedFilesParams p)
    {
        try { await _send(p); }
        catch (Exception ex) { _log.LogWarning(ex, "Could not send LSP watched-file changes"); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Dispose();
            foreach (var watcher in _directories.Values) watcher.Dispose();
            _directories.Clear();
            _watchStarts.Clear();
            _reported.Clear();
            _registrations.Clear();
            _options.Clear();
            _pending.Clear();
        }
    }
}
