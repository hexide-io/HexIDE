using System.Collections.Concurrent;
using System.Text.Json;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

using HexIDE.Conversations;

namespace HexIDE.Lsp;

public sealed class VBLspClient : ILspClient
{
    private readonly ILspTransport _transport;
    private readonly ILogger<VBLspClient> _logger;
    // Currently-open documents (uri -> latest version+text), so they can be replayed after a reconnect.
    private readonly ConcurrentDictionary<string, TrackedDocument> _openDocuments = new();
    // What each source currently says about each document. This connection's server is one source; an
    // external compiler injecting through this client is another, and neither may erase the other.
    private readonly DiagnosticLedger _diagnostics = new();
    private readonly object _reconnectGate = new();
    private JsonRpc? _rpc;
    private volatile bool _initialized;

    // What the connected server said it supports, kept exactly as it arrived. Raw is the single source of
    // truth: a typed view is one Deserialize away, whereas a summary cannot be turned back into the answer.
    // Cleared alongside _initialized — a capability set surviving a reconnect would describe the previous
    // server, and the whole point of the seam is that the next one may be a different product.
    private volatile CapabilitySnapshot? _capabilities;

    public JsonElement? AdvertisedCapabilities => _capabilities?.Value;

    /// <summary>
    /// Wraps the advertised capabilities so the field can be a reference. <c>JsonElement?</c> is a
    /// multi-word struct, so a bare field could be read half-updated by another thread — and it cannot be
    /// marked volatile for exactly that reason. Swapping an immutable reference is atomic, which is what
    /// this needs: written once during initialize, read from wherever a feature is requested.
    /// </summary>
    private sealed record CapabilitySnapshot(JsonElement Value);
    private volatile bool _stopping;
    private Task _reconnectTask = Task.CompletedTask;   // the running reconnect loop (or completed)
    private CancellationTokenSource? _reconnectCts;

    private sealed record TrackedDocument(int Version, string Text);

    public event EventHandler<PublishDiagnosticsParams>? DiagnosticsPublished;
    public event EventHandler<ShowMessageParams>? MessageShown;
    public event EventHandler<LogMessageParams>? MessageLogged;
    public event EventHandler<LogTraceParams>? TraceReceived;
    public event EventHandler? StateChanged;

    private ServerIdentity? _identity;

    /// <summary>What the server called itself at initialize. Cleared whenever the connection drops, so
    /// it never outlives the thing it describes.</summary>
    public ServerIdentity? ReportedIdentity => _identity;

    private LanguageConnectionAttempt? _attempt;

    /// <summary>The chain of the last connect attempt, and where it stopped.</summary>
    public LanguageConnectionAttempt? LastAttempt => _attempt;

    /// <summary>
    /// How many documents this client currently has open on the server.
    /// </summary>
    /// <remarks>
    /// Worth surfacing because zero is a real answer with two quite different causes: nothing of this
    /// language is open, or documents ARE open and are not reaching the server. hexide-io/HexIDE#273 is
    /// the second case, and it is otherwise indistinguishable from a healthy idle connection.
    /// </remarks>
    public int OpenDocumentCount
    {
        get { lock (_openDocuments) return _openDocuments.Count; }
    }

    /// <summary>The workspace root actually sent at initialize, or null if none was.</summary>
    public string? SentWorkspaceRootUri { get; private set; }

    /// <summary>Requests declined because the server never advertised them. A snapshot of the keys: the
    /// backing store is a ConcurrentDictionary written from request paths on other threads, so no lock is
    /// needed and none is taken.</summary>
    public IReadOnlyList<string> DeclinedCapabilities => [.. _warnedCapabilities.Keys];

    /// <summary>
    /// Records one connect attempt as it happens.
    /// </summary>
    /// <remarks>
    /// Built forward rather than reconstructed afterwards: by the time a caller sees a null handler or a
    /// caught exception, which of five different things went wrong is no longer recoverable from anything
    /// the client keeps. The stopwatch is per attempt, so a hung handshake reads as the duration it hung
    /// for rather than as a wall-clock timestamp nobody can subtract in their head.
    /// </remarks>
    private sealed class AttemptRecorder
    {
        private readonly List<LanguageConnectionStep> _steps = [];
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
        private LanguageConnectionStage _reached = LanguageConnectionStage.NotAttempted;

        public void Reached(LanguageConnectionStage stage)
        {
            _reached = stage;
            _steps.Add(new LanguageConnectionStep(
                stage, LanguageConnectionStepOutcome.Reached, _clock.Elapsed, null));
        }

        public LanguageConnectionAttempt StoppedAt(
            LanguageConnectionStage stage, string? detail,
            LanguageConnectionStepOutcome outcome = LanguageConnectionStepOutcome.StoppedHere)
        {
            // A stage is one rung. Reaching it and then stopping there is the SAME rung changing outcome,
            // not two events -- appending would render "Connecting" twice, once with a tick and once with
            // a cross, which reads as two attempts. Caught by looking at it, not by a test.
            var i = _steps.FindLastIndex(s => s.Stage == stage);
            var step = new LanguageConnectionStep(stage, outcome, _clock.Elapsed, detail);
            if (i >= 0) _steps[i] = step; else _steps.Add(step);
            return Build(stage);
        }

        public LanguageConnectionAttempt Build(LanguageConnectionStage? reached = null) =>
            new(reached ?? _reached, _steps, _startedAt);
    }

    /// <summary>Raised outside any lock, and never allowed to take the client down with a bad handler:
    /// this fires from transport and RPC callbacks, where an exception has nowhere useful to go.</summary>
    private void RaiseStateChanged()
    {
        try { StateChanged?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { _logger.LogDebug(ex, "A StateChanged handler threw"); }
    }

    // Running only when the underlying transport is connected AND the initialize handshake completed.
    public bool IsRunning => _transport.IsAlive && _initialized;

    /// <param name="languageId">
    /// What THIS server is told its documents are, in <c>didOpen</c>. Given rather than looked up: a global
    /// table would force two servers claiming one extension to agree about what it is called, and each has
    /// its own connection, so neither has to be wrong.
    /// </param>
    /// <param name="workspace">
    /// Where this server should think it is working. Consulted at initialize rather than held as a value,
    /// because the server starts lazily and which project is open by then is not knowable here. Null when
    /// the caller has no workspace to offer, in which case no root is sent — which is honest, and what the
    /// protocol says to do.
    /// </param>
    /// <param name="initializeTimeout">
    /// How long to wait for the server's <c>initialize</c> reply before abandoning the handshake. Injected
    /// so tests need not wait out the real one; leave it unset everywhere else.
    /// </param>
    public VBLspClient(
        ILspTransport transport, ILogger<VBLspClient> logger, string languageId,
        ILspWorkspace? workspace = null, TimeSpan? initializeTimeout = null,
        string trace = LspTraceValue.Off,
        ConversationLog? capture = null, string? connectionId = null)
    {
        _transport = transport;
        _logger = logger;
        _languageId = languageId;
        _workspace = workspace;
        _initializeTimeout = initializeTimeout ?? DefaultInitializeTimeout;
        _trace = LspTraceValue.Normalise(trace) ?? LspTraceValue.Off;
        _capture = capture;
        _connectionId = connectionId;
    }

    /// <summary>
    /// Where this connection's conversation is recorded, and under what name. Null when nothing is
    /// recording, which is every test that does not care and every construction site that predates this.
    /// </summary>
    /// <remarks>
    /// The log outlives this object deliberately. A server that dies and is respawned gets a new client,
    /// and its record has to be the same record — a connection's history is about the connection, not about
    /// whichever process happened to be serving it at the time.
    /// </remarks>
    private readonly ConversationLog? _capture;
    private readonly string? _connectionId;

    /// <summary>Records one thing, if anything is listening. Never throws into a caller.</summary>
    private void Note(
        ConversationDirection direction, ConversationEntryKind kind, string? method, string? detail)
    {
        if (_capture is not { } capture || _connectionId is not { } id) return;
        try { capture.Record(id, direction, kind, method, null, 0, null, detail); }
        catch (Exception) { /* a capture is never worth breaking a connection for */ }
    }

    /// <summary>
    /// The level this connection is currently asking for. Not readonly, because it is also what a later
    /// <c>$/setTrace</c> changed it to.
    /// </summary>
    /// <remarks>
    /// Kept here rather than only passed through to <c>initialize</c> because a transport can reconnect,
    /// and re-handshaking at the level configured months ago would quietly undo a level the developer set
    /// two minutes earlier. The connection is the thing that owns this, not the process.
    /// </remarks>
    private string _trace;

    private readonly string _languageId;
    private readonly ILspWorkspace? _workspace;
    private readonly TimeSpan _initializeTimeout;

    /// <summary>
    /// Deliberately generous. The cost of waiting too long is a late log line; the cost of waiting too
    /// little is tearing down a server that was merely slow to start — which would be a worse bug than the
    /// one this bounds, because it would strike healthy setups on cold or loaded machines. Nothing here is
    /// on a user-visible path: the handshake runs fire-and-forget from startup.
    /// </summary>
    private static readonly TimeSpan DefaultInitializeTimeout = TimeSpan.FromSeconds(30);

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        // Channel-level death signal (e.g. stdio server process exit). WebSocket drops surface via
        // JsonRpc.Disconnected instead, wired per-connection in ConnectAndInitializeAsync.
        _transport.Closed += OnTransportClosed;

        await ConnectAndInitializeAsync(cancellationToken);
    }

