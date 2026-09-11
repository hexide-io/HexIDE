using System.Diagnostics;
using System.Threading.Channels;

namespace HexIDE.Conversations;

/// <summary>
/// Everything recorded about every connection, and the only thing a tap talks to.
/// </summary>
/// <remarks>
/// <b>The tap must never make the protocol wait, and the measurements are unambiguous about why.</b> A
/// capture point in this pipeline is fully in-line: twenty-five milliseconds of work per call turned ten
/// round trips into six hundred. So <see cref="Record"/> does the least possible — decide, copy, hand off —
/// and every expensive thing happens on a pump that nothing is waiting for.
///
/// <para>
/// <b>Handing off is also why sequence numbers are allocated here rather than at the tap.</b> A single
/// reader on a channel preserves the order things were written in, and the pipeline itself already
/// guarantees frames arrive in order, so numbering in the pump costs nothing and keeps the tap cheaper.
/// </para>
///
/// <para>
/// <b>Overflow drops rather than blocks.</b> A full queue means the pump is behind; waiting for it would
/// push that delay directly into the language service. The count is kept and reported, because a record
/// that quietly loses frames reads exactly like one that had fewer to lose.
/// </para>
/// </remarks>
public sealed class ConversationLog : IAsyncDisposable
{
    private sealed class Connection(CaptureLimits limits, PayloadBudget budget)
    {
        public ConversationRing Ring { get; } = new(limits);
        public PayloadStore Bodies { get; } = new(limits, budget);

        /// <summary>
        /// Whether message bodies are being retained for this connection.
        /// </summary>
        /// <remarks>
        /// Read on the RPC thread on every single frame, so it is a plain volatile field rather than
        /// anything that takes a lock. Being one frame late either way is harmless; contending with the
        /// language service is not.
        /// </remarks>
        public volatile bool Armed;

        /// <summary>Frames seen at the tap, used only to recognise the opening of a connection.</summary>
        public int Seen;

        /// <summary>Requests sent and not yet answered, keyed by who started them.</summary>
        public Dictionary<(ConversationDirection Origin, string Id), (long Sequence, long StartedAt)> Outstanding { get; } = [];
    }

    private readonly record struct Pending(
        string ConnectionId,
        DateTimeOffset Timestamp,
        ConversationDirection Direction,
        ConversationEntryKind Kind,
        string? Method,
        string? CorrelationId,
        int SizeBytes,
        byte[]? Body,
        string? Detail,
        // A marker rather than an entry. The pump reads strictly in order, so a fence completing proves
        // everything written before it has been processed — which polling a queue length does not, since
        // an item can be out of the queue and still in flight.
        TaskCompletionSource? Fence = null,

        // The second marker, and it rides the queue for the same reason the fence does: clearing touches
        // per-connection state the pump owns, so doing it from a caller's thread would race the pump
        // rather than order against it. `ConnectionId` empty means every connection.
        TaskCompletionSource<int>? ClearTo = null);

    private readonly CaptureLimits _limits;
    private readonly TimeProvider _clock;
    private readonly PayloadBudget _budget;
    private readonly Channel<Pending> _queue;
    private readonly Task _pump;
    private readonly Dictionary<string, Connection> _connections = [];
    private readonly Dictionary<string, CaptureLimits>? _perConnection;
    private readonly object _connectionsLock = new();

    private long _sequence;
    private long _queueDropped;

