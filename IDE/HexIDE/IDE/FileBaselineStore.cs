using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HexIDE.IDE;

/// <summary>
/// Thread-safe, in-memory <see cref="IFileBaselineStore"/>. Keys are full paths compared
/// case-insensitively (matching the path handling in <c>ProjectService</c> on Windows/macOS).
/// </summary>
public sealed class FileBaselineStore : IFileBaselineStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, FileBaseline> _baselines = new(StringComparer.OrdinalIgnoreCase);
    private long _epoch;

    private static string Normalize(string path) => Path.GetFullPath(path);

    public FileBaseline? TryGet(string path)
    {
        lock (_lock)
            return _baselines.TryGetValue(Normalize(path), out var baseline) ? baseline : null;
    }

    public void Record(string path, byte[] content) =>
        RecordInternal(path, FileHasher.Hash(content), content.LongLength);

    public void Record(string path, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        RecordInternal(path, FileHasher.Hash(bytes), bytes.LongLength);
    }

    public void Record(string path, FileBaseline baseline)
    {
        lock (_lock)
            _baselines[Normalize(path)] = baseline;
    }

    private void RecordInternal(string path, string hash, long length)
    {
        var normalized = Normalize(path);
        DateTime lastWriteUtc;
        try { lastWriteUtc = File.GetLastWriteTimeUtc(normalized); }
        catch { lastWriteUtc = default; }

        lock (_lock)
        {
            var epoch = ++_epoch;
            _baselines[normalized] = new FileBaseline(hash, length, lastWriteUtc, epoch);
        }
    }

    public bool Matches(string path, byte[] diskContent)
    {
        var baseline = TryGet(path);
        if (baseline is null)
            return false;
        if (baseline.Length != diskContent.LongLength)
            return false;
        return string.Equals(baseline.Hash, FileHasher.Hash(diskContent), StringComparison.Ordinal);
    }

    public void Remove(string path)
    {
        lock (_lock)
            _baselines.Remove(Normalize(path));
    }

    public IReadOnlyDictionary<string, FileBaseline> Snapshot()
    {
        lock (_lock)
            return new Dictionary<string, FileBaseline>(_baselines, StringComparer.OrdinalIgnoreCase);
    }
}
