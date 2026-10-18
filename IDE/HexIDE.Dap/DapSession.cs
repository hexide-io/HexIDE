using System.Text.Json;
using System.Threading.Channels;

namespace HexIDE.Dap;

public enum DapSessionState { Created, Starting, Running, Stopped, Ended, Faulted }

/// <summary>A stop identity, scoped to one session. Never reuse frame/variable IDs across these identities.</summary>
public sealed record DapStop(Guid SessionId, long Generation, int? ThreadId, string Reason);
public sealed record DapSessionSnapshot(DapSessionState State, DapStop? Stop, Exception? Failure);
public sealed record DapSessionUpdate(DapSessionSnapshot Snapshot, DapEvent? Event);
public sealed record DapSourceBreakpoints(string Path, IReadOnlyList<int> Lines);

/// <summary>
/// Owns a connection (optionally its process), consumes its events, and coordinates one launch.
/// Consume Updates promptly on one background reader and dispatch UI work yourself. Inspection results
/// must still be checked with IsCurrent at the point they are applied to the UI.
/// </summary>
public sealed class DapSession : IAsyncDisposable
{
    private readonly DapConnection _connection;
    private readonly IAsyncDisposable _owner;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _commands = new(1, 1);
    private readonly TaskCompletionSource _initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Channel<DapSessionUpdate> _updates;
    private readonly TimeSpan _startupTimeout;
    private readonly Task _events;
    private Task? _dispose;
    private long _generation;
    private DapSessionSnapshot _snapshot = new(DapSessionState.Created, null, null);
    private JsonElement _capabilities = JsonSerializer.SerializeToElement(new { });

    public DapSession(DapConnection connection, TimeSpan? startupTimeout = null, int updateCapacity = 256)
        : this(connection, connection, startupTimeout, updateCapacity) { }