    /// <param name="limits">What a connection may hold when it has not overridden anything.</param>
    /// <param name="perConnection">
    /// Overrides by connection id, for the servers whose configuration asked for one.
    /// </param>
    /// <remarks>
    /// <b>Per-connection budgets rather than one pool, measured rather than assumed.</b> Over an identical
    /// twenty-edit script one server returned roughly a hundred times another's inbound bytes, almost
    /// entirely completion replies. Under a single budget the noisy server silently evicts the quiet one's
    /// history, and a single drop count reports a loss nobody can attribute to anything.
    ///
    /// <para>
    /// Overrides are clamped here as well as where they were read. Defence in depth rather than
    /// duplication: this constructor is public and a caller who never went near the configuration file can
    /// still not hand it a payload budget above the ceiling. The configuration path is the one that
    /// <em>reports</em> a correction, because that is where a person wrote the value.
    /// </para>
    /// </remarks>
    /// <param name="armEveryConnection">
    /// Arms every connection as it first appears, rather than waiting to be asked.
    /// </param>
    /// <remarks>
    /// <b>A default rather than a loop over the configured servers, and the difference matters.</b> Arming
    /// by id at startup can only reach ids somebody has already written down, and a connection is created
    /// lazily by whoever first needs one — for a server respawned after a crash, or for a registration that
    /// came from a file this process has not read. A default applies to every connection there turns out to
    /// be, which is what "arm capture at start" has to mean if it is to survive the restart-per-iteration
    /// development loop it exists for.
    ///
    /// <para>
    /// It does not make arming permanent. A connection armed this way disarms like any other; the default
    /// only decides what a connection starts as.
    /// </para>
    /// </remarks>
    public ConversationLog(
        CaptureLimits? limits = null,
        TimeProvider? clock = null,
        int queueDepth = 4096,
        IReadOnlyDictionary<string, CaptureLimits>? perConnection = null,
        bool armEveryConnection = false)
    {
        ArmsEveryConnection = armEveryConnection;
        _limits = (limits ?? CaptureLimits.Default).Clamped(out var adjustments);
        Adjustments = adjustments;

        _perConnection = perConnection is null
            ? null
            : perConnection.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Clamped(out _),
                StringComparer.Ordinal);
        _clock = clock ?? TimeProvider.System;
        _budget = new PayloadBudget(_limits.GlobalPayloadBytes);

