using System;
using System.Collections.Generic;
using System.Linq;

namespace HexIDE.IDE;

/// <summary>
/// Coalesces a burst of file-change notifications into a single batch. A <see cref="FileSystemWatcher"/>
/// fires one event per file, so a <c>git pull</c> or branch switch arrives as a flurry over a few hundred
/// milliseconds. Each <see cref="Notify"/> resets a quiet-period timer; the batch only becomes ready once
/// the filesystem has been quiet for <c>quietPeriod</c> — or once <c>maxWait</c> elapses since the first
/// event of the burst, so a continuously-churning directory still flushes.
/// <para>
/// Pure logic — owns no timer. The caller (the file-watcher service) drives it by polling
/// <see cref="TryDrain"/>; tests advance a fake <see cref="IClock"/> and call it directly, no real waiting.
/// Thread-safe.
/// </para>
/// </summary>
public sealed class ChangeCoalescer
{
    private readonly IClock _clock;
    private readonly TimeSpan _quietPeriod;
    private readonly TimeSpan _maxWait;
    private readonly object _lock = new();
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastNotifyUtc;
    private DateTime _burstStartUtc;

    public ChangeCoalescer(IClock clock, TimeSpan? quietPeriod = null, TimeSpan? maxWait = null)
    {
        _clock = clock;
        _quietPeriod = quietPeriod ?? TimeSpan.FromMilliseconds(400);
        _maxWait = maxWait ?? TimeSpan.FromMilliseconds(3000);
    }

    /// <summary>Records a changed path and (re)starts the quiet-period window.</summary>
    public void Notify(string path)
    {
        lock (_lock)
        {
            var now = _clock.UtcNow;
            if (_pending.Count == 0)
                _burstStartUtc = now;
            _pending.Add(path);
            _lastNotifyUtc = now;
        }
    }

    /// <summary>
    /// If the burst has settled (quiet period elapsed since the last <see cref="Notify"/>) or the
    /// max-wait cap has been hit, outputs the coalesced set of paths, clears the pending set, and returns
    /// true. Otherwise outputs an empty collection and returns false.
    /// </summary>
    public bool TryDrain(out IReadOnlyCollection<string> batch)
    {
        lock (_lock)
        {
            if (_pending.Count == 0)
            {
                batch = Array.Empty<string>();
                return false;
            }

            var now = _clock.UtcNow;
            var quietElapsed = now - _lastNotifyUtc >= _quietPeriod;
            var maxWaitExceeded = now - _burstStartUtc >= _maxWait;
            if (!quietElapsed && !maxWaitExceeded)
            {
                batch = Array.Empty<string>();
                return false;
            }

            batch = _pending.ToArray();
            _pending.Clear();
            return true;
        }
    }
}