    private DapSession(DapConnection connection, IAsyncDisposable owner, TimeSpan? startupTimeout, int updateCapacity)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(updateCapacity);
        _startupTimeout = startupTimeout ?? TimeSpan.FromSeconds(60);
        if (_startupTimeout <= TimeSpan.Zero || _startupTimeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(startupTimeout));
        (_connection, _owner) = (connection, owner);
        _updates = Channel.CreateBounded<DapSessionUpdate>(updateCapacity);
        _events = Task.Run(ReadEventsAsync);
    }

    public static DapSession Start(DapProcessOptions options)
    {
        var process = DapProcess.Start(options);
        return new(process.Connection, process, null, 256) { ProcessCompletion = process.Completion };
    }

    public Guid Id { get; } = Guid.NewGuid();
    public DapSessionSnapshot Snapshot { get { lock (_sync) return _snapshot; } }
    public JsonElement Capabilities { get { lock (_sync) return _capabilities; } }
    public ChannelReader<DapSessionUpdate> Updates => _updates.Reader;
    public Task Completion => _events;
    public Task<DapProcessResult>? ProcessCompletion { get; private init; }
    public bool IsCurrent(DapStop stop) { lock (_sync) return _snapshot.Stop == stop && stop.SessionId == Id; }

    /// <summary>One-based lines and columns, native source paths. Launch arguments are adapter-specific.</summary>
    public async Task<IReadOnlyList<DapResponse>> LaunchAsync(string adapterId, JsonElement arguments,
        IReadOnlyList<DapSourceBreakpoints> breakpoints, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterId);
        if (arguments.ValueKind != JsonValueKind.Object) throw new ArgumentException("Launch arguments must be an object.", nameof(arguments));
        ArgumentNullException.ThrowIfNull(breakpoints);
        // Copy caller-owned collections before the first await.
        var sources = breakpoints.Select(source => new DapSourceBreakpoints(source.Path, source.Lines.ToArray())).ToArray();
        foreach (var source in sources)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(source.Path);
            if (source.Lines.Any(line => line < 1)) throw new ArgumentOutOfRangeException(nameof(breakpoints));
        }
        lock (_sync)
        {
            if (_snapshot.State != DapSessionState.Created) throw new InvalidOperationException("A session can launch only once.");
            Change(DapSessionState.Starting);
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_startupTimeout);
        var token = deadline.Token;
        Task<DapResponse>? launch = null;
        try
        {
            var initialize = await Request("initialize", new { clientID = "hexide", adapterID = adapterId,
                linesStartAt1 = true, columnsStartAt1 = true, pathFormat = "path" }, token).ConfigureAwait(false);
            lock (_sync) _capabilities = initialize.Body ?? JsonSerializer.SerializeToElement(new { });
            // Some adapters answer launch only AFTER configurationDone. Never await it before configuration.
            launch = _connection.RequestAsync("launch", arguments, token);
            var ready = _initialized.Task.WaitAsync(token);
            if (await Task.WhenAny(ready, launch).ConfigureAwait(false) == launch)
                await launch.ConfigureAwait(false); // observe an early refusal immediately
            await ready.ConfigureAwait(false);
            var replies = new List<DapResponse>();
            foreach (var source in sources)
                replies.Add(await Request("setBreakpoints", new { source = new { path = source.Path },
                    breakpoints = source.Lines.Select(line => new { line }).ToArray() }, token).ConfigureAwait(false));
            var filters = Capabilities.TryGetProperty("exceptionBreakpointFilters", out var advertised)
                ? advertised.EnumerateArray().Where(filter => filter.TryGetProperty("default", out var enabled) && enabled.ValueKind == JsonValueKind.True)
                    .Select(filter => filter.GetProperty("filter").GetString()).ToArray() : [];
            if (Capabilities.TryGetProperty("exceptionBreakpointFilters", out _))
                await Request("setExceptionBreakpoints", new { filters }, token).ConfigureAwait(false);
            lock (_sync)
                if (_snapshot.State == DapSessionState.Starting) Change(DapSessionState.Running);
            if (Supports("supportsConfigurationDoneRequest"))
                await Request("configurationDone", new { }, token).ConfigureAwait(false);
            await launch.ConfigureAwait(false);
            if (Snapshot.State == DapSessionState.Faulted)
                throw new IOException("Debug session failed during startup.", Snapshot.Failure);
            return replies;
        }
        catch (Exception exception)
        {
            Fail(exception);
            await DisposeAsync().ConfigureAwait(false);
            if (launch is not null) { try { await launch.ConfigureAwait(false); } catch { /* original failure wins */ } }
            throw;
        }
    }

    public bool Supports(string capability) => Capabilities.TryGetProperty(capability, out var value) && value.ValueKind == JsonValueKind.True;

    public Task<DapResponse> ThreadsAsync(DapStop stop, CancellationToken token = default) => Inspect(stop, "threads", new { }, token);
    public Task<DapResponse> ThreadsAsync(CancellationToken token = default)
    {
        if (Snapshot.State is not (DapSessionState.Running or DapSessionState.Stopped))
            throw new InvalidOperationException("The debug session is not active.");
        return Request("threads", new { }, token);
    }
    public Task<DapResponse> StackTraceAsync(DapStop stop, int threadId, CancellationToken token = default) => Inspect(stop, "stackTrace", new { threadId }, token);
    public Task<DapResponse> ScopesAsync(DapStop stop, int frameId, CancellationToken token = default) => Inspect(stop, "scopes", new { frameId }, token);
    public Task<DapResponse> VariablesAsync(DapStop stop, int variablesReference, CancellationToken token = default) => Inspect(stop, "variables", new { variablesReference }, token);

    private async Task<DapResponse> Inspect(DapStop stop, string command, object arguments, CancellationToken token)
    {
        CheckStop(stop);
        var reply = await Request(command, arguments, token).ConfigureAwait(false);
        CheckStop(stop);
        return reply;
    }

    private void CheckStop(DapStop stop)
    {
        if (!IsCurrent(stop)) throw new InvalidOperationException("This inspection belongs to an expired debug stop.");
    }

    /// <summary>Only continue/next/stepIn/stepOut. Invalidates inspection before sending, including adapters without continued events.</summary>
    public async Task ResumeAsync(DapStop stop, string command, int threadId, CancellationToken token = default)
    {
        if (command is not ("continue" or "next" or "stepIn" or "stepOut")) throw new ArgumentException("Not a resume command.", nameof(command));
        await _commands.WaitAsync(token).ConfigureAwait(false);
        long generation = 0;
        try
        {
            lock (_sync)
            {
                CheckStop(stop);
                token.ThrowIfCancellationRequested();
                Change(DapSessionState.Running);
                generation = _generation;
            }
            try { await Request(command, new { threadId }, token).ConfigureAwait(false); }
            catch (DapRequestException)
            {
                lock (_sync)
                    if (_generation == generation && _snapshot.State == DapSessionState.Running)
                    {
                        // A refusal is definitive, but all previously issued inspections remain stale.
                        Change(DapSessionState.Stopped, stop with { Generation = _generation + 1 });
                    }
                throw;
            }
            catch (Exception exception)
            {
                // Cancellation/timeout cannot establish whether the adapter executed the request.
                Fail(exception);
                await DisposeAsync().ConfigureAwait(false);
                throw;
            }
            // No post-response Running transition: a fast stopped event may already have arrived.
        }
        finally { _commands.Release(); }
    }

    public async Task PauseAsync(int threadId, CancellationToken token = default)
    {
        lock (_sync)
            if (_snapshot.State != DapSessionState.Running) throw new InvalidOperationException("The session is not running.");
        await Request("pause", new { threadId }, token).ConfigureAwait(false);
    }

    public async Task DisconnectAsync(CancellationToken token = default)
    {
        try { await Request("disconnect", new { terminateDebuggee = true }, token).ConfigureAwait(false); }
        finally { await DisposeAsync().ConfigureAwait(false); }
    }

    private Task<DapResponse> Request(string command, object arguments, CancellationToken token) =>
        _connection.RequestAsync(command, JsonSerializer.SerializeToElement(arguments), token);

    private async Task ReadEventsAsync()
    {
        try
        {
            await foreach (var message in _connection.Events.ReadAllAsync().ConfigureAwait(false))
            {
                lock (_sync)
                {
                    if (_snapshot.State == DapSessionState.Faulted) continue;
                    if (_snapshot.State == DapSessionState.Ended) { Publish(message); continue; }
                    switch (message.Event)
                    {
                        case "initialized": _initialized.TrySetResult(); break;
                        case "stopped":
                            var body = message.Body;
                            int? thread = body is { } b && b.TryGetProperty("threadId", out var id) ? id.GetInt32() : null;
                            var reason = body is { } r && r.TryGetProperty("reason", out var why) ? why.GetString() ?? "unknown" : "unknown";
                            Change(DapSessionState.Stopped, new(Id, _generation + 1, thread, reason), message: message);
                            continue;
                        case "continued": Change(DapSessionState.Running, message: message); continue;
                        case "terminated": Change(DapSessionState.Ended, message: message); continue;
                    }
                    Publish(message);
                }
            }
            var failure = await _connection.Completion.ConfigureAwait(false);
            lock (_sync)
                if (_snapshot.State is not (DapSessionState.Ended or DapSessionState.Faulted))
                    Change(DapSessionState.Faulted, failure: failure ?? new IOException("Debug adapter closed before termination."));
        }
        catch (EndOfStreamException) when (Snapshot.State == DapSessionState.Ended)
        {
            // The program has already terminated. Raw transport diagnostics remain on ProcessCompletion.
        }
        catch (Exception exception) { Fail(exception); }
        finally
        {
            // Wake a startup waiting for initialized even when the stream closes first.
            _initialized.TrySetCanceled();
            await _owner.DisposeAsync().ConfigureAwait(false);
            _updates.Writer.TryComplete();
        }
    }

    private void Change(DapSessionState state, DapStop? stop = null, Exception? failure = null, DapEvent? message = null)
    {
        ++_generation;
        _snapshot = new(state, stop, failure);
        Publish(message);
    }

    private void Publish(DapEvent? message)
    {
        if (!_updates.Writer.TryWrite(new(_snapshot, message)))
        {
            var failure = new IOException("Debug session update queue overflowed; consume Updates promptly.");
            Fail(failure);
            _ = CloseOverflowedConnectionAsync();
            throw failure;
        }
    }

    private async Task CloseOverflowedConnectionAsync()
    {
        try { await _connection.DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception) { Fail(exception); }
    }

    private void Fail(Exception exception)
    {
        lock (_sync)
        {
            if (_snapshot.State == DapSessionState.Faulted) return;
            ++_generation;
            _snapshot = new(DapSessionState.Faulted, null, exception);
            _updates.Writer.TryWrite(new(_snapshot, null));
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync) return new(_dispose ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        // Start asynchronously so no owner disposal runs under the snapshot lock.
        await Task.Yield();
        lock (_sync)
        {
            if (_snapshot.State is not (DapSessionState.Ended or DapSessionState.Faulted))
            {
                ++_generation;
                _snapshot = new(DapSessionState.Ended, null, null);
                _updates.Writer.TryWrite(new(_snapshot, null));
            }
        }
        await _owner.DisposeAsync().ConfigureAwait(false);
        await _events.ConfigureAwait(false);
    }
}