        _queue = Channel.CreateBounded<Pending>(new BoundedChannelOptions(queueDepth)
        {
            // `Wait` paired with `TryWrite`, which is NOT the obvious combination and is the correct one.
            //
            // `DropWrite` sounds like what is wanted and is a trap: it discards the item and reports
            // SUCCESS, so a drop counter built on the return value can never increment and the record
            // loses frames in exactly the silent way this design forbids everywhere else. `Wait` only
            // affects the awaiting overload; `TryWrite` on a full channel returns false immediately
            // without blocking, which is both halves of what a tap needs — never wait, always know.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });

        _pump = Task.Run(PumpAsync);
    }

    /// <summary>What the configured limits had to be corrected to, if anything.</summary>
    public IReadOnlyList<string> Adjustments { get; }

    /// <summary>Frames the capture could not keep up with. Never silent.</summary>
    public long QueueDropped => Interlocked.Read(ref _queueDropped);

    public CaptureLimits Limits => _limits;

    /// <summary>
    /// What one connection may hold: its own override where it has one, the shared default otherwise.
    /// </summary>
    /// <remarks>
    /// Public because an export has to state the limits a record was taken under. A reader looking at a
    /// short timeline needs to know whether it is short because little happened or because the budget for
    /// that server was small, and those two are indistinguishable without this.
    /// </remarks>
    public CaptureLimits LimitsFor(string connectionId) =>
        _perConnection is not null && _perConnection.TryGetValue(connectionId, out var own) ? own : _limits;

    /// <summary>Whether bodies are being retained for a connection.</summary>
    public bool IsArmed(string connectionId) => Of(connectionId).Armed;

    /// <summary>
    /// How many frames at the start of a connection are kept in full whatever the arming says.
    /// </summary>
    /// <remarks>
    /// Generous rather than exact. The handshake is an initialize request and its reply, an initialized
    /// notification, and whatever a server volunteers immediately afterwards — a banner, a capability
    /// registration attempt, an early diagnostic. Counting precisely would mean recognising methods, and
    /// this needs to be decided before a frame has been read.
    /// </remarks>
    public const int OpeningFrames = 8;

    /// <summary>
    /// Whether this frame's body should be kept: because someone armed the connection, or because the
    /// connection has only just started.
    /// </summary>
    /// <remarks>
    /// <b>The handshake is kept unconditionally, and it is the one exception to arming.</b> Servers start
    /// lazily, when a document of their language is first opened, so arming after the fact provably cannot
    /// reach an <c>initialize</c> that has already happened — and several of the costliest defects in this
    /// project's history turn on what that exchange contained. The cost is fixed and tiny: a handful of
    /// frames, once per process.
    ///
    /// <para>
    /// Decided by position rather than by method, deliberately. Recognising <c>initialize</c> would mean
    /// reading the frame first and then deciding whether to copy it, which depends on the inbound buffer
    /// still being valid after the inner formatter has run — something nothing here has measured. Counting
    /// frames needs no such assumption.
    /// </para>
    ///
    /// <para>
    /// Called once per frame in each direction, so the count advances roughly twice per exchange. That is
    /// why the allowance is generous rather than precise.
    /// </para>
    /// </remarks>
    public bool ShouldKeepBody(string connectionId)
    {
        var connection = Of(connectionId);
        if (connection.Armed) return true;

        // Saturating, so a long-lived connection cannot roll this counter over and start capturing again.
        var seen = Interlocked.Increment(ref connection.Seen);
        if (seen > OpeningFrames) Interlocked.Exchange(ref connection.Seen, OpeningFrames + 1);
        return seen <= OpeningFrames;
    }

    /// <summary>
    /// Tells the record that this connection is being established again, so its opening counts afresh.
    /// </summary>
    /// <remarks>
    /// <b>Without this the handshake rule quietly stops applying after the first connection.</b> The
    /// allowance is a frame count, and a respawned server produces a whole new handshake against a counter
    /// that has already run out — so the one exchange the rule exists to protect would be kept on the first
    /// connection and dropped on every one after it.
    ///
    /// <para>
    /// Arming is deliberately NOT reset. It belongs to the connection rather than to the process, and a
    /// crash is what somebody armed capture to watch — taking the tool away at that moment would take it
    /// away exactly when it was about to be useful.
    /// </para>
    /// </remarks>
    public void Reconnecting(string connectionId) => Interlocked.Exchange(ref Of(connectionId).Seen, 0);

    /// <summary>
    /// Starts or stops retaining bodies for one connection.
    /// </summary>
    /// <remarks>
    /// Per connection, because several servers can be attached and a developer is nearly always chasing
    /// one of them. Arming everything would multiply the only expensive part of this for no gain.
    /// </remarks>
    public void Arm(string connectionId, bool armed) => Of(connectionId).Armed = armed;

    /// <summary>Whether a connection is armed the moment it first appears.</summary>
    public bool ArmsEveryConnection { get; }

    /// <summary>
    /// Every connection the log knows about, whether or not it currently holds anything for them.
    /// </summary>
    /// <remarks>
    /// <b>Measured need, not symmetry.</b> A caller reporting what is being recorded used to derive the
    /// list from the envelopes present, which is the same answer until somebody clears the record — and
    /// then it says there are no connections at all, while they are alive and armed. An agent that cleared
    /// a misspelled id and one that cleared a real one got the identical reply, which is the exact
    /// confusion reporting the state was meant to remove.
    /// </remarks>
    public IReadOnlyList<string> ConnectionIds
    {
        get { lock (_connectionsLock) return [.. _connections.Keys]; }
    }

    /// <summary>
    /// Discards a connection's record, or every connection's, keeping whatever was armed armed.
    /// </summary>
    /// <remarks>
    /// <b>Arming survives a clear, and separating the two is the point.</b> Arming is a decision somebody
    /// made about what to watch; the record is what has been watched so far. A loop that exercises a
    /// feature, reads the record, clears it and exercises the next one would be pointless if clearing also
    /// stopped the recording.
    ///
    /// <para>
    /// What is <em>not</em> reset is how many frames the connection has seen. The opening allowance exists
    /// because a handshake cannot be captured retrospectively, and a clear does not make a connection new
    /// again — treating it as new would hand a second opening allowance to a server that has been running
    /// for an hour.
    /// </para>
    ///
    /// <para>
    /// Ordered through the pump rather than applied in place, so it cannot race a frame being recorded.
    /// Everything written before the call is discarded; anything written after it survives.
    /// </para>
    /// </remarks>
    /// <returns>How many envelopes were discarded, so a caller can say what it did.</returns>
    public async Task<int> ClearAsync(string? connectionId = null)
    {
        var done = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await _queue.Writer.WriteAsync(new Pending(
                connectionId ?? "", default, default, default, null, null, 0, null, null,
                ClearTo: done)).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            // The log is going away and disposal clears everything anyway.
            return 0;
        }

        return await done.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Records one thing that happened. Called on the RPC thread; does as little as possible.
    /// </summary>
    /// <param name="body">
    /// The bytes, which the caller must have copied already — a buffer handed to a tap is invalid the
    /// instant the tap returns, measured across forty-two held sequences, all of which threw on re-read.
    /// Null when nothing was retained, which is the ordinary case for an unarmed connection.
    /// </param>
    public void Record(
        string connectionId,
        ConversationDirection direction,
        ConversationEntryKind kind,
        string? method,
        string? correlationId,
        int sizeBytes,
        byte[]? body = null,
        string? detail = null)
    {
        var pending = new Pending(
            connectionId, _clock.GetUtcNow(), direction, kind, method, correlationId, sizeBytes, body, detail);

        if (!_queue.Writer.TryWrite(pending)) Interlocked.Increment(ref _queueDropped);
    }

    /// <summary>Everything held, across every connection, in the order it happened.</summary>
    /// <remarks>
    /// Ordered by sequence rather than by timestamp. Connections are recorded independently and a clock has
    /// finite resolution, so timestamps tie; the sequence is a total order by construction and survives
    /// being merged.
    /// </remarks>
    public IReadOnlyList<ConversationEnvelope> Snapshot(string? connectionId = null)
    {
        List<Connection> connections;
        lock (_connectionsLock)
        {
            connections = connectionId is null
                ? [.. _connections.Values]
                : _connections.TryGetValue(connectionId, out var one) ? [one] : [];
        }

        return [.. connections.SelectMany(c => c.Ring.Snapshot()).OrderBy(e => e.Sequence)];
    }

    /// <summary>The body retained for a sequence, or null if it was never held or has been evicted.</summary>
    public StoredBody? Body(string connectionId, long sequence) => Of(connectionId).Bodies.Find(sequence);

    /// <summary>What this connection has lost, and to what.</summary>
    public (long Envelopes, long Bodies, long Refused) Losses(string connectionId)
    {
        var connection = Of(connectionId);
        return (connection.Ring.Dropped, connection.Bodies.DroppedBodies, connection.Bodies.RefusedBodies);
    }

    /// <summary>Waits until everything recorded so far has been processed. For tests, and for export.</summary>
    /// <remarks>
    /// Sends a fence through the queue rather than watching its length. An item can be out of the queue and
    /// still being processed, so a length of zero proves nothing; a fence cannot be reached until
    /// everything written ahead of it has been.
    ///
    /// <para>
    /// <b>The fence waits for room, and this is the one place in the capture that is allowed to.</b> The tap
    /// never waits — it writes with <c>TryWrite</c> and counts what it could not place, because a language
    /// service must not be slowed by a diagnostic. A drain is the opposite kind of call: waiting is the
    /// entire request. An earlier version used <c>TryWrite</c> here too and returned a completed task when
    /// the queue was full, which meant a caller asking "is everything processed" could be told yes while a
    /// frame was still in flight — recorded nowhere, counted nowhere, and appearing for all the world like
    /// a hole in the accounting. It was measured as exactly that: nine hundred and ninety-nine frames of a
    /// thousand, on a CI runner, against a deliberately tiny queue.
    /// </para>
    /// </remarks>
    public async Task DrainAsync()
    {
        var fence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await _queue.Writer.WriteAsync(new Pending(
                "", default, default, default, null, null, 0, null, null, fence)).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            // The log is being disposed. There is nothing left to drain towards, and dispose releases any
            // fence it finds still queued, so nobody is left waiting either way.
            return;
        }

        await fence.Task.ConfigureAwait(false);
    }