    /// <summary>Connects the transport, binds JSON-RPC, performs the initialize handshake, and
    /// replays any tracked documents. Used for the first connect and for every reconnect.</summary>
    private async Task ConnectAndInitializeAsync(CancellationToken cancellationToken)
    {
        // A FRESH formatter per connection: StreamJsonRpc formatters carry per-connection state and
        // cannot be reused across JsonRpc instances — reusing one silently breaks every reconnect.
        // It still carries the AOT-safe LspJsonContext config so serialization is identical each time.
        var formatter = new SystemTextJsonFormatter
        {
            JsonSerializerOptions = new JsonSerializerOptions(LspJsonContext.Default.Options)
            {
                PropertyNameCaseInsensitive = true,
            }
        };
        // Wrapped, not subclassed: every serialization member on the formatter is a non-virtual interface
        // implementation, so an override does not compile and the `new` that does is never called.
        IJsonRpcMessageFormatter wired = _capture is { } log && _connectionId is { } captureId
            ? new CapturingFormatter(formatter, log, captureId)
            : formatter;

        var attempt = new AttemptRecorder();
        attempt.Reached(LanguageConnectionStage.Connecting);
        Note(ConversationDirection.Local, ConversationEntryKind.Lifecycle, null, "connecting");

        var handler = await _transport.ConnectAsync(wired, cancellationToken);
        if (handler is null)
        {
            Note(ConversationDirection.Local, ConversationEntryKind.Lifecycle, null,
                $"could not connect: {_transport.LastFailure ?? "no reason given"}");
            // No server/endpoint available — run with LSP features disabled. `null` is the transport's
            // whole vocabulary for failure, so the reason comes from the transport itself.
            _attempt = attempt.StoppedAt(LanguageConnectionStage.Connecting, _transport.LastFailure);
            RaiseStateChanged();
            return;
        }

        attempt.Reached(LanguageConnectionStage.Connected);
        Note(ConversationDirection.Local, ConversationEntryKind.Lifecycle, null, "connected");

        var rpc = new JsonRpc(handler, new LspNotificationReceiver(this));
        rpc.Disconnected += OnRpcDisconnected;
        _rpc = rpc;
        rpc.StartListening();

        await InitializeAsync(cancellationToken, attempt);

        if (_initialized)
            await ReopenTrackedDocumentsAsync();
    }

    /// <summary>
    /// After a (re)connect, replay <c>textDocument/didOpen</c> for every tracked document so the
    /// freshly-initialised server regains its document set and republishes diagnostics.
    ///
    /// <para>
    /// The version each document is replayed with is the one this connection has been tracking, not 1.
    /// The server has no memory of it either way, and resetting would put the client's count behind the
    /// session's, so the next change would arrive with a version the server had already seen.
    /// </para>
    /// </summary>
    private async Task ReopenTrackedDocumentsAsync()
    {
        if (_rpc is null || !_initialized) return;
        foreach (var (uri, document) in _openDocuments)
            await SendDidOpenAsync(uri, document.Version, document.Text);
    }

    /// <summary>
    /// Tells the server about a document, whether for the first time or again after a reconnect.
    ///
    /// <para>
    /// <b>One method because there were two, and they drifted.</b> The replay used to build its own
    /// notification with the language identifier hardcoded to <c>"vb6"</c> and no capability gate, so
    /// after any reconnect a configured foreign server was told every one of its documents was Visual
    /// Basic — the exact global answer that the per-server identifier exists to prevent, reintroduced in
    /// the one path nobody looked at (hexide-io/HexIDE#272). A server keying its state on the identifier
    /// then held a document set it could not analyse, and the symptom was language features that worked,
    /// silently stopped, and never came back.
    /// </para>
    /// </summary>
    private async Task SendDidOpenAsync(string uri, int version, string text)
    {
        var rpc = _rpc;
        if (rpc is null || !_initialized || !ServerCapabilities.AcceptsOpenClose(_capabilities?.Value)) return;

        var p = new DidOpenTextDocumentParams(new TextDocumentItem(uri, _languageId, version, text));
        try { await rpc.NotifyWithParameterObjectAsync("textDocument/didOpen", p); }
        // No token: this path is the reconnect replay as well as the first open, and neither is cancelled
        // by a caller — so anything thrown here is a real failure rather than a superseded request.
        catch (Exception ex) { WarnRequestFailedOnce("textDocument/didOpen", ex, CancellationToken.None); }
    }

    /// <summary>
    /// Gives up on a server that accepted the connection and then said nothing.
    ///
    /// <para>
    /// The connection is torn down rather than left alone, because leaving it means a request pending on a
    /// live channel forever — and because <see cref="DisposeRpc"/> detaches the handler before disposing,
    /// so nothing else here will notice. The reconnect loop is therefore started explicitly rather than
    /// waited for.
    /// </para>
    ///
    /// <para>
    /// For <c>stdio</c> that loop does not exist — <c>CanReconnect</c> is false, because the server is a
    /// child process this client launched and re-dialling it is not a thing the transport can do. In that
    /// case this is as far as recovery goes, and the point is the log line: the failure stops being
    /// invisible, which is the whole of the bug.
    /// </para>
    /// </summary>
    private void AbandonUnansweredHandshake()
    {
        _logger.LogWarning(
            "The language server accepted a connection but did not answer initialize within {Timeout}. "
          + "Abandoning the handshake and tearing the connection down. Language features will be "
          + "unavailable for this server{Recovery}.",
            _initializeTimeout,
            _transport.CanReconnect ? " until a reconnect succeeds" : ", and this transport cannot reconnect");

        DisposeRpc();

        if (_stopping || !_transport.CanReconnect) return;
        lock (_reconnectGate)
        {
            // Same single-loop invariant as OnRpcDisconnected. When this fires from inside the loop the
            // task is still running, so no second one starts and the existing backoff simply continues.
            if (!_stopping && _reconnectTask.IsCompleted)
                _reconnectTask = ReconnectLoopAsync();
        }
    }

    private void OnRpcDisconnected(object? sender, JsonRpcDisconnectedEventArgs e)
    {
        _initialized = false;
        _capabilities = null;
        _warnedCapabilities.Clear();
        _warnedFailures.Clear();
        _identity = null;

        // Before the stopping check, deliberately. A deliberate shutdown and a server that fell over are
        // the two things a reader most needs told apart, and only one of them is worth alarm.
        Note(ConversationDirection.Local, ConversationEntryKind.Lifecycle, null,
            _stopping
                ? "disconnected: shutting down"
                : $"disconnected: {e.Reason} ({e.Description ?? "no description"})");

        RaiseStateChanged();
        if (_stopping) return;
        _logger.LogWarning("VB LSP connection lost: {Reason} ({Description})", e.Reason, e.Description);
        if (!_transport.CanReconnect) return;
        lock (_reconnectGate)
        {
            // Start a reconnect loop only if one isn't already running (and we're not stopping).
            if (!_stopping && _reconnectTask.IsCompleted)
                _reconnectTask = ReconnectLoopAsync();
        }
    }

