namespace HexIDE.Conversations;

/// <summary>
/// One connection's envelopes: a bounded ring that keeps the beginning and forgets the middle.
/// </summary>
/// <remarks>
/// <b>Drop-oldest, with the handshake pinned.</b> Stopping when full leaves a developer holding the least
/// interesting part of a long session. Plain drop-oldest discards <c>initialize</c> first, which is the one
/// thing that must never go. So the first <see cref="CaptureLimits.PrologueEntries"/> are kept out of the
/// rotation entirely and the ring turns over behind them.
///
/// <para>
/// <b>A drop is counted and reported, never silent.</b> A record that truncates without saying so reads
/// exactly like a complete one, which is the same shape as a test guard that skips instead of failing — a
/// failure this project has already paid for once.
/// </para>
///
/// <para>
/// Thread-safe, because frames arrive on whatever thread the transport is reading on and a capture must
/// never make the caller wait to find out. Everything here is O(1) under one short lock.
/// </para>
/// </remarks>
public sealed class ConversationRing(CaptureLimits limits)
{
    private readonly object _lock = new();

    /// <summary>The opening entries, never evicted.</summary>
    private readonly List<ConversationEnvelope> _prologue = new(Math.Min(limits.PrologueEntries, 256));

    /// <summary>Everything after the prologue, oldest first, evicted from the front.</summary>
    private readonly Queue<ConversationEnvelope> _recent = new();

    private long _dropped;

    /// <summary>How many envelopes were discarded to make room, since this connection began.</summary>
    public long Dropped { get { lock (_lock) return _dropped; } }

    /// <summary>How many are held right now, prologue included.</summary>
    public int Count { get { lock (_lock) return _prologue.Count + _recent.Count; } }

    public void Add(ConversationEnvelope envelope)
    {
        lock (_lock)
        {
            if (_prologue.Count < limits.PrologueEntries)
            {
                _prologue.Add(envelope);
                return;
            }

            _recent.Enqueue(envelope);

            // The prologue is part of the budget rather than extra on top of it. Otherwise a limit means
            // "this many, plus two hundred", which is not what anybody set.
            while (_prologue.Count + _recent.Count > limits.EnvelopeEntries)
            {
                _recent.Dequeue();
                _dropped++;
            }
        }
    }

    /// <summary>
    /// Replaces an envelope already recorded, matched by sequence, and does nothing if it has been dropped.
    /// </summary>
    /// <remarks>
    /// A request is recorded when it is sent, because a request that never comes back must still appear —
    /// that absence is a finding. Its outcome and elapsed time are only knowable later, so the entry is
    /// completed in place rather than a second one being written, which would double every request in a
    /// timeline whose whole job is to be read in order.
    ///
    /// <para>
    /// Silently doing nothing when the entry is gone is correct rather than lazy: a long-running request
    /// whose envelope has aged out of a busy ring is exactly the case, and there is nothing left to
    /// complete.
    /// </para>
    /// </remarks>
    public void Complete(long sequence, ConversationOutcome outcome, TimeSpan elapsed)
    {
        lock (_lock)
        {
            for (var i = _prologue.Count - 1; i >= 0; i--)
            {
                if (_prologue[i].Sequence != sequence) continue;
                _prologue[i] = _prologue[i] with { Outcome = outcome, Elapsed = elapsed };
                return;
            }

            // A queue cannot be indexed, and rebuilding it per completion would be quadratic on a busy
            // connection. Copying once into an array and back is O(n) but happens only when the entry is
            // not in the prologue, and only for a sequence that is still resident.
            if (!_recent.Any(e => e.Sequence == sequence)) return;

            var all = _recent.ToArray();
            _recent.Clear();
            foreach (var e in all)
            {
                _recent.Enqueue(e.Sequence == sequence
                    ? e with { Outcome = outcome, Elapsed = elapsed }
                    : e);
            }
        }
    }

    /// <summary>Everything held, in the order it was recorded.</summary>
    public IReadOnlyList<ConversationEnvelope> Snapshot()
    {
        lock (_lock)
        {
            var all = new List<ConversationEnvelope>(_prologue.Count + _recent.Count);
            all.AddRange(_prologue);
            all.AddRange(_recent);
            return all;
        }
    }
}