    private Connection Of(string connectionId)
    {
        lock (_connectionsLock)
        {
            if (_connections.TryGetValue(connectionId, out var existing)) return existing;
            var created = new Connection(LimitsFor(connectionId), _budget) { Armed = ArmsEveryConnection };
            _connections[connectionId] = created;
            return created;
        }
    }

    private async Task PumpAsync()
    {
        await foreach (var pending in _queue.Reader.ReadAllAsync())
        {
            // Nothing in here may throw into the channel loop: a dead pump would take the whole record
            // with it and the language service would carry on none the wiser.
            try { Process(pending); }
            catch (Exception) { /* a capture is never worth breaking anything for */ }
        }
    }

    private void Process(Pending pending)
    {
        if (pending.Fence is { } fence)
        {
            fence.TrySetResult();
            return;
        }

        if (pending.ClearTo is { } cleared)
        {
            cleared.TrySetResult(ClearOnPump(pending.ConnectionId));
            return;
        }

        var connection = Of(pending.ConnectionId);
        var sequence = ++_sequence;   // single reader, so no interlock needed

        if (TryComplete(connection, pending)) return;

        connection.Ring.Add(new ConversationEnvelope(
            sequence,
            pending.ConnectionId,
            pending.Timestamp,
            pending.Direction,
            pending.Kind,
            pending.Method,
            pending.CorrelationId,
            pending.SizeBytes,
            Detail: pending.Detail));

        if (pending.Kind == ConversationEntryKind.Request && pending.CorrelationId is { } id)
            connection.Outstanding[(pending.Direction, id)] = (sequence, Stopwatch.GetTimestamp());

        if (pending.Body is { } body) connection.Bodies.TryAdd(sequence, body);
    }

