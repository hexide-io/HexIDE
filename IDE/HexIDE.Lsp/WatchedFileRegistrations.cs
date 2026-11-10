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
    private readonly Dictionary<string, (string Uri, int Type)> _pending = new(StringComparer.Ordinal);
    private readonly Func<string[]> _roots;
    private readonly Func<DidChangeWatchedFilesParams, Task> _send;
    private readonly ILogger _log;
    private readonly Timer _timer;
    private bool _disposed;

    public WatchedFileRegistrations(Func<string[]> roots, Func<DidChangeWatchedFilesParams, Task> send, ILogger log)
    {
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
            foreach (var root in added) { _directories[root].Dispose(); _directories.Remove(root); }
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
        watcher.Created += (_, e) => Notify(e.FullPath, 1);
        watcher.Changed += (_, e) => Notify(e.FullPath, 2);
        watcher.Deleted += (_, e) => Notify(e.FullPath, 3);
        watcher.Renamed += (_, e) => { Notify(e.OldFullPath, 3); Notify(e.FullPath, 1); };
        watcher.Error += (_, e) => _log.LogWarning(e.GetException(), "LSP watched-file watcher failed for {Root}", root);
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
        }
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

    public void Notify(string path, int type)
    {
        if (type is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(type));
        path = Path.GetFullPath(path);
        lock (_gate)
        {
            if (_disposed || !Matches(path, type)) return;
            var wasEmpty = _pending.Count == 0;
            if (_pending.TryGetValue(path, out var previous) && previous.Type == 1 && type == 2) type = 1;
            _pending[path] = (new Uri(path).AbsoluteUri, type);
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
                var changes = _pending.Where(e => Matches(e.Key, e.Value.Type))
                    .Select(e => new WatchedFileEvent(e.Value.Uri, e.Value.Type)).ToArray();
                _pending.Clear();
                // Queue while holding the lifetime lock so disposal cannot replace this connection.
                if (changes.Length > 0) _ = SendAsync(new DidChangeWatchedFilesParams(changes));
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
            _registrations.Clear();
            _options.Clear();
            _pending.Clear();
        }
    }
}