    /// <summary>Reconnect-with-backoff loop for transports that support it (WebSocket). Tears down the
    /// faulted JSON-RPC and re-runs connect → initialise → re-open until it succeeds or Stop is called.</summary>
    private async Task ReconnectLoopAsync()
    {
        // Single-loop invariant is enforced by the caller under _reconnectGate.
        var cts = new CancellationTokenSource();
        _reconnectCts = cts;
        try
        {
            var delay = TimeSpan.FromSeconds(1);
            var maxDelay = TimeSpan.FromSeconds(30);
            while (!_stopping && !cts.IsCancellationRequested)
            {
                try { await Task.Delay(delay, cts.Token); }
                catch (OperationCanceledException) { return; }
                if (_stopping) return;

                DisposeRpc();
                try
                {
                    await ConnectAndInitializeAsync(cts.Token);
                    if (_initialized && _transport.IsAlive)
                    {
                        _logger.LogInformation("VB LSP reconnected.");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "VB LSP reconnect attempt failed");
                }

                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, maxDelay.Ticks));
            }
        }
        finally
        {
            if (ReferenceEquals(_reconnectCts, cts)) _reconnectCts = null;
            cts.Dispose();
        }
    }

    /// <summary>Unsubscribes and disposes the current JSON-RPC connection (leaving the transport intact).</summary>
    private void DisposeRpc()
    {
        var rpc = _rpc;
        _rpc = null;
        if (rpc is not null)
        {
            rpc.Disconnected -= OnRpcDisconnected;
            rpc.Dispose();
        }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken, AttemptRecorder attempt)
    {
        if (_rpc is null) return;

        // Asked for now, not when this client was built: the server starts on first use, and the project
        // open at that moment is what it should be rooted at.
        var initParams = new InitializeParams(
            ProcessId: Environment.ProcessId,
            RootUri: WorkspaceRootUri(),
            WorkspaceFolders: WorkspaceFolders(),
            // Sent even when it is off. The field is optional and off is its default, so this line changes
            // nothing about how a server behaves — it changes what a capture of a quiet session can tell
            // you, which is the difference between "we never asked" and "we asked and it said nothing".
            Trace: _trace,
            Capabilities: new ClientCapabilities(
                new TextDocumentClientCapabilities(
                    PublishDiagnostics: new PublishDiagnosticsClientCapabilities(),
                    Hover: new HoverClientCapabilities(ContentFormat: ["plaintext"]),
                    // Claimed here so the server has something to answer. Declaring and gating are two
                    // halves of one negotiation: gate without declare and a conformant server withholds
                    // `save` because nothing asked for it, while we decline to send because it did not
                    // offer — both correct, nothing happening, and no error anywhere.
                    Synchronization: new TextDocumentSyncClientCapabilities(DidSave: true),
                    // Same bargain as `save` above, and the same failure if only half of it ships: a server
                    // that composes its capabilities from what the client asked for withholds
                    // `codeLensProvider` when nothing declared this, and then our gate declines to ask.
                    CodeLens: new CodeLensClientCapabilities()),
                new WorkspaceClientCapabilities(
                    ExecuteCommand: new ExecuteCommandClientCapabilities(),
                    WorkspaceFolders: true)));

        // Deliberately received as a raw JsonElement, and interpreted separately below.
        //
        // These are two different failures and they must not share a catch. "The server did not complete
        // the handshake" is fatal — there is nothing to talk to. "We could not model the reply it sent" is
        // not: the server answered, the connection is good, and the worst honest outcome is that we know
        // less than we might about what it supports. Sharing one catch is what made a single unexpected
        // capability shape disable every language feature including diagnostics (#238).
        // Bounded, because the unbounded version had no failure mode — it had a disappearance. A server
        // that connects and then never answers left this await pending for the life of the process: no
        // diagnostics, no exception, nothing logged, and `IsRunning` false forever. The catch below cannot
        // help, because a hang never throws (hexide-io/HexIDE#231).
        //
        // `WaitAsync` rather than a linked CancellationTokenSource, and the difference is not stylistic.
        // Handing StreamJsonRpc a token does not bound the wait: on cancellation it notifies the server and
        // then keeps waiting for that cancellation to be acknowledged — which a server ignoring `initialize`
        // will also ignore. Measured, after the first attempt at this fix used a linked token and hung
        // exactly as before. The token is still passed, so the server is told; the bound is here.
        JsonElement raw;
        SentWorkspaceRootUri = WorkspaceRootUri();
        attempt.Reached(LanguageConnectionStage.HandshakeSent);
        try
        {
            raw = await _rpc
                .InvokeWithParameterObjectAsync<JsonElement>("initialize", initParams, cancellationToken)
                .WaitAsync(_initializeTimeout, cancellationToken);
            await _rpc.NotifyWithParameterObjectAsync("initialized", EmptyParams.Instance);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Someone called Stop while we were waiting. Ordinary shutdown, not a fault.
            _logger.LogDebug("Initialize abandoned: the client was stopped during the handshake.");
            // Cancelled, NOT stopped: the IDE was shutting down and nothing was wrong. The registry
            // writes Failed for this today, which makes every ordinary exit look like a fault.
            _attempt = attempt.StoppedAt(
                LanguageConnectionStage.HandshakeSent,
                "the client was stopped during the handshake",
                LanguageConnectionStepOutcome.Cancelled);
            return;
        }
        catch (TimeoutException)
        {
            _attempt = attempt.StoppedAt(
                LanguageConnectionStage.HandshakeSent,
                $"the server did not answer initialize within {_initializeTimeout:g}");
            AbandonUnansweredHandshake();
            RaiseStateChanged();
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize VB6 LSP server");
            _attempt = attempt.StoppedAt(LanguageConnectionStage.HandshakeSent, ex.Message);
            RaiseStateChanged();
            return;
        }

        _capabilities = ReadCapabilities(raw) is { } caps ? new CapabilitySnapshot(caps) : null;
        _identity = ReadServerIdentity(raw);
        attempt.Reached(LanguageConnectionStage.Initialized);
        _attempt = attempt.Build();
        _initialized = true;
        NoteHandshake();
        _logger.LogInformation("LSP server initialized: {Server}",
            _identity is { Name: { Length: > 0 } n }
                ? (_identity.Version is { Length: > 0 } v ? $"{n} {v}" : n)
                : "(the server did not name itself)");
        RaiseStateChanged();
    }

    /// <summary>
    /// The workspace directory as a <c>file://</c> URI, or null when there is no project open.
    ///
    /// <para>
    /// Null rather than a guess. The protocol allows a root-less session, and inventing one — the current
    /// directory, a temp path — would point every workspace-relative lookup a server makes at somewhere
    /// the user has never heard of.
    /// </para>
    /// </summary>
    /// <summary>
    /// The workspace's folders as the wire wants them, or null when there are none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Null rather than an empty array</b>, because the protocol gives the two different meanings: null
    /// is "no folders are open", where an empty array is a workspace that has folders and happens to have
    /// none of them right now. With no project loaded the first is the true statement.
    /// </para>
    ///
    /// <para>
    /// <c>rootUri</c> is still sent alongside, and the four servers this suite drives are why: measured,
    /// they show three different behaviours. texlab reads <c>workspace_folders</c> ALONE and had no root at
    /// all before this change; clangd never parses the field, so <c>rootUri</c> is its only channel; rumdl
    /// prefers the folders and falls back; the reference JSON server reads neither. Sending one of the pair
    /// silently un-scopes whichever servers read the other, and there is no way to tell from this side.
    /// </para>
    /// </remarks>
    private WorkspaceFolder[]? WorkspaceFolders()
    {
        if (_workspace?.Folders is not { Count: > 0 } folders) return null;

        var wire = new List<WorkspaceFolder>(folders.Count);
        foreach (var folder in folders)
        {
            try
            {
                wire.Add(new WorkspaceFolder(new Uri(folder.Directory).AbsoluteUri, folder.Name));
            }
            catch (Exception ex)
            {
                // One unusable path costs that folder, not the handshake. Logged rather than swallowed,
                // because a server quietly rooted at fewer places than the IDE has open is exactly the
                // kind of silence this whole change exists to remove.
                _logger.LogWarning(ex,
                    "Could not express workspace folder {Directory} as a URI; it will not be sent",
                    folder.Directory);
            }
        }
        return wire.Count > 0 ? [.. wire] : null;
    }

    private string? WorkspaceRootUri()
    {
        var directory = _workspace?.Directory;
        if (string.IsNullOrWhiteSpace(directory)) return null;

        try
        {
            return new Uri(Path.GetFullPath(directory)).AbsoluteUri;
        }
        catch (Exception ex)
        {
            // A path that cannot be made into a URI costs the root, not the connection.
            _logger.LogWarning(ex, "Could not express the workspace directory {Directory} as a URI", directory);
            return null;
        }
    }

    /// <summary>
    /// Lifts the capabilities object out of an initialize reply, or null if there is not one.
    /// Never throws: an uninterpretable reply costs knowledge, not the connection.
    /// </summary>
    /// <summary>
    /// The server's own name and version from the initialize reply, or null if it offered none.
    ///
    /// <para>
    /// Both members are optional in the protocol, and this is the one field here that does not come from
    /// HexIDE's own configuration — everything else describes what the IDE was told to expect, while this
    /// describes what actually answered. Never used to decide behaviour.
    /// </para>
    /// </summary>
    private ServerIdentity? ReadServerIdentity(JsonElement raw)
    {
        try
        {
            if (raw.ValueKind != JsonValueKind.Object
                || !raw.TryGetProperty("serverInfo", out var info)
                || info.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var name = info.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                ? n.GetString() : null;
            var version = info.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() : null;

            return name is null && version is null ? null : new ServerIdentity(name, version);
        }
        catch (Exception ex)
        {
            // Cosmetic detail. A server that sends something unreadable here is still a working server.
            _logger.LogDebug(ex, "Could not read serverInfo");
            return null;
        }
    }

    private JsonElement? ReadCapabilities(JsonElement raw)
    {
        try
        {
            if (raw.ValueKind != JsonValueKind.Object
                || !raw.TryGetProperty("capabilities", out var caps))
            {
                _logger.LogWarning("Initialize reply carried no capabilities object.");
                return null;
            }

            // Cloned: the JsonDocument backing `raw` is disposed when this call returns, after which any
            // element reaching into it throws — at some arbitrary later read, far from here.
            return caps.Clone();
        }
        catch (Exception ex)
        {
            // Worth a warning rather than silence: it means a server is advertising something in a shape
            // this client cannot read, which is a gap in the model rather than a fault of the server's.
            _logger.LogWarning(ex, "Could not read the server's advertised capabilities; continuing without them.");
            return null;
        }
    }

    public async Task OpenDocumentAsync(string uri, string text, CancellationToken cancellationToken = default)
    {
        // Tracked BEFORE the gate, deliberately: a document opened while no server is up must still be
        // replayed when one arrives, which is what makes lazy start and reconnect work at all.
        _openDocuments[uri] = new TrackedDocument(1, text);
        await SendDidOpenAsync(uri, 1, text);
    }

    public async Task ChangeDocumentAsync(string uri, int version, string text, CancellationToken cancellationToken = default)
    {
        _openDocuments[uri] = new TrackedDocument(version, text);
        var rpc = _rpc;
        if (rpc is null || !_initialized || !ServerCapabilities.AcceptsChanges(_capabilities?.Value)) return;
        var p = new DidChangeTextDocumentParams(
            new VersionedTextDocumentIdentifier(uri, version),
            [new TextDocumentContentChangeEvent(text)]);
        try { await rpc.NotifyWithParameterObjectAsync("textDocument/didChange", p); }
        catch (Exception ex) { WarnRequestFailedOnce("textDocument/didChange", ex, cancellationToken); }
    }

    public async Task CloseDocumentAsync(string uri, CancellationToken cancellationToken = default)
    {
        _openDocuments.TryRemove(uri, out _);
        var rpc = _rpc;
        if (rpc is null || !_initialized || !ServerCapabilities.AcceptsOpenClose(_capabilities?.Value)) return;
        var p = new DidCloseTextDocumentParams(new TextDocumentIdentifier(uri));
        try { await rpc.NotifyWithParameterObjectAsync("textDocument/didClose", p); }
        catch (Exception ex) { WarnRequestFailedOnce("textDocument/didClose", ex, cancellationToken); }
    }

    public async Task SaveDocumentAsync(string uri, CancellationToken cancellationToken = default)
    {
        // No _openDocuments write, unlike its three siblings: a save changes neither the text nor the
        // version, so there is nothing here to record. It is an announcement about state the server
        // already has.
        var rpc = _rpc;
        if (rpc is null || !_initialized) return;

        var mode = ServerCapabilities.ReadSave(_capabilities?.Value);
        if (mode == SaveNotification.None) return;

        // Only when negotiated. Sending it unasked would be harmless on the wire and wrong in principle —
        // the server told us how it wants this, and overriding that makes us unpredictable to its author.
        var text = mode == SaveNotification.WithText && _openDocuments.TryGetValue(uri, out var document)
            ? document.Text
            : null;

        var p = new DidSaveTextDocumentParams(new TextDocumentIdentifier(uri), text);
        try { await rpc.NotifyWithParameterObjectAsync("textDocument/didSave", p); }
        catch (Exception ex) { WarnRequestFailedOnce("textDocument/didSave", ex, cancellationToken); }
    }

    public async Task<HoverResult?> RequestHoverAsync(string uri, Position position, CancellationToken cancellationToken = default)
    {
        if (_rpc is null || !_initialized || !CanServe("hoverProvider")) return null;
        var p = new TextDocumentPositionParams(new TextDocumentIdentifier(uri), position);
        try
        {
            return await _rpc.InvokeWithParameterObjectAsync<HoverResult?>(
                "textDocument/hover", p, cancellationToken);
        }
        catch (Exception ex)
        {
            WarnRequestFailedOnce("textDocument/hover", ex, cancellationToken);
            return null;
        }
    }

    /// <summary>
    /// The document's symbols, in whichever of the protocol's two shapes the server chose to answer in.
    /// </summary>
    /// <remarks>
    /// <b><c>textDocument/documentSymbol</c> has two legal replies and they share no field.</b>
    /// <c>DocumentSymbol[]</c> is a tree with <c>range</c>/<c>selectionRange</c> and <c>children</c>;
    /// <c>SymbolInformation[]</c> is flat and carries a <c>location</c> instead. Deserializing straight
    /// into the first shape does not fail on the second — it produces symbols whose ranges are null, and
    /// the first consumer to read one throws somewhere unrelated to the cause.
    ///
    /// <para>
    /// So the answer is read as JSON and normalised here. Everything past this point sees one shape, with
    /// both ranges guaranteed present, which is the only way the guarantee on
    /// <see cref="DocumentSymbol"/> can be true.
    /// </para>
    /// </remarks>
    public async Task<DocumentSymbol[]> RequestDocumentSymbolsAsync(string uri, CancellationToken cancellationToken = default)
    {
        if (_rpc is null || !_initialized || !CanServe("documentSymbolProvider")) return [];
        var p = new DocumentSymbolParams(new TextDocumentIdentifier(uri));
        try
        {
            var raw = await _rpc.InvokeWithParameterObjectAsync<JsonElement?>(
                "textDocument/documentSymbol", p, cancellationToken);
            return ReadDocumentSymbols(raw);
        }
        catch (Exception ex)
        {
            WarnRequestFailedOnce("textDocument/documentSymbol", ex, cancellationToken);
            return [];
        }
    }

    /// <summary>Turns either legal reply shape into the one the rest of the client understands.</summary>
    internal static DocumentSymbol[] ReadDocumentSymbols(JsonElement? raw)
    {
        if (raw is not { ValueKind: JsonValueKind.Array } array) return [];

        var symbols = new List<DocumentSymbol>();
        foreach (var element in array.EnumerateArray())
        {
            if (ReadSymbol(element, 64) is { } symbol) symbols.Add(symbol);
        }
        return [.. symbols];
    }

    /// <summary>
    /// One symbol of either shape, or null when the element is not one at all.
    /// </summary>
    /// <remarks>
    /// The two shapes are told apart by <c>location</c>, which only <c>SymbolInformation</c> has. A
    /// <c>SymbolInformation</c>'s single range becomes both of ours: it is the only range the server gave,
    /// so claiming to know a narrower selection would be inventing one.
    ///
    /// <para>
    /// <c>selectionRange</c> falls back to <c>range</c> when a <c>DocumentSymbol</c> omits it. The
    /// specification requires it, so this is a malformed reply — but the whole point of this seam is that a
    /// server we did not write cannot make a consumer here throw.
    /// </para>
    /// </remarks>
    private static DocumentSymbol? ReadSymbol(JsonElement element, int depth)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String)
            return null;

        var kind = element.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.Number
            ? (SymbolKind)k.GetInt32()
            : SymbolKind.Method;

        Messages.Range? range = null;
        if (element.TryGetProperty("location", out var location)
            && location.ValueKind == JsonValueKind.Object
            && location.TryGetProperty("range", out var locationRange))
        {
            range = ReadRange(locationRange);
        }
        else if (element.TryGetProperty("range", out var own))
        {
            range = ReadRange(own);
        }

        // A symbol with no range at all cannot be navigated to, and every consumer here exists to navigate.
        // Dropping it loses a name; keeping it would put a null where nothing checks for one.
        if (range is null) return null;

        var selection = element.TryGetProperty("selectionRange", out var sel) ? ReadRange(sel) : null;

        DocumentSymbol[]? children = null;
        if (depth > 0
            && element.TryGetProperty("children", out var childArray)
            && childArray.ValueKind == JsonValueKind.Array)
        {
            var read = new List<DocumentSymbol>();
            foreach (var child in childArray.EnumerateArray())
            {
                if (ReadSymbol(child, depth - 1) is { } c) read.Add(c);
            }
            if (read.Count > 0) children = [.. read];
        }

        var detail = element.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String
            ? d.GetString()
            : null;

        return new DocumentSymbol(name.GetString()!, kind, range, selection ?? range, detail, children);
    }

    private static Messages.Range? ReadRange(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty("start", out var start) || !element.TryGetProperty("end", out var end))
            return null;
        return ReadPosition(start) is { } s && ReadPosition(end) is { } e ? new Messages.Range(s, e) : null;
    }

    private static Position? ReadPosition(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty("line", out var line) && line.ValueKind == JsonValueKind.Number
        && element.TryGetProperty("character", out var ch) && ch.ValueKind == JsonValueKind.Number
            ? new Position(line.GetInt32(), ch.GetInt32())
            : null;

    public async Task<FoldingRange[]> RequestFoldingRangesAsync(string uri, CancellationToken cancellationToken = default)
    {
        if (_rpc is null || !_initialized || !CanServe("foldingRangeProvider")) return [];
        var p = new FoldingRangeParams(new TextDocumentIdentifier(uri));
        try
        {
            return await _rpc.InvokeWithParameterObjectAsync<FoldingRange[]>(
                "textDocument/foldingRange", p, cancellationToken) ?? [];
        }
        catch (Exception ex)
        {
            WarnRequestFailedOnce("textDocument/foldingRange", ex, cancellationToken);
            return [];
        }
    }

    public async Task<CompletionItem[]> RequestCompletionAsync(string uri, Position position, CancellationToken cancellationToken = default)
    {
        if (_rpc is null || !_initialized || !CanServe("completionProvider")) return [];
        var p = new CompletionParams(new TextDocumentIdentifier(uri), position);
        try
        {
            var result = await _rpc.InvokeWithParameterObjectAsync<CompletionList?>(
                "textDocument/completion", p, cancellationToken);
            return result?.Items ?? [];
        }
        catch (Exception ex)
        {
            WarnRequestFailedOnce("textDocument/completion", ex, cancellationToken);
            return [];
        }
    }

    public async Task<SignatureHelp?> RequestSignatureHelpAsync(string uri, Position position, CancellationToken cancellationToken = default)
    {
        if (_rpc is null || !_initialized || !CanServe("signatureHelpProvider")) return null;
        var p = new SignatureHelpParams(new TextDocumentIdentifier(uri), position);
        try
        {
            return await _rpc.InvokeWithParameterObjectAsync<SignatureHelp?>(
                "textDocument/signatureHelp", p, cancellationToken);
        }
        catch (Exception ex)
        {
            WarnRequestFailedOnce("textDocument/signatureHelp", ex, cancellationToken);
            return null;
        }
    }

    /// <summary>
    /// Where the symbol under the cursor is defined, in whichever of the protocol's three shapes the
    /// server answered in.
    /// </summary>
    /// <remarks>
    /// <b><c>textDocument/definition</c> answers <c>Location | Location[] | LocationLink[]</c>.</b> Only the
    /// middle one deserializes into an array, so a server returning a single <c>Location</c> object — which
    /// is the natural answer to "where is this one thing defined", and what several servers send — failed
    /// to bind, landed in the catch below, and became "no definition here". Silent, and indistinguishable
    /// from a server that genuinely had nothing.
    /// </remarks>
    public async Task<Location[]?> RequestDefinitionAsync(string uri, Position position, CancellationToken cancellationToken = default)
    {
        if (_rpc is null || !_initialized || !CanServe("definitionProvider")) return null;
        var p = new TextDocumentPositionParams(new TextDocumentIdentifier(uri), position);
        try
        {
            var raw = await _rpc.InvokeWithParameterObjectAsync<JsonElement?>(
                "textDocument/definition", p, cancellationToken);
            return ReadLocations(raw);
        }
        catch (Exception ex)
        {
            WarnRequestFailedOnce("textDocument/definition", ex, cancellationToken);
            return null;
        }
    }

    public async Task<Location[]?> RequestDeclarationAsync(string uri, Position position, CancellationToken cancellationToken = default)
    {
        // Gated on declarationProvider, not definitionProvider. Falling back to the definition capability
        // would send declaration requests to servers that never claimed to answer them -- and the two
        // capabilities are genuinely independent: of the servers this suite drives, several advertise
        // definition and explicitly not declaration.
        if (_rpc is null || !_initialized || !CanServe("declarationProvider")) return null;
        var p = new TextDocumentPositionParams(new TextDocumentIdentifier(uri), position);
        try
        {
            var raw = await _rpc.InvokeWithParameterObjectAsync<JsonElement?>(
                "textDocument/declaration", p, cancellationToken);
            return ReadLocations(raw);
        }
        catch (Exception ex)
        {
            WarnRequestFailedOnce("textDocument/declaration", ex, cancellationToken);
            return null;
        }
    }

    /// <summary>Reads all three reply shapes into the one the caller understands, or null for none.</summary>
    internal static Location[]? ReadLocations(JsonElement? raw)
    {
        if (raw is not { } value) return null;

        if (value.ValueKind == JsonValueKind.Object)
            return ReadLocation(value) is { } single ? [single] : null;

        if (value.ValueKind != JsonValueKind.Array) return null;

        var locations = new List<Location>();
        foreach (var element in value.EnumerateArray())
        {
            if (ReadLocation(element) is { } location) locations.Add(location);
        }
        return [.. locations];
    }

    /// <summary>
    /// One <c>Location</c> or <c>LocationLink</c>.
    /// </summary>
    /// <remarks>
    /// A <c>LocationLink</c> is told apart by <c>targetUri</c>, and its <c>targetSelectionRange</c> is
    /// preferred over <c>targetRange</c>: the first is the identifier itself, the second the whole
    /// declaration including its body. Landing the caret on the body would technically be the definition
    /// and would not look like arriving at one.
    /// </remarks>
    private static Location? ReadLocation(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        if (element.TryGetProperty("targetUri", out var targetUri) && targetUri.ValueKind == JsonValueKind.String)
        {
            var range =
                (element.TryGetProperty("targetSelectionRange", out var sel) ? ReadRange(sel) : null)
                ?? (element.TryGetProperty("targetRange", out var whole) ? ReadRange(whole) : null);
            return range is null ? null : new Location(targetUri.GetString()!, range);
        }

        if (!element.TryGetProperty("uri", out var uri) || uri.ValueKind != JsonValueKind.String) return null;
        if (!element.TryGetProperty("range", out var r) || ReadRange(r) is not { } plain) return null;
        return new Location(uri.GetString()!, plain);
    }

    public async Task<DocumentHighlight[]?> RequestDocumentHighlightAsync(string uri, Position position, CancellationToken cancellationToken = default)
    {
        if (_rpc is null || !_initialized || !CanServe("documentHighlightProvider")) return null;
        var p = new TextDocumentPositionParams(new TextDocumentIdentifier(uri), position);
        try
        {
            return await _rpc.InvokeWithParameterObjectAsync<DocumentHighlight[]?>(
                "textDocument/documentHighlight", p, cancellationToken);
        }
        catch (Exception ex)
        {
            WarnRequestFailedOnce("textDocument/documentHighlight", ex, cancellationToken);
            return null;
        }
    }

    public async Task<WorkspaceEdit?> RequestRenameAsync(string uri, Position position, string newName, CancellationToken cancellationToken = default)
    {
        if (_rpc is null || !_initialized || !CanServe("renameProvider")) return null;
        var p = new RenameParams(new TextDocumentIdentifier(uri), position, newName);
        try
        {
            return await _rpc.InvokeWithParameterObjectAsync<WorkspaceEdit?>(
                "textDocument/rename", p, cancellationToken);
        }
        catch (Exception ex)
        {
            WarnRequestFailedOnce("textDocument/rename", ex, cancellationToken);
            return null;
        }
    }

    public async Task<TextEdit[]> RequestFormattingAsync(string uri, CancellationToken cancellationToken = default)
    {
        if (_rpc is null || !_initialized || !CanServe("documentFormattingProvider")) return [];
        var p = new DocumentFormattingParams(
            new TextDocumentIdentifier(uri),
            new FormattingOptions(4, true));
        try
        {
            return await _rpc.InvokeWithParameterObjectAsync<TextEdit[]?>(
                "textDocument/formatting", p, cancellationToken) ?? [];
        }
        catch (Exception ex)
        {
            WarnRequestFailedOnce("textDocument/formatting", ex, cancellationToken);
            return [];
        }
    }

    public async Task<CodeLens[]> RequestCodeLensesAsync(string uri, CancellationToken cancellationToken = default)
    {
        if (_rpc is null || !_initialized || !CanServe("codeLensProvider")) return [];
        var p = new CodeLensParams(new TextDocumentIdentifier(uri));

        CodeLens[] lenses;
        try
        {
            lenses = await _rpc.InvokeWithParameterObjectAsync<CodeLens[]?>(
                "textDocument/codeLens", p, cancellationToken) ?? [];
        }
        catch (Exception ex)
        {
            WarnRequestFailedOnce("textDocument/codeLens", ex, cancellationToken);
            return [];
        }

        // Resolve here, on the connection that issued the lens. A lens with no command is not broken, it is
        // deferred -- and a caller cannot resolve it, because by the time lenses from several servers have
        // been gathered into one list nothing records which connection produced which. Doing it here is what
        // lets the rest of the client treat a lens as a plain value.
        if (!ServerCapabilities.ResolvesCodeLenses(AdvertisedCapabilities)) return lenses;

        var resolved = new CodeLens[lenses.Length];
        for (var i = 0; i < lenses.Length; i++)
        {
            // Already actionable. Asking again would be a round trip for an answer we hold.
            if (lenses[i].Command is not null) { resolved[i] = lenses[i]; continue; }
            resolved[i] = await ResolveCodeLensAsync(lenses[i], cancellationToken) ?? lenses[i];
        }
        return resolved;
    }

    /// <summary>
    /// Fills in one lens's command via <c>codeLens/resolve</c>, or null when the server could not.
    /// </summary>
    /// <remarks>
    /// Deliberately not on <see cref="ILspClient"/>. Exposing it would invite a caller to resolve a lens
    /// against the wrong connection, which is exactly the mistake the gathering step makes possible and
    /// this class exists to prevent.
    /// </remarks>
    private async Task<CodeLens?> ResolveCodeLensAsync(CodeLens lens, CancellationToken cancellationToken)
    {
        if (_rpc is null) return null;
        try
        {
            return await _rpc.InvokeWithParameterObjectAsync<CodeLens?>(
                "codeLens/resolve", lens, cancellationToken);
        }
        catch (Exception ex)
        {
            // The unresolved lens is still returned by the caller. A lens that cannot be resolved is one
            // the user cannot click, which is poor -- and dropping it would hide that the server offered
            // something here at all, which is worse.
            WarnRequestFailedOnce("codeLens/resolve", ex, cancellationToken);
            return null;
        }
    }

    public async Task<System.Text.Json.JsonElement?> ExecuteCommandAsync(
        string command,
        System.Text.Json.JsonElement[]? arguments = null,
        CancellationToken cancellationToken = default)
    {
        // Gated on the command, not on the capability. A server that advertises executeCommandProvider
        // still only owns the commands it named, and sending it someone else's is how a client turns
        // "nothing happened" into "the wrong thing happened".
        if (_rpc is null || !_initialized
            || !ServerCapabilities.DeclaresCommand(AdvertisedCapabilities, command)) return null;

        var p = new ExecuteCommandParams(command, arguments);
        try
        {
            return await _rpc.InvokeWithParameterObjectAsync<System.Text.Json.JsonElement?>(
                "workspace/executeCommand", p, cancellationToken);
        }
        catch (Exception ex)
        {
            WarnRequestFailedOnce("workspace/executeCommand", ex, cancellationToken);
            return null;
        }
    }

    public async Task<VbaBuiltinSymbol[]> RequestBuiltinSymbolsAsync(CancellationToken cancellationToken = default)
    {
        if (_rpc is null || !_initialized || !CanServeExperimental("vbBuiltinSymbols")) return [];
        try
        {
            return await _rpc.InvokeWithParameterObjectAsync<VbaBuiltinSymbol[]>(
                "vb/builtinSymbols", EmptyParams.Instance, cancellationToken) ?? [];
        }
        catch (Exception ex)
        {
            WarnRequestFailedOnce("vb/builtinSymbols", ex, cancellationToken);
            return [];
        }
    }

    public async Task<SymbolInformation[]> RequestWorkspaceSymbolsAsync(
        string query, CancellationToken cancellationToken = default)
    {
        if (_rpc is null || !_initialized || !CanServe("workspaceSymbolProvider")) return [];
        try
        {
            var raw = await _rpc.InvokeWithParameterObjectAsync<JsonElement?>(
                "workspace/symbol", new WorkspaceSymbolParams(query), cancellationToken);
            return ReadWorkspaceSymbols(raw);
        }
        catch (Exception ex)
        {
            WarnRequestFailedOnce("workspace/symbol", ex, cancellationToken);
            return [];
        }
    }

    /// <summary>
    /// Reads either reply shape into the one the caller understands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 3.17 added <c>WorkspaceSymbol</c> beside the older <c>SymbolInformation</c>, and the difference is
    /// not cosmetic: a <c>WorkspaceSymbol</c>'s <c>location</c> may carry <b>only a uri</b>, with the range
    /// deferred to <c>workspaceSymbol/resolve</c> so a server can answer a broad query without computing
    /// positions for thousands of hits.
    /// </para>
    ///
    /// <para>
    /// <b>We do not implement resolve, and a range-less hit is kept anyway</b>, pointing at the start of its
    /// file. That is a deliberate degradation rather than an oversight: the file is right, only the line is
    /// unknown, so the user lands in the correct document instead of not being told the symbol exists.
    /// Dropping it would hide a real answer; inventing a line would be a wrong one. Position zero is
    /// visibly "we were not told", which is the honest third option.
    /// </para>
    /// </remarks>
    internal static SymbolInformation[] ReadWorkspaceSymbols(JsonElement? raw)
    {
        if (raw is not { ValueKind: JsonValueKind.Array } array) return [];

        var symbols = new List<SymbolInformation>();
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;
            if (!element.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String) continue;
            if (!element.TryGetProperty("location", out var location)) continue;
            if (!location.TryGetProperty("uri", out var uri) || uri.ValueKind != JsonValueKind.String) continue;

            var kind = element.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.Number
                ? (SymbolKind)k.GetInt32()
                : SymbolKind.Variable;

            // ReadRange takes the RANGE, not the location that holds it — passing the location silently
            // returns null and every symbol lands at line 0, which reads as a server that answered without
            // positions rather than a client that looked in the wrong place.
            var range = location.TryGetProperty("range", out var r) ? ReadRange(r) : null;
            range ??= new Messages.Range(new Position(0, 0), new Position(0, 0));

            symbols.Add(new SymbolInformation(
                name.GetString()!,
                kind,
                new Location(uri.GetString()!, range),
                element.TryGetProperty("containerName", out var container)
                    && container.ValueKind == JsonValueKind.String
                        ? container.GetString()
                        : null));
        }
        return [.. symbols];
    }

    public async Task StopAsync()
    {
        _stopping = true;
        // Safe even if the reconnect loop already disposed the CTS in its finally.
        try { _reconnectCts?.Cancel(); } catch (ObjectDisposedException) { }

        // Wait for any in-flight reconnect loop to fully exit so it cannot establish a new
        // connection after we have torn everything down.
        Task reconnectTask;
        lock (_reconnectGate) { reconnectTask = _reconnectTask; }
        try { await reconnectTask; } catch { /* loop is best-effort */ }

        var rpc = _rpc;
        if (rpc is not null)
        {
            // Both take NO parameters (LSP 3.17), and the distinction is not pedantry.
            // `InvokeAsync(name)` is StreamJsonRpc's POSITIONAL overload and puts `"params":[]` on
            // the wire, which rumdl (tower-lsp) and an OmniSharp-based server both REJECT; and
            // `EmptyParams.Instance` sends `"params":{}`, which rumdl also rejects. Only omitting the
            // member is accepted by every server measured, and it is what the specification and
            // vscode-languageserver-node send. A null argument object is what omits it.
            //
            // The cost of getting this wrong is not a rejected request: LSP has a server exit 0 when a
            // shutdown preceded exit and 1 otherwise, so a server that refuses ours correctly reports
            // that we never shut it down, and any supervisor reads every clean exit as a crash.
            // See hexide-io/HexIDE#312.
            try { await rpc.InvokeWithParameterObjectAsync<object?>("shutdown", null); } catch { }
            try { await rpc.NotifyWithParameterObjectAsync("exit", null); } catch { }
        }
        DisposeRpc();

        _transport.Closed -= OnTransportClosed;
        await _transport.DisposeAsync();
        _initialized = false;
        _capabilities = null;
        _identity = null;
        _warnedCapabilities.Clear();
        _warnedFailures.Clear();
        RaiseStateChanged();
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private void OnTransportClosed(object? sender, EventArgs e)
    {
        _initialized = false;
        _capabilities = null;
        _warnedCapabilities.Clear();
        _warnedFailures.Clear();
        _identity = null;
        RaiseStateChanged();
    }

    /// <summary>
    /// Records what one source says about a document and raises the document's whole set.
    ///
    /// <para>
    /// Every publish goes through here, the server's included, so that no source is privileged: whoever
    /// speaks replaces only their own rows. Raising the union keeps the event a whole-document
    /// replacement, which is what every subscriber already implements.
    /// </para>
    /// </summary>
    internal void RaisePublishDiagnostics(string owner, PublishDiagnosticsParams p) =>
        DiagnosticsPublished?.Invoke(this, _diagnostics.Record(p.Uri, owner, p.Diagnostics));

    internal void RaiseMessageShown(ShowMessageParams p) => MessageShown?.Invoke(this, p);
    internal void RaiseMessageLogged(LogMessageParams p) => MessageLogged?.Invoke(this, p);
    internal void RaiseTraceReceived(LogTraceParams p) => TraceReceived?.Invoke(this, p);

    /// <summary>
    /// Asks this server for a different amount of commentary, and remembers the answer it will never give.
    /// </summary>
    /// <remarks>
    /// The level is recorded before the notification is sent rather than after, and deliberately: there is
    /// no acknowledgement to wait for, so "sent successfully" and "the server is now verbose" are not the
    /// same statement and the second one is not available. Recording first also means a reconnect
    /// re-handshakes at the level the developer last chose, even if the connection died mid-notification.
    /// </remarks>
    public async Task SetTraceAsync(string value, CancellationToken cancellationToken = default)
    {
        if (LspTraceValue.Normalise(value) is not { } level)
        {
            _logger.LogWarning("Ignoring an unknown trace level {Value} for the {Language} server",
                value, _languageId);
            return;
        }

        _trace = level;

        if (_rpc is null) return;

        try
        {
            await _rpc.NotifyWithParameterObjectAsync("$/setTrace", new SetTraceParams(level))
                .WaitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // A notification that could not be written is not a failure worth surfacing: the level is
            // recorded, and the next handshake carries it.
            _logger.LogDebug(ex, "Could not send $/setTrace to the {Language} server", _languageId);
        }
    }

    public Task InjectDiagnosticsAsync(string uri, Diagnostic[] diagnostics, string owner)
    {
        RaisePublishDiagnostics(owner, new PublishDiagnosticsParams(uri, diagnostics));
        return Task.CompletedTask;
    }

    public Task ClearDiagnosticsFromAsync(string owner)
    {
        foreach (var p in _diagnostics.Withdraw(owner))
            DiagnosticsPublished?.Invoke(this, p);
        return Task.CompletedTask;
    }

    private sealed class LspNotificationReceiver
    {
        private readonly VBLspClient _client;

        public LspNotificationReceiver(VBLspClient client) => _client = client;

        [JsonRpcMethod("textDocument/publishDiagnostics", UseSingleObjectParameterDeserialization = true)]
        public void OnPublishDiagnostics(PublishDiagnosticsParams p)
        {
            _client._logger.LogDebug("publishDiagnostics: uri={Uri}, count={Count}", p.Uri, p.Diagnostics.Length);
            _client.RaisePublishDiagnostics(DiagnosticOwner.LanguageServer, p);
        }

        /// <summary>
        /// The server's own log output, written through at the severity it declared.
        ///
        /// <para>
        /// Logged and not surfaced: this channel is where servers put detail, and a server in a bad state
        /// can be voluble on it. Putting that in front of a user would trade one bad experience for
        /// another. The log is where someone goes when features are missing, which is exactly the moment
        /// this matters.
        /// </para>
        /// </summary>
        [JsonRpcMethod("window/logMessage", UseSingleObjectParameterDeserialization = true)]
        public void OnLogMessage(LogMessageParams p)
        {
            _client._logger.Log(LevelOf(p.Type), "[{Language} server] {Message}", _client._languageId, p.Message);
            _client.RaiseMessageLogged(p);
        }

        /// <summary>
        /// The server describing its own work, having been asked to.
        /// </summary>
        /// <remarks>
        /// Logged at Debug and raised. Debug because this is commentary a developer opted into and not
        /// something to put in a shared log by default, and raised because the log is not where it is meant
        /// to end up — it is the one channel carrying reasoning a wire capture cannot reconstruct.
        ///
        /// <para>
        /// Expect nothing from most servers. Five were driven with verbose tracing and an explicit
        /// <c>$/setTrace</c>; four never sent a single one of these. A handler that is never called is the
        /// normal case here rather than evidence of a defect.
        /// </para>
        /// </remarks>
        [JsonRpcMethod("$/logTrace", UseSingleObjectParameterDeserialization = true)]
        public void OnLogTrace(LogTraceParams p)
        {
            _client._logger.LogDebug("[{Language} server trace] {Message}", _client._languageId, p.Message);
            _client.RaiseTraceReceived(p);
        }

        /// <summary>
        /// The server asking for the user's attention. Logged <b>and</b> raised, because a message the user
        /// never sees is the bug, and a message with no trace afterwards is the next one.
        /// </summary>
        [JsonRpcMethod("window/showMessage", UseSingleObjectParameterDeserialization = true)]
        public void OnShowMessage(ShowMessageParams p)
        {
            _client._logger.Log(LevelOf(p.Type), "[{Language} server] {Message}", _client._languageId, p.Message);
            _client.RaiseMessageShown(p);
        }

        /// <summary>
        /// Refuses dynamic capability registration, out loud.
        ///
        /// <para>
        /// <b>Refusing is correct, and is not the part worth changing.</b> <c>dynamicRegistration</c> is a
        /// <em>client</em> capability — the specification's own wording is "whether hover supports dynamic
        /// registration" — and this client declares it nowhere. A conformant server therefore may not
        /// register dynamically, and must put everything in its <c>initialize</c> reply.
        /// </para>
        ///
        /// <para>
        /// Servers that ask anyway exist, and the wire answer they already got was the right one: a
        /// <c>MethodNotFound</c> error, which lets them fall back instead of waiting. That behaviour is
        /// preserved exactly — the error code is set deliberately rather than inherited from having no
        /// handler at all. What was missing was on this side: nothing was written down, so a server that
        /// asked, was refused, and quietly served less left the user with fewer features and no trace of
        /// why (hexide-io/HexIDE#288).
        /// </para>
        /// </summary>
        [JsonRpcMethod("client/registerCapability", UseSingleObjectParameterDeserialization = true)]
        public void OnRegisterCapability(JsonElement p)
        {
            _client._logger.LogWarning(
                "The {Language} server asked to register capabilities dynamically, which this client does "
              + "not support and does not advertise support for; refused. Anything it withheld from its "
              + "initialize reply expecting to register later will be unavailable. Registrations: {What}",
                _client._languageId, Describe(p));

            throw new LocalRpcException("This client does not support dynamic capability registration.")
            {
                ErrorCode = (int)StreamJsonRpc.Protocol.JsonRpcErrorCode.MethodNotFound,
            };
        }

        /// <summary>The counterpart, refused the same way and for the same reason.</summary>
        [JsonRpcMethod("client/unregisterCapability", UseSingleObjectParameterDeserialization = true)]
        public void OnUnregisterCapability(JsonElement p)
        {
            _client._logger.LogWarning(
                "The {Language} server asked to unregister capabilities dynamically; refused, as nothing "
              + "was ever registered that way. Registrations: {What}",
                _client._languageId, Describe(p));

            throw new LocalRpcException("This client does not support dynamic capability registration.")
            {
                ErrorCode = (int)StreamJsonRpc.Protocol.JsonRpcErrorCode.MethodNotFound,
            };
        }

        /// <summary>
        /// The methods named in a registration payload, for the log. Best effort by design: this is a
        /// message from a server we did not write, and failing to summarise it must not turn a refusal
        /// into an exception of a different kind.
        /// </summary>
        private static string Describe(JsonElement p)
        {
            try
            {
                if (p.ValueKind != JsonValueKind.Object ||
                    !p.TryGetProperty("registrations", out var registrations) ||
                    registrations.ValueKind != JsonValueKind.Array)
                {
                    return "(unreadable)";
                }

                var methods = registrations.EnumerateArray()
                    .Select(r => r.TryGetProperty("method", out var m) ? m.GetString() : null)
                    .Where(m => !string.IsNullOrEmpty(m))
                    .ToArray();

                return methods.Length == 0 ? "(none named)" : string.Join(", ", methods);
            }
            catch (Exception)
            {
                return "(unreadable)";
            }
        }

        /// <summary>
        /// Maps the protocol's message levels onto the logger's.
        /// </summary>
        /// <remarks>
        /// <c>Log</c> becomes Debug rather than Information: it is the level a server uses for running
        /// commentary, and promoting it would bury the two levels that mean something. <c>Debug</c>, added
        /// in 3.18, is noisier still and goes to Trace.
        ///
        /// <para>
        /// <b>An unrecognised value goes to Trace, and the arm here previously had that exactly backwards.</b>
        /// The old reasoning was that a server saying something we cannot rank is still a server saying
        /// something. True, and it drew the wrong conclusion, because this scale gets quieter as its numbers
        /// rise. A value we do not recognise is therefore one the protocol added <em>after</em> the noisiest
        /// level we know, and sending it to Information promoted a server's most trivial chatter above its
        /// own running commentary. 3.18's <c>Debug</c> is the first real instance, and the old arm would
        /// have mis-ranked it upward without ever failing. Nothing is dropped either way; it is only ranked
        /// where something we cannot read belongs.
        /// </para>
        /// </remarks>
        private static LogLevel LevelOf(LspMessageType type) => type switch
        {
            LspMessageType.Error => LogLevel.Error,
            LspMessageType.Warning => LogLevel.Warning,
            LspMessageType.Info => LogLevel.Information,
            LspMessageType.Log => LogLevel.Debug,
            LspMessageType.Debug => LogLevel.Trace,
            _ => LogLevel.Trace,
        };
    }

    /// <summary>
    /// True when the connected server advertised this capability. Warns once when it did not.
    ///
    /// <para>
    /// A gated-out feature returns exactly what an absent server returns, which is why gating needed no
    /// change in any caller: <c>lsp-client</c> already requires that language features "degrade rather than
    /// fail", and that degradation path was already built and tested.
    /// </para>
    /// </summary>
    /// <summary>
    /// Every capability this client will ever ask a server for.
    /// </summary>
    /// <remarks>
    /// <b>Hand-written and guarded, because a list like this rots the moment nobody is checking it.</b>
    /// Each entry is a literal passed to <see cref="CanServe"/> somewhere below, and a test asserts the two
    /// sets are identical — so wiring a new method and forgetting this list fails the build rather than
    /// quietly reporting a capability as unused forever.
    ///
    /// <para>
    /// It exists to answer the question a server author most wants answered and nothing here could answer
    /// before: what have I advertised that this client is not taking?
    /// </para>
    /// </remarks>
    internal static readonly string[] ConsumedCapabilities =
    [
        "codeLensProvider",
        "completionProvider",
        "declarationProvider",
        "definitionProvider",
        "documentFormattingProvider",
        "documentHighlightProvider",
        "documentSymbolProvider",
        "foldingRangeProvider",
        "hoverProvider",
        "renameProvider",
        "signatureHelpProvider",
        "vbBuiltinSymbols",
        "workspaceSymbolProvider",
    ];

    /// <summary>
    /// Records the handshake landing, and everything the server offered that this client will not use.
    /// </summary>
    /// <remarks>
    /// Free, because both halves are known the moment initialize returns: the server has just said what it
    /// can do, and what this client asks for is fixed. It is the inverse of a refused request, and for
    /// somebody writing a server it is the more useful direction — a refusal says what they failed to
    /// offer, this says what they offered and nobody came for.
    /// </remarks>
    private void NoteHandshake()
    {
        Note(ConversationDirection.Local, ConversationEntryKind.Lifecycle, null,
            _identity is { Name: { Length: > 0 } name }
                ? $"initialized: {name}{(_identity.Version is { Length: > 0 } v ? " " + v : "")}"
                : "initialized: the server did not name itself");

        if (_capabilities?.Value is not { ValueKind: System.Text.Json.JsonValueKind.Object } capabilities)
            return;

        foreach (var advertised in capabilities.EnumerateObject())
        {
            // `false` is a refusal rather than an offer, and reporting it as unconsumed would blame this
            // client for declining something nobody put on the table.
            if (advertised.Value.ValueKind == System.Text.Json.JsonValueKind.False) continue;
            if (ConsumedCapabilities.Contains(advertised.Name, StringComparer.Ordinal)) continue;

            // `experimental` is a container rather than a capability, and this client does reach inside it
            // for one method. Reporting the container as unused would be wrong; walking it and comparing
            // its contents is not done, so an unused EXPERIMENTAL capability goes unreported. A stated
            // limit rather than a silent one.
            if (advertised.Name == "experimental") continue;

            Note(ConversationDirection.Local, ConversationEntryKind.Unconsumed, null,
                $"advertised and unused: '{advertised.Name}'");
        }
    }

    private bool CanServe(string capabilityName)
    {
        if (ServerCapabilities.Supports(_capabilities?.Value, capabilityName)) return true;
        WarnUnavailableOnce(capabilityName);
        return false;
    }

    private bool CanServeExperimental(string capabilityName)
    {
        if (ServerCapabilities.SupportsExperimental(_capabilities?.Value, capabilityName)) return true;
        WarnUnavailableOnce("experimental." + capabilityName);
        return false;
    }

    /// <summary>
    /// Says once, at warning, that a wanted capability was not advertised.
    ///
    /// <para>
    /// This is the difference between an honest refusal and a silent blackout, and it earns its keep on one
    /// specific failure: the server is resolved by probing the output directory and several parents, so an
    /// older binary sitting in one of them is found, advertises little or nothing, and every feature quietly
    /// stops. Without a line naming what was missing, that is indistinguishable from "the IDE is broken".
    /// </para>
    ///
    /// <para>
    /// Once per capability per connection, because these are asked on every keystroke — a per-request log
    /// would bury the thing it is trying to surface. The set clears with the connection, so a reconnect to a
    /// different server reports afresh.
    /// </para>
    /// </summary>
    private void WarnUnavailableOnce(string capabilityName)
    {
        if (!_warnedCapabilities.TryAdd(capabilityName, 0)) return;

        // The same once-per-connection cadence serves both purposes. These are asked on every keystroke,
        // so a per-request entry would bury the timeline exactly as a per-request log line would.
        Note(ConversationDirection.Sent, ConversationEntryKind.NeverSent, null,
            $"not asked: the server did not advertise '{capabilityName}'");

        _logger.LogWarning(
            "The connected language server did not advertise '{Capability}'; that feature is unavailable. "
          + "If it should be supported, check which server binary was resolved — a stale one advertises "
          + "little and disables features silently.",
            capabilityName);
    }

    /// <summary>
    /// Says once, at warning, that a request to the server threw.
    /// </summary>
    /// <remarks>
    /// <b>An exception and an empty answer are not the same event, and logging both at debug made them
    /// indistinguishable.</b> A server replying "no definition here" is normal and constant; a request that
    /// <em>threw</em> means the feature did not work. Both landed in the same catch at the same level, under
    /// a log the IDE writes at Information by default — so a feature could be bound, implemented,
    /// advertised and inert with no trace anywhere (hexide-io/HexIDE#325).
    ///
    /// <para>
    /// <b>Cancellation is excluded, and that exclusion is the whole reason this can be a warning at all.</b>
    /// Completion, signature help, highlighting and folding are each driven by a
    /// <see cref="CancellationTokenSource"/> that is replaced on the next keystroke, so a superseded request
    /// throwing <see cref="OperationCanceledException"/> is the design working. Warning on those would emit
    /// a line per keypress and bury exactly what this exists to surface.
    /// </para>
    ///
    /// <para>
    /// Once per method per connection, for the same reason as
    /// <see cref="WarnUnavailableOnce"/>: a server broken for one method is broken for every call to it, and
    /// the second thousand lines say nothing the first did not. Repeats stay at debug so a full trace is
    /// still available to anyone who asks for one. The set clears with the connection.
    /// </para>
    /// </remarks>
    private void WarnRequestFailedOnce(string method, Exception ex, CancellationToken cancellationToken)
    {
        if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "{Method} was superseded before it completed", method);
            return;
        }

        if (!_warnedFailures.TryAdd(method, 0))
        {
            _logger.LogDebug(ex, "{Method} failed again", method);
            return;
        }

        _logger.LogWarning(ex,
            "'{Method}' failed against the connected language server, so that feature will not work. "
          + "Further failures of this method are logged at debug for this connection.",
            method);
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _warnedCapabilities = new();

    /// <summary>Methods already reported as failing on this connection. Cleared wherever
    /// <see cref="_warnedCapabilities"/> is, so a reconnect to a different server reports afresh.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _warnedFailures = new();
}
