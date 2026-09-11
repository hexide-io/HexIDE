using System.Security.Cryptography;

namespace HexIDE.Conversations;

/// <summary>
/// A message body as it was retained: possibly shortened, always honest about by how much.
/// </summary>
/// <param name="Head">The opening bytes.</param>
/// <param name="Tail">The closing bytes, or null when the whole body fitted.</param>
/// <param name="TrueLength">
/// What the body actually was. Stated rather than implied, because a reader looking at a shortened frame
/// and not knowing it is shortened will draw conclusions from a shape that was never on the wire.
/// </param>
public sealed record StoredBody(byte[] Head, byte[]? Tail, int TrueLength)
{
    /// <summary>True when this is only part of what crossed the wire.</summary>
    public bool IsTruncated => Tail is not null;

    /// <summary>What this costs the budget.</summary>
    public int RetainedBytes => Head.Length + (Tail?.Length ?? 0);
}

/// <summary>A ceiling shared by every connection, so several of them cannot add up to something unbounded.</summary>
public sealed class PayloadBudget(long ceilingBytes)
{
    private readonly object _lock = new();
    private long _used;

    public long Used { get { lock (_lock) return _used; } }
    public long Ceiling => ceilingBytes;

    /// <summary>Takes <paramref name="bytes"/> if there is room, and says whether there was.</summary>
    public bool TryTake(long bytes)
    {
        lock (_lock)
        {
            if (_used + bytes > ceilingBytes) return false;
            _used += bytes;
            return true;
        }
    }

    public void Give(long bytes)
    {
        lock (_lock) _used = Math.Max(0, _used - bytes);
    }
}

/// <summary>
/// One connection's retained message bodies, capped in bytes, deduplicated by content.
/// </summary>
/// <remarks>
/// <b>Bodies are evicted while their envelopes survive.</b> That asymmetry is the point: the timeline stays
/// complete and only the ability to expand an old frame is lost, which degrades legibly. Dropping entries
/// from the list instead would leave a record that reads as though less happened than did.
///
/// <para>
/// <b>Deduplicated because this protocol repeats itself relentlessly.</b> Full-document synchronisation
/// sends the whole file on every change, and the client flushes again on an opening parenthesis, on a
/// comma, and on save — so the same bytes go out several times a second, and successive snapshots of one
/// document differ by a character. Sharing identical bodies is the single highest-yield thing this class
/// does and it costs a dictionary.
/// </para>
/// </remarks>
public sealed class PayloadStore(CaptureLimits limits, PayloadBudget budget)
{
    private sealed class Entry
    {
        public required StoredBody Body { get; init; }
        public int References { get; set; }
    }

    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _byContent = [];
    private readonly Dictionary<long, string> _bySequence = [];
    private readonly Queue<long> _order = new();

    private long _bytes;
    private long _droppedBodies;
    private long _refusedBodies;

    /// <summary>Bytes retained by this connection.</summary>
    public long RetainedBytes { get { lock (_lock) return _bytes; } }

    /// <summary>Bodies discarded to make room, since this connection began.</summary>
    public long DroppedBodies { get { lock (_lock) return _droppedBodies; } }

    /// <summary>
    /// Bodies never retained at all, because there was no room to be made.
    /// </summary>
    /// <remarks>
    /// Counted apart from <see cref="DroppedBodies"/> because the two mean different things to a reader.
    /// A drop is this connection's own history rotating. A refusal is this connection being unable to
    /// record anything new, which at the global ceiling is somebody ELSE's traffic crowding it out — the
    /// one starvation the per-connection budgets do not prevent, and worth being able to name rather than
    /// leaving as an unexplained hole.
    /// </remarks>
    public long RefusedBodies { get { lock (_lock) return _refusedBodies; } }

    /// <summary>
    /// Retains a body against a sequence number, or declines to.
    /// </summary>
    /// <returns>
    /// False when there was no room even after evicting everything evictable — which happens when a single
    /// body is larger than the whole global ceiling. Reported rather than thrown: a capture that cannot
    /// hold one enormous frame must carry on holding the rest.
    /// </returns>
    public bool TryAdd(long sequence, ReadOnlySpan<byte> body)
    {
        var stored = Shorten(body, limits.FrameBytes);

        // Hashed over the WHOLE body, not the retained part. Two different large bodies that happen to
        // share a head and a tail would otherwise be treated as one, which is a wrong answer rather than a
        // lost optimisation.
        var content = Convert.ToHexString(SHA256.HashData(body));

        lock (_lock)
        {
            if (_byContent.TryGetValue(content, out var existing))
            {
                existing.References++;
                _bySequence[sequence] = content;
                _order.Enqueue(sequence);
                return true;
            }

            var cost = stored.RetainedBytes;

            while (_bytes + cost > limits.PayloadBytesPerConnection && EvictOldest()) { }
            if (_bytes + cost > limits.PayloadBytesPerConnection)
            {
                _refusedBodies++;
                return false;
            }

            while (!budget.TryTake(cost))
            {
                if (!EvictOldest())
                {
                    _refusedBodies++;
                    return false;
                }
            }

            _byContent[content] = new Entry { Body = stored, References = 1 };
            _bySequence[sequence] = content;
            _order.Enqueue(sequence);
            _bytes += cost;
            return true;
        }
    }

    /// <summary>The body retained for <paramref name="sequence"/>, or null if it was never held or has gone.</summary>
    public StoredBody? Find(long sequence)
    {
        lock (_lock)
        {
            return _bySequence.TryGetValue(sequence, out var content)
                && _byContent.TryGetValue(content, out var entry)
                    ? entry.Body
                    : null;
        }
    }

    /// <summary>Releases everything. Called when a capture ends, which is when the session does.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            budget.Give(_bytes);
            _bytes = 0;
            _byContent.Clear();
            _bySequence.Clear();
            _order.Clear();
        }
    }

    /// <summary>Caller holds the lock.</summary>
    private bool EvictOldest()
    {
        while (_order.Count > 0)
        {
            var sequence = _order.Dequeue();
            if (!_bySequence.Remove(sequence, out var content)) continue;
            if (!_byContent.TryGetValue(content, out var entry)) continue;

            entry.References--;
            if (entry.References > 0) continue;   // another sequence still points at these bytes

            _byContent.Remove(content);
            var freed = entry.Body.RetainedBytes;
            _bytes -= freed;
            budget.Give(freed);
            _droppedBodies++;
            return true;
        }

        return false;
    }

    /// <summary>
    /// The body, or its two ends when it is too big to keep whole.
    /// </summary>
    /// <remarks>
    /// Both ends rather than a prefix. A JSON document cut off at the front alone is unreadable — you
    /// cannot tell what closed and what did not — and in a full-document synchronisation frame the tail is
    /// where the recent edit usually is.
    /// </remarks>
    private static StoredBody Shorten(ReadOnlySpan<byte> body, int limit)
    {
        if (body.Length <= limit) return new StoredBody(body.ToArray(), null, body.Length);

        var half = limit / 2;
        return new StoredBody(body[..half].ToArray(), body[^half..].ToArray(), body.Length);
    }
}