    /// <summary>
    /// Completes the request a response answers, if this is one.
    /// </summary>
    /// <remarks>
    /// <b>Keyed on who STARTED the exchange, not on who sent this frame.</b> Ids collide across directions
    /// — measured, a server-initiated request numbered 2 arriving while our own outbound request 2 was
    /// still open — so pairing on the id alone merges two unrelated messages. A response therefore looks up
    /// the opposite direction from its own.
    /// </remarks>
    private static bool TryComplete(Connection connection, Pending pending)
    {
        if (pending.Kind is not (ConversationEntryKind.Response or ConversationEntryKind.ErrorResponse)) return false;
        if (pending.CorrelationId is not { } id) return false;

        var origin = pending.Direction == ConversationDirection.Received
            ? ConversationDirection.Sent
            : ConversationDirection.Received;

        if (!connection.Outstanding.Remove((origin, id), out var started)) return false;

        connection.Ring.Complete(
            started.Sequence,
            pending.Kind == ConversationEntryKind.ErrorResponse
                ? ConversationOutcome.Failed
                : ConversationOutcome.Answered,
            Stopwatch.GetElapsedTime(started.StartedAt));

        // The response is not itself an entry. It is the second half of one, and a timeline that showed
        // both would double every request in a view whose entire job is to be read in order.
        return true;
    }

    /// <summary>
    /// The pump-thread half of <see cref="ClearAsync"/>. Never called from anywhere else.
    /// </summary>
    /// <remarks>
    /// <c>Outstanding</c> is the reason this cannot be done in place: it is a plain dictionary that only
    /// the single reader touches, so it carries no lock to take. Clearing it matters — its entries name
    /// sequence numbers that no longer exist, and a reply arriving afterwards would otherwise be paired
    /// against a vanished envelope.
    /// </remarks>
    private int ClearOnPump(string connectionId)
    {
        List<Connection> targets;
        lock (_connectionsLock)
        {
            targets = connectionId.Length == 0
                ? [.. _connections.Values]
                : _connections.TryGetValue(connectionId, out var one) ? [one] : [];
        }

        var discarded = 0;
        foreach (var connection in targets)
        {
            discarded += connection.Ring.Count;
            connection.Ring.Clear();
            connection.Bodies.Clear();
            connection.Outstanding.Clear();
        }

        return discarded;
    }

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        try { await _pump; } catch (Exception) { /* shutting down */ }

        // Anything still waiting on a fence that was never reached is released rather than left hanging.
        while (_queue.Reader.TryRead(out var leftover)) leftover.Fence?.TrySetResult();

        lock (_connectionsLock)
        {
            foreach (var connection in _connections.Values) connection.Bodies.Clear();
            _connections.Clear();
        }
    }
}
