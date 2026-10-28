using System.Collections.Concurrent;
using System.Text.Json;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

using HexIDE.Conversations;
using HexIDE.Redaction;

namespace HexIDE.Lsp;

public sealed class VBLspClient : ILspClient
{
    private readonly ILspTransport _transport;
    private readonly ConversationRedactor? _workspaceNames;
    private readonly ILogger<VBLspClient> _logger;
    // Currently-open documents (uri -> latest version+text), so they can be replayed after a reconnect.
    private readonly ConcurrentDictionary<string, TrackedDocument> _openDocuments = new();
    private readonly ConcurrentDictionary<string, JsonElement> _progressTokens = new();
    private readonly SemaphoreSlim _changeGate = new(1, 1);
    // What each source currently says about each document. This connection's server is one source; an
    // external compiler injecting through this client is another, and neither may erase the other.
    private readonly DiagnosticLedger _diagnostics = new();
    private readonly object _reconnectGate = new();
    private JsonRpc? _rpc;
    private volatile bool _initialized;
    private volatile bool _initializeAnswered;

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

    /// <summary>
    /// A document this client holds, as it was last opened or changed.
    /// </summary>
    /// <param name="OpenedOn">
    /// The connection whose server has been opened on it, numbered as <see cref="_connection"/> counts them, or
    /// <see cref="NotOpened"/>. A server knows nothing of a document until it is opened on that server, and every
    /// new connection is a new server as far as the document goes; the replay opens each again.
    /// </param>
    private sealed record TrackedDocument(int Version, string Text, int OpenedOn = NotOpened);

    /// <summary>No connection: a document tracked before any server was up, or before this one was opened on it.</summary>
    private const int NotOpened = 0;

    /// <summary>
    /// Counts completed handshakes, so a document opened on one connection can be told from one opened on the
    /// next. Raised before <see cref="_initialized"/> is set, so whatever reads it as initialized reads the
    /// connection that made it so.
    /// </summary>
    private int _connection;

    public event EventHandler<PublishDiagnosticsParams>? DiagnosticsPublished;
    public event EventHandler<ShowMessageParams>? MessageShown;
    public event EventHandler<ProgressParams>? ProgressReceived;
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
    /// <param name="workspaceNames">
    /// The session redactor for names this client supplies. URI fields stay real so the server can use
    /// them; folder display names are assigned their export pseudonym before they reach the wire.
    /// </param>
    /// <param name="locale">Resolved UI language, read anew for each handshake.</param>
    public VBLspClient(
        ILspTransport transport, ILogger<VBLspClient> logger, string languageId,
        ILspWorkspace? workspace = null, TimeSpan? initializeTimeout = null,
        string trace = LspTraceValue.Off,
        ConversationLog? capture = null, string? connectionId = null,
        ConversationRedactor? workspaceNames = null, Func<string>? locale = null)
    {
        _transport = transport;
        _logger = logger;
        _languageId = languageId;
        _workspace = workspace;
        _initializeTimeout = initializeTimeout ?? DefaultRequestTimeout;
        _trace = LspTraceValue.Normalise(trace) ?? LspTraceValue.Off;
        _capture = capture;
        _connectionId = connectionId;
        _workspaceNames = workspaceNames;
        _locale = locale ?? (() => "en");
    }

    private readonly Func<string> _locale;

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
        ConversationDirection direction, ConversationEntryKind kind, string? method, string? detail,
        bool fault = false)
    {
        if (_capture is not { } capture || _connectionId is not { } id) return;
        try { capture.Record(id, direction, kind, method, null, 0, null, detail, fault); }
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
    internal TimeSpan RequestTimeout { get; set; } = DefaultRequestTimeout;

    // Shared with initialize for consistency; a policy budget, not a measured server response time.
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);

    // Guard removal/disposal against cancellation while a change drains the wire notifications.
    private readonly object _supersededGate = new();
    private readonly Dictionary<CancellationTokenSource, (string Uri, RequestCancellation Scope)> _superseded = new();

    private async Task CancelSupersededAsync(string uri)
    {
        Task[] sends;
        lock (_supersededGate)
        {
            var requests = _superseded.Where(entry => entry.Value.Uri == uri).ToArray();
            foreach (var request in requests) request.Key.Cancel();
            sends = requests.Select(request => request.Value.Scope.CancellationSent).ToArray();
        }
        await Task.WhenAll(sends);
    }

    private async Task<T> InvokeRequestAsync<T>(JsonRpc rpc, string method, object? parameters,
        CancellationToken cancellationToken = default, TimeSpan? timeout = null, string? supersededUri = null)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var scope = new RequestCancellation(_capture, _connectionId);
        var previous = RequestCancellation.Current.Value;
        Task<T> pending;
        if (supersededUri is not null)
            lock (_supersededGate) _superseded.Add(cancellation, (supersededUri, scope));
        try
        {
            RequestCancellation.Current.Value = scope;
            pending = rpc.InvokeWithParameterObjectAsync<T>(method, parameters, cancellation.Token);
        }
        catch
        {
            lock (_supersededGate) _superseded.Remove(cancellation);
            throw;
        }
        finally { RequestCancellation.Current.Value = previous; }

        try { return await pending.WaitAsync(timeout ?? RequestTimeout, cancellation.Token); }
        catch (Exception ex) when (ex is TimeoutException || ex is OperationCanceledException)
        {
            if (ex is TimeoutException) scope.Outcome = ConversationOutcome.TimedOut;
            // Notify best effort, but never wait for the server to acknowledge cancellation.
            cancellation.Cancel();
            _ = pending.ContinueWith(t => _ = t.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw;
        }
        catch (Exception ex)
        {
            scope.DropAnswer(ex);
            throw;
        }
        finally
        {
            lock (_supersededGate) _superseded.Remove(cancellation);
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        // Channel-level death signal (e.g. stdio server process exit). WebSocket drops surface via
        // JsonRpc.Disconnected instead, wired per-connection in ConnectAndInitializeAsync.
        _transport.Closed += OnTransportClosed;
        _transport.Notice += OnTransportNotice;

        await ConnectAndInitializeAsync(cancellationToken);
    }

    /// <summary>Connects the transport, binds JSON-RPC, performs the initialize handshake, and
    /// replays any tracked documents. Used for the first connect and for every reconnect.</summary>
    private async Task ConnectAndInitializeAsync(CancellationToken cancellationToken)
    {
        _serverRefreshesDiagnostics = false;
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
        //
        // UNCONDITIONALLY, which it was not. The wrapper also repairs the one frame shape that would
        // otherwise take the whole connection down (see RepairNullParams), so making it conditional on
        // having somewhere to write a record meant an unrecorded connection died where a recorded one
        // lived. Every test that builds a client without a capture was on the losing side of that, which is
        // how it went unnoticed. It records nothing when there is nothing to record.
        IJsonRpcMessageFormatter wired = new CapturingFormatter(formatter, _capture, _connectionId);

        // The opening allowance counts frames, so a reconnect has to start it again or the handshake rule
        // would apply once per process rather than once per connection — and a respawn after a crash is
        // where a handshake is most worth having.
        if (_capture is { } reconnecting && _connectionId is { } reconnectingId)
            reconnecting.Reconnecting(reconnectingId);

        var attempt = new AttemptRecorder();
        attempt.Reached(LanguageConnectionStage.Connecting);
        Note(ConversationDirection.Local, ConversationEntryKind.Lifecycle, null, "connecting");

        var handler = await _transport.ConnectAsync(wired, cancellationToken);
        if (handler is null)
        {
            Note(ConversationDirection.Local, ConversationEntryKind.Lifecycle, null,
                $"could not connect: {_transport.LastFailure ?? "no reason given"}", fault: true);
            // No server/endpoint available — run with LSP features disabled. `null` is the transport's
            // whole vocabulary for failure, so the reason comes from the transport itself.
            _attempt = attempt.StoppedAt(LanguageConnectionStage.Connecting, _transport.LastFailure);
            RaiseStateChanged();
            return;
        }

        attempt.Reached(LanguageConnectionStage.Connected);
        Note(ConversationDirection.Local, ConversationEntryKind.Lifecycle, null, "connected");

        // Once per connection, and only when there is something to say. A reader who is not told that a
        // transport cannot produce an exit code will read its absence as a clean exit.
        if (_transport.Unobservable is { Length: > 0 } unobservable)
            Note(ConversationDirection.Local, ConversationEntryKind.Note, null, unobservable);

        var rpc = new LspJsonRpc(handler, new LspNotificationReceiver(this));
        var cancellation = new LspCancellationStrategy(rpc, _logger);
        rpc.CancellationStrategy = cancellation;
        rpc.AddLocalRpcTarget(cancellation);
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
        foreach (var uri in _openDocuments.Keys)
        {
            // Skipped if an open already reached this connection between its handshake and here, so that no
            // server is opened on a document twice; and taken as tracked NOW, so a change recorded since the
            // loop began is what is sent.
            if (MarkOpened(uri) is not { } document) continue;
            await SendDidOpenAsync(uri, document.Version, document.Text);

            // And ASK, for a server that only answers when asked.
            //
            // Not a belt-and-braces repeat of the ask in OpenDocumentAsync — it is the only ask those
            // documents will ever get. A document tracked before the handshake completed was opened when
            // there were no capabilities to gate on, so its ask was skipped; a server that publishes covers
            // that case by publishing on didOpen, and a server that does not has no such fallback. Without
            // this, a document opened while the server was still starting — which is every document in the
            // lazy-start path, and every document after a reconnect — would sit unanalysed for good.
            PullDiagnosticsInBackground(uri);
        }
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
        _initializeAnswered = false;
        ClearProgress();
        _initialized = false;
        _capabilities = null;
        _warnedCapabilities.Clear();
        _semanticWarnings.Clear();
        _prioritySkips.Clear();
        _warnedFailures.Clear();
        _identity = null;

        // Before the stopping check, deliberately. A deliberate shutdown and a server that fell over are
        // the two things a reader most needs told apart, and only one of them is worth alarm.
        Note(ConversationDirection.Local, ConversationEntryKind.Lifecycle, null,
            _stopping
                ? "disconnected: shutting down"
                : $"disconnected: {e.Reason} ({e.Description ?? "no description"})",
            fault: !_stopping);

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
        _initializeAnswered = false;
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
            WorkspaceFolders: _sentWorkspaceFolders = WorkspaceFolders(),
            // Sent even when it is off. The field is optional and off is its default, so this line changes
            // nothing about how a server behaves — it changes what a capture of a quiet session can tell
            // you, which is the difference between "we never asked" and "we asked and it said nothing".
            Trace: _trace,
            Locale: _locale(),
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
                    CodeLens: new CodeLensClientCapabilities(),
                    // The same bargain again, and the only one here where shipping half of it makes things
                    // actively WORSE rather than merely no better. A server may read this as "the client
                    // will ask, so I need not announce" and stop publishing — measured against ruff, which
                    // publishes freely without this line and publishes nothing at all with it. So this
                    // declaration and the request that follows it are one change and cannot be separated.
                    Diagnostic: new DiagnosticClientCapabilities(),
                    // Not a gate like the three above: the reader below already takes both answers, so
                    // nothing goes silent without this. What goes is the nesting, because a server that
                    // honours it sends the flat form to a client that never said it could take the tree.
                    DocumentSymbol: new DocumentSymbolClientCapabilities(HierarchicalDocumentSymbolSupport: true),
                    SemanticTokens: new SemanticTokensClientCapabilities(new(),
                        ["namespace", "type", "class", "enum", "interface", "struct", "typeParameter",
                         "parameter", "variable", "property", "enumMember", "event", "function", "method",
                         "macro", "keyword", "modifier", "comment", "string", "number", "regexp", "operator", "decorator"],
                        ["declaration", "definition", "readonly", "static", "deprecated", "abstract",
                         "async", "modification", "documentation", "defaultLibrary"], ["relative"])),
                new WorkspaceClientCapabilities(
                    ExecuteCommand: new ExecuteCommandClientCapabilities(),
                    WorkspaceFolders: true,
                    Diagnostics: new WorkspaceDiagnosticsClientCapabilities(),
                    FileOperations: new FileOperationsClientCapabilities()),
                // The same declare-and-gate bargain as save and codeLens: silence otherwise is correct.
                Window: new WindowClientCapabilities()));

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
            raw = await InvokeRequestAsync<JsonElement>(_rpc, "initialize", initParams, cancellationToken, _initializeTimeout);
            _initializeAnswered = true;
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
        Interlocked.Increment(ref _connection);
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
                var uri = new Uri(folder.Directory).AbsoluteUri;
                // A name is not a URI, so the textual export pass cannot recognise it. Assign the
                // same name as the URI's final segment here, using the export's session mapping.
                var name = _workspaceNames is { IsPseudonymising: true }
                    ? _workspaceNames.Uri(uri).TrimEnd('/').Split('/')[^1]
                    : folder.Name;
                wire.Add(new WorkspaceFolder(uri, name));
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

        // Shared with the {workspaceUri} launch placeholder rather than spelled again here. A server told
        // one directory on its command line and a different one at initialize answers about the wrong
        // workspace without failing, which is the worst shape a disagreement can take.
        var uri = LspWorkspaceUri.For(directory);

        // A path that cannot be made into a URI costs the root, not the connection.
        if (uri is null)
            _logger.LogWarning("Could not express the workspace directory {Directory} as a URI", directory);

        return uri;
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

    /// <summary>
    /// Membership is a ROUTER concern, so one connection ignores it: by the time a document reaches this
    /// class the registry has already decided that this server should see it. Implemented rather than
    /// refused because the interface is what the registry itself implements, and a connection used
    /// directly (the tests, and the single-server path) must accept the same call.
    /// </summary>
    public Task OpenDocumentAsync(
        string uri, string text, bool isProjectMember, CancellationToken cancellationToken = default) =>
        OpenDocumentAsync(uri, text, cancellationToken);

    public async Task OpenDocumentAsync(string uri, string text, CancellationToken cancellationToken = default)
    {
        // Tracked BEFORE the gate, deliberately: a document opened while no server is up must still be
        // replayed when one arrives, which is what makes lazy start and reconnect work at all.
        var (document, alreadyOpen) = TrackOpen(uri, text);

        // The replay at the end of this connection's handshake has opened it already, and asked for its
        // diagnostics: a second open would be a duplicate, and one sent from an older copy (#719).
        if (alreadyOpen) return;

        await SendDidOpenAsync(uri, document.Version, document.Text);
        PullDiagnosticsInBackground(uri);
    }

    /// <summary>
    /// Records an open of <paramref name="uri"/>, and says whether this connection's server has been opened on
    /// it already.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Never behind what is tracked.</b> A change recorded before the open, which happens while a server is
    /// starting, is newer than the open's text: the edit was made after the open began. So the tracked copy is
    /// kept, and the open carries it, rather than the open's text at the opening version replacing it. Replacing
    /// it sent the server the text from before the edit, at a lower version than one it had just been given,
    /// and left nothing to resend the edit (hexide-io/HexIDE#719).
    /// </para>
    /// <para>
    /// A document is marked opened on this connection only while there is one. Opened before any handshake,
    /// it is the replay that opens it.
    /// </para>
    /// </remarks>
    private (TrackedDocument Document, bool AlreadyOpen) TrackOpen(string uri, string text)
    {
        while (true)
        {
            var connection = _initialized ? Volatile.Read(ref _connection) : NotOpened;
            if (!_openDocuments.TryGetValue(uri, out var tracked))
            {
                var added = new TrackedDocument(LspDocumentVersion.Opening, text, connection);
                if (_openDocuments.TryAdd(uri, added)) return (added, false);
                continue;
            }

            if (connection != NotOpened && tracked.OpenedOn == connection) return (tracked, true);

            var opened = tracked.Version > LspDocumentVersion.Opening
                ? tracked with { OpenedOn = connection }
                : new TrackedDocument(LspDocumentVersion.Opening, text, connection);
            if (_openDocuments.TryUpdate(uri, opened, tracked)) return (opened, false);
        }
    }

    /// <summary>
    /// Marks a tracked document opened on the current connection, for the replay. Null if it is no longer
    /// tracked, or this connection's server has been opened on it already.
    /// </summary>
    private TrackedDocument? MarkOpened(string uri)
    {
        var connection = Volatile.Read(ref _connection);
        while (_openDocuments.TryGetValue(uri, out var tracked))
        {
            if (tracked.OpenedOn == connection) return null;
            var opened = tracked with { OpenedOn = connection };
            if (_openDocuments.TryUpdate(uri, opened, tracked)) return opened;
        }
        return null;
    }

    /// <summary>Whether this connection's server has been opened on <paramref name="uri"/>.</summary>
    private bool IsOpenOnThisConnection(string uri) =>
        _openDocuments.TryGetValue(uri, out var tracked) && tracked.OpenedOn == Volatile.Read(ref _connection);

    public async Task ChangeDocumentAsync(string uri, int version, string text, CancellationToken cancellationToken = default)
    {
        // Keep the version check, tracking and send ordered: a debounce can cross a newer flush (#720).
        await _changeGate.WaitAsync();
        try
        {
            while (true)
            {
                if (_openDocuments.TryGetValue(uri, out var tracked))
                {
                    if (version <= tracked.Version) return;
                    if (_openDocuments.TryUpdate(uri, tracked with { Version = version, Text = text }, tracked)) break;
                }
                else if (_openDocuments.TryAdd(uri, new TrackedDocument(version, text))) break;
            }
            var rpc = _rpc;
            if (rpc is null || !_initialized || !ServerCapabilities.AcceptsChanges(_capabilities?.Value)) return;

            // Not until the server has been opened on it. A change to a document it has never heard of is not one a
            // server can apply, and a foreign server measured here ignores it. The open still to come, from the replay
            // or from the registry once its last claimant has started, carries this text instead, because it was
            // tracked above (#719).
            if (!IsOpenOnThisConnection(uri)) return;
            var p = new DidChangeTextDocumentParams(
                new VersionedTextDocumentIdentifier(uri, version),
                [new TextDocumentContentChangeEvent(text)]);
            await CancelSupersededAsync(uri);
            try { await rpc.NotifyWithParameterObjectAsync("textDocument/didChange", p); }
            catch (Exception ex) { WarnRequestFailedOnce("textDocument/didChange", ex, cancellationToken); }

            // After the notification rather than before it: the answer is supposed to describe the text the
            // server has just been given, and asking first would describe the text before this edit.
            PullDiagnosticsInBackground(uri);
        }
        finally { _changeGate.Release(); }
    }

    public async Task CloseDocumentAsync(string uri, CancellationToken cancellationToken = default)
    {
        _openDocuments.TryRemove(uri, out _);
        ForgetPullState(uri);
        var rpc = _rpc;
        if (rpc is null || !_initialized || !ServerCapabilities.AcceptsOpenClose(_capabilities?.Value)) return;
        var p = new DidCloseTextDocumentParams(new TextDocumentIdentifier(uri));
        try { await rpc.NotifyWithParameterObjectAsync("textDocument/didClose", p); }
        catch (Exception ex) { WarnRequestFailedOnce("textDocument/didClose", ex, cancellationToken); }
    }

    // ── Diagnostics this server will only give when asked ────────────────────────────────────────────
    //
    // A server may deliver diagnostics by answering `textDocument/diagnostic` instead of publishing them
    // (LSP 3.17). Nothing downstream of RaisePublishDiagnostics can tell which happened, and that is the
    // requirement rather than a convenience — `lsp-client` asks for one channel, and a marker pipeline that
    // had to know how its diagnostics were obtained would be the seam leaking.

    /// <summary>What the server called its last answer about each document, so it can say "still true".</summary>
    private readonly ConcurrentDictionary<string, string> _pullResultIds = new();

    /// <summary>
    /// How many times this client has asked about each document. A request carries the value it saw, and
    /// the answer is compared against <see cref="_pullShown"/> to decide whether it is still worth showing.
    /// </summary>
    private readonly ConcurrentDictionary<string, int> _pullGeneration = new();

    /// <summary>
    /// The newest generation whose answer has actually reached the editor, per document.
    /// </summary>
    /// <remarks>
    /// <b>Two counters rather than one, because one was measurably wrong.</b> The first version compared an
    /// answer's generation against the latest generation ASKED and dropped anything older. That reads as
    /// "do not show markers for text the user has edited past", and it does do that — but it also drops the
    /// answer to every request that was overtaken before it came back, and if the last request's answer is
    /// the one that goes missing or arrives out of order, the document is left showing <em>nothing at all</em>,
    /// permanently. Overlapping requests are not exotic: <c>workspace/diagnostic/refresh</c> arriving while a
    /// pull is in flight produces them every time, which is how a scripted server reproduced a document with
    /// three answered requests and no diagnostics on screen.
    ///
    /// <para>
    /// Comparing against what has been SHOWN instead makes the rule monotonic: the newest answer to arrive
    /// always wins and nothing can leave the document blank. The cost is that a stale answer arriving before
    /// its newer sibling is displayed briefly and then replaced — which is exactly what a publishing server
    /// does anyway, and enormously preferable to silence.
    /// </para>
    /// </remarks>
    private readonly ConcurrentDictionary<string, int> _pullShown = new();

    /// <summary>
    /// Held while an answer is checked and published, and while a close clears what was published.
    /// </summary>
    /// <remarks>
    /// <b>The check and the publication are one step, or the check means nothing.</b> An answer used to claim its
    /// slot in <see cref="_pullShown"/> and then parse and publish, so a stale answer that had passed its check
    /// could still be publishing when a newer one checked, won and published, and the stale one then landed last
    /// and stayed on screen, the exact outcome the counters exist to prevent. Answers to overlapping requests
    /// complete on whichever pool threads are free, so on a loaded machine the interleave happened on its own: a
    /// test guarding this failed on CI at any deadline (hexide-io/HexIDE#503). A close is a publication too, of an
    /// empty set, and an answer that had passed its "still open" check used to be able to put marks back after it.
    /// One gate for every document: what it guards is a parse and an event, and contention is not the risk here.
    /// </remarks>
    private readonly object _pullPublishGate = new();

    /// <summary>
    /// True when this connection's diagnostics come from asking, so anything it publishes is redundant.
    /// </summary>
    internal bool IgnoresPublicationsFromThisServer() => CanServeQuietly("diagnosticProvider");

    /// <summary>
    /// Records, once per connection, that publications are being dropped in favour of asking.
    /// </summary>
    /// <remarks>
    /// Silence here would be the defect this whole area keeps producing. A server author watching the
    /// protocol inspector, having published diagnostics that never appeared, needs to be told that the
    /// client asked instead — otherwise "my publishDiagnostics is ignored" looks like a broken client rather
    /// than a client honouring the capability their own server advertised.
    /// </remarks>
    private void NoteIgnoredPublicationOnce()
    {
        if (!_warnedCapabilities.TryAdd("publishDiagnostics/ignored", 0)) return;

        Note(ConversationDirection.Received, ConversationEntryKind.Unconsumed, "textDocument/publishDiagnostics",
            "ignored: this server advertised 'diagnosticProvider', so its diagnostics are taken from "
          + "textDocument/diagnostic instead. Publishing as well is legal; taking both would make the "
          + "document's marks depend on which arrived last.");

        _logger.LogDebug(
            "Ignoring publishDiagnostics from a server that advertised diagnosticProvider; using the "
          + "pull model for this connection instead.");
    }

    /// <summary>
    /// A close that also says, for certain, that this connection now has nothing to report about the name.
    /// </summary>
    /// <remarks>
    /// The clearing publication is unconditional here, where <see cref="ForgetPullState"/> raises it only
    /// for a server that answers when asked. For an ordinary close that gate is right: a PUSH server clears
    /// a document by publishing an empty set itself, and pre-empting it would fight a server that has a
    /// view. For a rename it is wrong — the name is going away, so there is no later publication to wait
    /// for and a push server will never send one about a document it was told to close.
    /// </remarks>
    public async Task CloseDocumentForRenameAsync(string uri, CancellationToken cancellationToken = default)
    {
        await CloseDocumentAsync(uri, cancellationToken);
        RaisePublishDiagnostics(DiagnosticOwner.LanguageServer, new PublishDiagnosticsParams(uri, []));
    }

    private void ForgetPullState(string uri)
    {
        // The result identifier goes, because it names an answer about a document this client has stopped
        // tracking. The two COUNTERS stay, and that is deliberate: resetting them makes a reopened document
        // start at generation 1 again, so an answer still in flight from before the close — which carries a
        // higher number — outranks every fresh answer that follows it, and the reopened document shows
        // pre-close diagnostics until the counter climbs back past it. Monotonic per document for the life
        // of the connection costs two integers per file ever opened and has no such window.
        lock (_pullPublishGate)
        {
            _pullResultIds.TryRemove(uri, out _);

            // Nothing else would. A push server clears a document by publishing an empty set for it, and a
            // server that only answers when asked has no way to say anything about a document we have stopped
            // asking about — so the marks would outlive the editor that showed them.
            if (CanServeQuietly("diagnosticProvider"))
                RaisePublishDiagnostics(DiagnosticOwner.LanguageServer, new PublishDiagnosticsParams(uri, []));
        }
    }

    /// <summary>
    /// Asks, without making the caller wait for the answer.
    /// </summary>
    /// <remarks>
    /// Deliberately not awaited. Opening a document must not block on a round trip to a linter — a server
    /// that publishes delivers its diagnostics asynchronously, and a server that answers must not be slower
    /// to open a file merely because of how it delivers the same thing. <see cref="PullDiagnosticsAsync"/>
    /// swallows its own failures for the same reason every other request path here does.
    /// </remarks>
    private void PullDiagnosticsInBackground(string uri)
    {
        if (!CanServeQuietly("diagnosticProvider")) return;
        var generation = _pullGeneration.AddOrUpdate(uri, 1, (_, n) => n + 1);
        _ = PullDiagnosticsAsync(uri, generation);
    }

    /// <summary>
    /// Asks the server what it has to say about one document, and puts the answer on the one channel.
    /// </summary>
    /// <remarks>
    /// <b>Gated with <c>Supports</c> rather than <see cref="CanServe"/>, and that is the point rather than an
    /// oversight.</b> Every other gate warns when the capability is absent, because a server without
    /// <c>hoverProvider</c> cannot do hover. A server without <c>diagnosticProvider</c> is not deficient: it
    /// publishes instead, which is what four of the five servers this suite drives do, and what the
    /// specification treats as the ordinary case. Warning here would put a line in the log blaming every
    /// conformant server for conforming.
    /// </remarks>
    private async Task PullDiagnosticsAsync(string uri, int generation)
    {
        var rpc = _rpc;
        if (rpc is null || !_initialized) return;

        _pullResultIds.TryGetValue(uri, out var previousResultId);
        var p = new DocumentDiagnosticParams(
            new TextDocumentIdentifier(uri),
            ServerCapabilities.DiagnosticIdentifier(_capabilities?.Value),
            previousResultId);

        var connection = Volatile.Read(ref _connection);
        _openDocuments.TryGetValue(uri, out var document);
        var delay = 250;
        JsonElement? raw;
        while (true)
        {
            try
            {
                raw = await InvokeRequestAsync<JsonElement?>(rpc, "textDocument/diagnostic", p, supersededUri: uri);
                break;
            }
            catch (RemoteInvocationException ex) when (ShouldRetryDiagnostic(ex))
            {
                await Task.Delay(delay);
                delay = Math.Min(delay * 2, 4000);
                // A fresh pull owns a newer generation. A close/reopen or reconnect must not revive this one.
                if (!_initialized || !ReferenceEquals(rpc, _rpc)
                    || connection != Volatile.Read(ref _connection)
                    || !_openDocuments.TryGetValue(uri, out var current) || current != document
                    || !_pullGeneration.TryGetValue(uri, out var latest) || latest != generation)
                    return;
            }
            catch (Exception ex)
            {
                WarnRequestFailedOnce("textDocument/diagnostic", ex, CancellationToken.None);
                return;
            }
        }

        // Checked and published under one gate, or a newer answer can publish between the two and this one land
        // on top of it (#503). See _pullPublishGate.
        lock (_pullPublishGate)
        {
            // Closed while we were asking. Nothing may be published for a document the editor has let go — the
            // close already cleared it, and putting marks back afterwards would leave them with nothing that
            // could ever remove them.
            if (!_openDocuments.ContainsKey(uri)) return;

            // Already overtaken: something newer is on screen, so this answer describes text nobody is looking
            // at. Claiming this slot and finding we did not win is the whole check — see _pullShown for why it
            // is measured against what has been shown rather than against what has been asked.
            if (_pullShown.AddOrUpdate(uri, generation, (_, shown) => Math.Max(shown, generation)) != generation)
                return;

            ApplyDiagnosticReport(uri, raw);
        }
    }

    private sealed class LspJsonRpc(IJsonRpcMessageHandler handler, object target) : JsonRpc(handler, target)
    {
        // The default CommonErrorData discards LSP's retriggerRequest field.
        protected override Type? GetErrorDetailsDataType(StreamJsonRpc.Protocol.JsonRpcError error) =>
            error.Error?.Code == (StreamJsonRpc.Protocol.JsonRpcErrorCode)(-32802) ? typeof(JsonElement) : base.GetErrorDetailsDataType(error);
    }

    private static bool ShouldRetryDiagnostic(RemoteInvocationException ex) =>
        ex.ErrorCode == -32801
        || (ex.ErrorCode == -32802
            && !(ex.ErrorData is JsonElement { ValueKind: JsonValueKind.Object } data
                && data.TryGetProperty("retriggerRequest", out var retrigger)
                && retrigger.ValueKind == JsonValueKind.False));

    /// <summary>
    /// Reads one diagnostic report and publishes what it means.
    /// </summary>
    /// <remarks>
    /// <b>An <c>unchanged</c> report is not an empty one, and confusing the two is destructive.</b> It means
    /// "what I told you last time still stands" and carries no items at all — so handling it as an empty set
    /// would erase exactly the diagnostics it was sent to preserve, and only on the second request, so the
    /// marks would appear correctly and then vanish. That is why this returns rather than publishes.
    ///
    /// <para>
    /// Read from a raw element rather than a typed result because the protocol defines the reply as a union
    /// discriminated by <c>kind</c> — <c>RelatedFullDocumentDiagnosticReport | RelatedUnchangedDocumentDiagnosticReport</c>
    /// — which a source-generated resolver cannot deserialize into one class. <c>relatedDocuments</c> is
    /// ignored, which is honest rather than lazy: this client does not declare
    /// <c>relatedDocumentSupport</c>, so a conformant server sends none.
    /// </para>
    /// </remarks>
    internal void ApplyDiagnosticReport(string uri, JsonElement? raw)
    {
        if (raw is not { ValueKind: JsonValueKind.Object } report) return;

        if (report.TryGetProperty("resultId", out var resultId)
            && resultId.ValueKind == JsonValueKind.String
            && resultId.GetString() is { Length: > 0 } id)
        {
            _pullResultIds[uri] = id;
        }

        var kind = report.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String
            ? k.GetString()
            : null;

        // `unchanged` says the previous answer stands. Leave what is showing exactly as it is.
        if (string.Equals(kind, "unchanged", StringComparison.Ordinal)) return;

        // Anything else is a full report. `items` is required on one, and a report without it is a server
        // saying it has nothing to report — which is an empty set and must clear, not be ignored.
        var diagnostics = report.TryGetProperty("items", out var items)
            ? ReadDiagnostics(items)
            : [];

        RaisePublishDiagnostics(DiagnosticOwner.LanguageServer, new PublishDiagnosticsParams(uri, diagnostics));
    }

    /// <summary>
    /// The diagnostics out of a report's <c>items</c>, skipping any this client cannot make sense of.
    /// </summary>
    /// <remarks>
    /// One malformed entry costs that entry rather than the document's whole set. The alternative — letting
    /// the deserializer throw on the array — would discard a server's every diagnostic because one of them
    /// had a shape we did not expect, which is the failure mode #238 was.
    /// </remarks>
    internal static Diagnostic[] ReadDiagnostics(JsonElement items)
    {
        if (items.ValueKind != JsonValueKind.Array) return [];

        var read = new List<Diagnostic>(items.GetArrayLength());
        foreach (var item in items.EnumerateArray())
        {
            if (ReadDiagnostic(item) is { } diagnostic) read.Add(diagnostic);
        }
        return [.. read];
    }

    /// <summary>
    /// One diagnostic, or null when the element is not one at all.
    /// </summary>
    /// <remarks>
    /// Read field by field rather than deserialized, which is the treatment every other foreign reply gets
    /// here: a server we did not write must not be able to make a consumer throw by sending a member in a
    /// shape this client did not anticipate.
    ///
    /// <para>
    /// A diagnostic with no range is dropped. Its whole purpose is to mark a place in a document, and one
    /// without a place cannot be rendered — where keeping it would put a null somewhere nothing checks.
    /// </para>
    /// </remarks>
    private static Diagnostic? ReadDiagnostic(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty("range", out var rangeElement)) return null;
        if (ReadRange(rangeElement) is not { } range) return null;

        var message = element.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
            ? m.GetString() ?? string.Empty
            : string.Empty;

        DiagnosticSeverity? severity = element.TryGetProperty("severity", out var s)
                                       && s.ValueKind == JsonValueKind.Number
                                       && s.TryGetInt32(out var level)
            ? (DiagnosticSeverity)level
            : null;

        var source = element.TryGetProperty("source", out var src) && src.ValueKind == JsonValueKind.String
            ? src.GetString()
            : null;

        // Cloned, for the same reason the advertised capabilities are: this element belongs to the
        // document the reply was parsed from, which is disposed when this call returns, and what is left
        // behind is an element that throws on the first read from whoever kept it.
        JsonElement? code = element.TryGetProperty("code", out var c)
                            && c.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? c.Clone()
            : null;

        var href = element.TryGetProperty("codeDescription", out var description)
                   && description.ValueKind == JsonValueKind.Object
                   && description.TryGetProperty("href", out var h)
                   && h.ValueKind == JsonValueKind.String
            ? h.GetString()
            : null;

        return new Diagnostic(range, message, severity, source, code,
            href is null ? null : new CodeDescription(href));
    }

    public async Task DidCreateFilesAsync(string[] uris, CancellationToken cancellationToken = default)
    {
        var rpc = _rpc;
        if (rpc is null || !_initialized) return;
        var files = uris.Where(uri => FileOperationFilters.Matches(_capabilities?.Value, "didCreate", uri))
            .Select(uri => new FileOperationUri(uri)).ToArray();
        if (files.Length == 0) return;
        try { await rpc.NotifyWithParameterObjectAsync("workspace/didCreateFiles", new FileOperationParams(files)); }
        catch (Exception ex) { WarnRequestFailedOnce("workspace/didCreateFiles", ex, cancellationToken); }
    }

    public async Task DidRenameFilesAsync(FileRename[] files, CancellationToken cancellationToken = default)
    {
        var rpc = _rpc;
        if (rpc is null || !_initialized) return;
        var matching = files.Where(file => FileOperationFilters.Matches(_capabilities?.Value, "didRename", file.OldUri)
            || FileOperationFilters.Matches(_capabilities?.Value, "didRename", file.NewUri)).ToArray();
        if (matching.Length == 0) return;
        try { await rpc.NotifyWithParameterObjectAsync("workspace/didRenameFiles", new RenameFilesParams(matching)); }
        catch (Exception ex) { WarnRequestFailedOnce("workspace/didRenameFiles", ex, cancellationToken); }
    }

    public async Task DidDeleteFilesAsync(string[] uris, CancellationToken cancellationToken = default)
    {
        var rpc = _rpc;
        if (rpc is null || !_initialized) return;
        var files = uris.Where(uri => FileOperationFilters.Matches(_capabilities?.Value, "didDelete", uri))
            .Select(uri => new FileOperationUri(uri)).ToArray();
        if (files.Length == 0) return;
        try { await rpc.NotifyWithParameterObjectAsync("workspace/didDeleteFiles", new FileOperationParams(files)); }
        catch (Exception ex) { WarnRequestFailedOnce("workspace/didDeleteFiles", ex, cancellationToken); }
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

        // Asked again, because a save is a reason to think the answer changed and a server that only
        // answers when asked has no way to volunteer that it has. This matters most for the very servers
        // that make save meaningful — the ones deferring their analysis until a file is written — for which
        // not re-asking here means their entire output is unreachable.
        PullDiagnosticsInBackground(uri);
    }

    public async Task<HoverResult?> RequestHoverAsync(string uri, Position position, CancellationToken cancellationToken = default)
    {
        var unavailable = _rpc is null ? "no RPC connection"
            : !_initialized ? "initialization is incomplete"
            : !CanServe("hoverProvider") ? "hoverProvider is unavailable" : null;
        if (unavailable is not null)
        {
            _logger.LogDebug("textDocument/hover was not sent: {Reason}", unavailable);
            return null;
        }
        var p = new TextDocumentPositionParams(new TextDocumentIdentifier(uri), position);
        try
        {
            var result = await InvokeRequestAsync<HoverResult?>(_rpc!,
                "textDocument/hover", p, cancellationToken);
            if (result is null) _logger.LogDebug("textDocument/hover: server returned null");
            return result;
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
            var raw = await InvokeRequestAsync<JsonElement?>(_rpc,
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

    public async Task<SemanticTokenResult?> RequestSemanticTokensAsync(string uri, CancellationToken cancellationToken = default)
    {
        if (_rpc is null || !_initialized || !ServerCapabilities.SupportsSemanticTokensFull(AdvertisedCapabilities)) return null;
        try
        {
            var legend = AdvertisedCapabilities!.Value.GetProperty("semanticTokensProvider").GetProperty("legend")
                .GetProperty("tokenTypes").EnumerateArray().Select(t => t.GetString()!).ToArray();
            var result = await InvokeRequestAsync<SemanticTokens>(_rpc, "textDocument/semanticTokens/full",
                new SemanticTokensParams(new TextDocumentIdentifier(uri)), cancellationToken, supersededUri: uri);
            if (result?.Data is not { Length: > 0 })
            {
                if (_semanticWarnings.TryAdd(uri, 0))
                    _logger.LogDebug("[LSP] No semantic tokens for {Uri}; keeping lexical colours", uri);
                return null;
            }
            return new SemanticTokenResult(result.Data, legend);
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException && !cancellationToken.IsCancellationRequested && _semanticWarnings.TryAdd(uri, 0))
                _logger.LogWarning(ex, "[LSP] Semantic tokens failed for {Uri}; keeping lexical colours", uri);
            return null;
        }
    }

    public async Task<FoldingRange[]> RequestFoldingRangesAsync(string uri, CancellationToken cancellationToken = default)
    {
        if (_rpc is null || !_initialized || !CanServe("foldingRangeProvider")) return [];
        var p = new FoldingRangeParams(new TextDocumentIdentifier(uri));
        try
        {
            return await InvokeRequestAsync<FoldingRange[]>(_rpc,
                "textDocument/foldingRange", p, cancellationToken, supersededUri: uri) ?? [];
        }
        catch (Exception ex)
        {
            WarnRequestFailedOnce("textDocument/foldingRange", ex, cancellationToken);
            return [];
        }
    }

    public Task<CompletionItem[]> RequestCompletionAsync(string uri, Position position, CancellationToken cancellationToken = default) =>
        RequestCompletionAsync(uri, position, cancellationToken, static () => { });

    public async Task<CompletionItem[]> RequestCompletionAsync(string uri, Position position,
        CancellationToken cancellationToken, Action onAnswer)
    {
        if (_rpc is null || !_initialized || !CanServe("completionProvider")) return [];
        var p = new CompletionParams(new TextDocumentIdentifier(uri), position);
        try
        {
            var raw = await InvokeRequestAsync<JsonElement?>(_rpc,
                "textDocument/completion", p, cancellationToken);
            var result = raw is not { } value || value.ValueKind == JsonValueKind.Null
                ? null
                : value.ValueKind == JsonValueKind.Array
                    ? new CompletionList(false, value.Deserialize(LspJsonContext.Default.CompletionItemArray)!)
                    : value.Deserialize(LspJsonContext.Default.CompletionList);
            onAnswer();
            return result?.Items ?? [];
        }
        catch (Exception ex)
        {
            WarnRequestFailedOnce("textDocument/completion", ex, cancellationToken);
            return [];
        }
    }

    public async Task<SignatureHelp?> RequestSignatureHelpAsync(string uri, Position position, CancellationToken cancellationToken = default, string? triggerCharacter = null)
    {
        if (_rpc is null || !_initialized || !CanServe("signatureHelpProvider")) return null;
        var p = new SignatureHelpParams(new TextDocumentIdentifier(uri), position);
        try
        {
            return await InvokeRequestAsync<SignatureHelp?>(_rpc,
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
            var raw = await InvokeRequestAsync<JsonElement?>(_rpc,
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
            var raw = await InvokeRequestAsync<JsonElement?>(_rpc,
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
            return await InvokeRequestAsync<DocumentHighlight[]?>(_rpc,
                "textDocument/documentHighlight", p, cancellationToken);
        }
        catch (Exception ex)
        {
            WarnRequestFailedOnce("textDocument/documentHighlight", ex, cancellationToken);
            return null;
        }
    }

    public async Task<RenameResult?> RequestRenameAsync(string uri, Position position, string newName, CancellationToken cancellationToken = default)
    {
        if (_rpc is null || !_initialized || !CanServe("renameProvider")) return null;
        var p = new RenameParams(new TextDocumentIdentifier(uri), position, newName);
        try
        {
            var edit = await InvokeRequestAsync<WorkspaceEdit?>(_rpc,
                "textDocument/rename", p, cancellationToken);
            return edit is null ? null : new RenameResult(edit, null);
        }
        catch (RemoteInvocationException ex) when (!cancellationToken.IsCancellationRequested)
        {
            if (ex.ErrorCode is -32800 or -32801)
            {
                _logger.LogDebug(ex, "textDocument/rename was cancelled by the server");
                return null;
            }

            if (ex.ErrorCode == -32803)
                _logger.LogDebug(ex, "textDocument/rename refused: {Reason}", ex.Message);
            else
                WarnRequestFailedOnce("textDocument/rename", ex, cancellationToken);
            return new RenameResult(null, ex.Message);
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
            return await InvokeRequestAsync<TextEdit[]?>(_rpc,
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
            lenses = await InvokeRequestAsync<CodeLens[]?>(_rpc,
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
            return await InvokeRequestAsync<CodeLens?>(_rpc,
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

    private WorkspaceFolder[]? _sentWorkspaceFolders;

    public async Task UpdateWorkspaceFoldersAsync(CancellationToken cancellationToken = default)
    {
        if (_rpc is null || !_initialized) return;
        var capabilities = AdvertisedCapabilities;
        if (capabilities is not { ValueKind: JsonValueKind.Object } caps
            || !caps.TryGetProperty("workspace", out var workspace)
            || workspace.ValueKind != JsonValueKind.Object
            || !workspace.TryGetProperty("workspaceFolders", out var folders)
            || folders.ValueKind != JsonValueKind.Object
            || !folders.TryGetProperty("changeNotifications", out var notifications)
            || !(notifications.ValueKind == JsonValueKind.True
                || notifications.ValueKind == JsonValueKind.String)) return;

        var current = (WorkspaceFolders() ?? []).DistinctBy(f => f.Uri).ToArray();
        var previous = _sentWorkspaceFolders ?? [];
        var added = current.Where(f => !previous.Any(p => p.Uri == f.Uri)).ToArray();
        var removed = previous.Where(f => !current.Any(p => p.Uri == f.Uri)).ToArray();
        if (added.Length == 0 && removed.Length == 0) return;
        cancellationToken.ThrowIfCancellationRequested();
        await _rpc.NotifyWithParameterObjectAsync("workspace/didChangeWorkspaceFolders",
            new DidChangeWorkspaceFoldersParams(new WorkspaceFoldersChangeEvent(added, removed)));
        _sentWorkspaceFolders = current;
    }

    private bool _serverRefreshesDiagnostics;

    public async Task<JsonElement?> ImportProjectAsync(ProjectImportSpec spec, string uri, string text,
        CancellationToken cancellationToken = default)
    {
        if (_rpc is null || !_initialized) throw new InvalidOperationException("The server is not initialized.");
        if (!ServerCapabilities.DeclaresCommand(AdvertisedCapabilities, spec.Command))
            throw new InvalidOperationException($"The server does not advertise project-import command {spec.Command}.");
        var result = await InvokeRequestAsync<JsonElement?>(_rpc, "workspace/executeCommand",
            new ExecuteCommandParams(spec.Command, spec.ExpandArguments(uri, text)), cancellationToken);
        if (result is { ValueKind: JsonValueKind.Object } reply && reply.TryGetProperty("notes", out var notes))
        {
            var messages = notes.ValueKind == JsonValueKind.Array ? notes.EnumerateArray().ToArray() : [notes];
            foreach (var note in messages)
            {
                var textValue = note.ValueKind == JsonValueKind.Object && note.TryGetProperty("message", out var message)
                    ? message : note;
                if (textValue.ValueKind != JsonValueKind.String) continue;
                LogServerMessage(new LogMessageParams(LspMessageType.Info, textValue.GetString()!));
            }
        }
        if (!_serverRefreshesDiagnostics)
        {
            _pullResultIds.Clear();
            foreach (var document in _openDocuments.Keys) PullDiagnosticsInBackground(document);
        }
        return result;
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
            return await InvokeRequestAsync<System.Text.Json.JsonElement?>(_rpc,
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
            return await InvokeRequestAsync<VbaBuiltinSymbol[]>(_rpc,
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
            var raw = await InvokeRequestAsync<JsonElement?>(_rpc,
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

    /// <summary>How long stopping waits on a server, once for the answer to <c>shutdown</c> and once for <c>exit</c> to go out.</summary>
    internal static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);

    public async Task StopAsync()
    {
        ClearProgress();
        _stopping = true;
        // Safe even if the reconnect loop already disposed the CTS in its finally.
        try { _reconnectCts?.Cancel(); } catch (ObjectDisposedException) { }

        // Wait for any in-flight reconnect loop to fully exit so it cannot establish a new
        // connection after we have torn everything down.
        Task reconnectTask;
        lock (_reconnectGate) { reconnectTask = _reconnectTask; }
        try { await reconnectTask; } catch { /* loop is best-effort */ }

        var rpc = _rpc;
        if (rpc is not null && _initializeAnswered && !_transport.LeaveServerRunning)
        {
            // Both take NO parameters (LSP 3.17), and the distinction is not pedantry.
            // `InvokeAsync(name)` is StreamJsonRpc's POSITIONAL overload and puts `"params":[]` on
            // the wire, which rumdl (tower-lsp) and an OmniSharp-based server both REJECT; and
            // `EmptyParams.Instance` sends `"params":{}`, which rumdl also rejects. `"params":null` is
            // rejected too: rumdl 0.2.64 answers -32602, "Unexpected params: null" (#368). Only omitting the
            // member is accepted by every server measured, and it is what the specification and
            // vscode-languageserver-node send. In the parameter-object overload below, a null argument
            // object omits the member; JSON null on the wire is not equivalent to absence.
            //
            // The cost of getting this wrong is not a rejected request: LSP has a server exit 0 when a
            // shutdown preceded exit and 1 otherwise, so a server that refuses ours correctly reports
            // that we never shut it down, and any supervisor reads every clean exit as a crash.
            // See hexide-io/HexIDE#312.
            // A live but wedged server must not hold IDE shutdown indefinitely (#392).
            try
            {
                await InvokeRequestAsync<object?>(rpc, "shutdown", null, timeout: ShutdownTimeout);
            }
            catch { }
            // Bounded too (#1221). A server that has stopped reading fills the pipe, so the write ahead of this one
            // never finishes and exit queues behind it for good. The shutdown deadline above does not cover that: it
            // ends the wait for an answer, not the send. Disposing the connection below ends the stuck writes.
            try { await rpc.NotifyWithParameterObjectAsync("exit", null).WaitAsync(ShutdownTimeout); } catch { }
        }
        DisposeRpc();

        _transport.Closed -= OnTransportClosed;
        await _transport.DisposeAsync();
        _transport.Notice -= OnTransportNotice;
        _initialized = false;
        _capabilities = null;
        _identity = null;
        _warnedCapabilities.Clear();
        _semanticWarnings.Clear();
        _prioritySkips.Clear();
        _warnedFailures.Clear();
        RaiseStateChanged();
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    /// <summary>
    /// Puts the process's own output on the timeline, beside the messages it interleaves with.
    /// </summary>
    /// <remarks>
    /// Attribution comes from the client rather than the transport because the connection id lives here:
    /// with several servers attached, standard error is only useful if you can tell which one wrote it.
    /// </remarks>
    private void OnTransportNotice(object? sender, TransportNotice notice)
    {
        // The two directions are not interchangeable and ConversationDirection already says which is
        // which: Received is "a reply, a notification, a line of standard error", Local is "a process
        // starting". A stderr line arrived from the server; an exit code is something observed about it.
        var (direction, kind) = notice.Kind switch
        {
            TransportNoticeKind.StandardError => (ConversationDirection.Received, ConversationEntryKind.StandardError),
            TransportNoticeKind.StandardOutput => (ConversationDirection.Received, ConversationEntryKind.StandardOutput),
            _ => (ConversationDirection.Local, ConversationEntryKind.Lifecycle),
        };

        // A non-zero exit is a fault (#945), while this client stops the server too (#1314). LSP has a server exit 0
        // when a shutdown preceded exit and non-zero otherwise, so the code is the server's account of how the stop
        // went. Stdio disposal also reports the observed exit after its grace period or process-tree kill.
        Note(direction, kind, null, notice.Text, fault: notice.Fault);
    }

    private void OnTransportClosed(object? sender, EventArgs e)
    {
        _initialized = false;
        _capabilities = null;
        _warnedCapabilities.Clear();
        _semanticWarnings.Clear();
        _prioritySkips.Clear();
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

    private void ClearProgress()
    {
        foreach (var key in _progressTokens.Keys)
            if (_progressTokens.TryRemove(key, out var token))
                ProgressReceived?.Invoke(this, new ProgressParams(token, new WorkDoneProgressValue("end")));
    }

    internal void RaiseMessageShown(ShowMessageParams p) => MessageShown?.Invoke(this, p);

    private void LogServerMessage(LogMessageParams p)
    {
        _logger.Log(LspNotificationReceiver.LevelOf(p.Type), "[{Language} server] {Message}", _languageId, p.Message);
        RaiseMessageLogged(p);
    }

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

            // A server that offered to answer when asked is answered TO, and its unbidden publications are
            // dropped. Not a preference — taking both would put two sources of truth under one owner, where
            // each whole-document set replaces the other and the winner is decided by arrival order.
            //
            // It is a real shape, not a hypothetical: rumdl advertises `diagnosticProvider` AND publishes,
            // and once this client declares it can ask, what rumdl publishes is an EMPTY set while the real
            // diagnostics come back as the answer. Accepting both leaves the document's marks depending on
            // which landed second. Measured.
            if (_client.IgnoresPublicationsFromThisServer())
            {
                _client.NoteIgnoredPublicationOnce();
                return;
            }

            _client.RaisePublishDiagnostics(DiagnosticOwner.LanguageServer, p);
        }

        /// <summary>
        /// The server's own log output. Log is retained at Information; Debug goes to Trace.
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
            _client.LogServerMessage(p);
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

        [JsonRpcMethod("window/workDoneProgress/create", UseSingleObjectParameterDeserialization = true)]
        public void OnWorkDoneProgressCreate(WorkDoneProgressCreateParams p)
        {
            if (p.Token.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                _client._progressTokens.TryAdd(p.Token.ValueKind + ":" + p.Token.ToString(), p.Token);
        }

        // Measured with StreamJsonRpc 2.24.92: its reserved IProgress marshalling falls back to
        // this target for both string and integer tokens. No custom formatter is needed (#360).
        [JsonRpcMethod("$/progress", UseSingleObjectParameterDeserialization = true)]
        public void OnProgress(ProgressParams p)
        {
            if (p.Token.ValueKind is not (JsonValueKind.String or JsonValueKind.Number)) return;
            var key = p.Token.ValueKind + ":" + p.Token.ToString();
            switch (p.Value.Kind)
            {
                case "begin":
                    // rumdl sends begin without create, even when support was not advertised.
                    _client._progressTokens[key] = p.Token;
                    break;
                case "report":
                    if (!_client._progressTokens.ContainsKey(key)) return;
                    break;
                case "end":
                    if (!_client._progressTokens.TryRemove(key, out _)) return;
                    break;
                default:
                    return; // Other $/progress payloads (e.g. partial results) are not work-done progress.
            }
            _client.ProgressReceived?.Invoke(_client, p);
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

        /// <summary>
        /// The server asking to be asked again — <c>workspace/diagnostic/refresh</c>.
        /// </summary>
        /// <remarks>
        /// <b>Honoured rather than refused, because it is the only thing a pull-model server has.</b> Such a
        /// server speaks only when spoken to, so when something changes that is not a document — its
        /// configuration file, a rule set, a dependency it watches — it cannot tell anyone. This request is
        /// how it says "the answers you hold are stale", and refusing it leaves the editor showing
        /// diagnostics from a configuration that no longer exists, with no event that would ever correct
        /// them.
        ///
        /// <para>
        /// Every open document is asked about afresh, and the result identifiers are dropped first: holding
        /// them would invite the server to answer "unchanged" against the very answers being invalidated.
        /// </para>
        ///
        /// <para>
        /// <b>Parameterless, unlike every other handler here, and it has to be.</b> The specification gives
        /// this request no parameters at all, so a conformant server sends no <c>params</c> member — and a
        /// handler declared with the usual single-object deserialization then has a required argument
        /// nothing supplies. Measured: the client answered
        /// <c>-32602 "An argument was not supplied for a required parameter"</c> and the refresh was
        /// silently refused, which looks exactly like success from outside. Declared this way it binds
        /// whether the member is absent, an empty array, or an empty object.
        /// </para>
        /// </remarks>
        [JsonRpcMethod("workspace/diagnostic/refresh")]
        public void OnDiagnosticRefresh()
        {
            _client._serverRefreshesDiagnostics = true;
            _client._logger.LogDebug("The server asked for diagnostics to be refreshed; re-requesting.");
            _client._pullResultIds.Clear();
            foreach (var uri in _client._openDocuments.Keys)
                _client.PullDiagnosticsInBackground(uri);
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
        /// <c>Log</c> becomes Information so running commentary reaches the default log.
        /// <c>Debug</c>, added in 3.18, is noisier and goes to Trace.
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
        internal static LogLevel LevelOf(LspMessageType type) => type switch
        {
            LspMessageType.Error => LogLevel.Error,
            LspMessageType.Warning => LogLevel.Warning,
            LspMessageType.Info => LogLevel.Information,
            LspMessageType.Log => LogLevel.Information,
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
    /// Each entry is either a literal passed to <see cref="CanServe"/> somewhere below or a capability read
    /// through one of the dedicated readers on <c>ServerCapabilities</c>, and a test asserts the list is
    /// exactly those — so wiring a new method and forgetting this list fails the build rather than quietly
    /// reporting a capability as unused forever. The guard once read only the gates, and the two capabilities
    /// read the other way went missing from here (hexide-io/HexIDE#394).
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
        // Consumed, but gated quietly — see CanServeQuietly. It belongs here because a server that
        // advertises it IS taken up on it; the absence of a warning is about what its ABSENCE means.
        "diagnosticProvider",
        "documentFormattingProvider",
        "documentHighlightProvider",
        "documentSymbolProvider",
        // Read by the command-routing reader rather than a gate: a command is routed to the server that
        // named it, so the question is never "does any server offer this" but "which one declared it".
        "executeCommandProvider",
        "foldingRangeProvider",
        "semanticTokensProvider",
        "hoverProvider",
        "renameProvider",
        "signatureHelpProvider",
        // Read by the sync readers rather than a gate, because its two shapes mean different things — and
        // it gates every document notification this client sends, so reporting it unused is the most
        // misleading thing this list could get wrong.
        "textDocumentSync",
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

    /// <summary>
    /// True when the server advertised this capability, and <b>silent</b> when it did not.
    /// </summary>
    /// <remarks>
    /// <b>For the capabilities whose absence is an answer rather than a shortfall.</b> Every other gate warns,
    /// because a server without <c>hoverProvider</c> cannot do hover and somebody should be told. A server
    /// without <c>diagnosticProvider</c> is not missing anything: it publishes diagnostics instead, which is
    /// what four of the five servers this suite drives do and what the specification treats as ordinary.
    /// Routing that through <see cref="CanServe"/> would write a line into the log, and an entry into the
    /// protocol capture, blaming every conformant server for the way it conforms.
    ///
    /// <para>
    /// Still counted as consumed — see <see cref="ConsumedCapabilities"/> — because a server that advertises
    /// it is taken up on it. The two questions are "do we use this when offered" and "is not offering it a
    /// problem", and only the first belongs in that list.
    /// </para>
    /// </remarks>
    private bool CanServeQuietly(string capabilityName) =>
        ServerCapabilities.Supports(_capabilities?.Value, capabilityName);

    private bool CanServeExperimental(string capabilityName)
    {
        if (ServerCapabilities.SupportsExperimental(_capabilities?.Value, capabilityName)) return true;
        WarnUnavailableOnce("experimental." + capabilityName);
        return false;
    }

    /// <summary>Records an answer discarded by the registry's completion budget.</summary>
    public void NoteLateCompletion() =>
        Note(ConversationDirection.Local, ConversationEntryKind.Note, "textDocument/completion",
            "answered after the popup opened; completion discarded");

    /// <summary>Explains a routing decision once per capability per connection, on the skipped connection.</summary>
    public void NotePrioritySkip(string capabilityName, string winnerId)
    {
        if (!_prioritySkips.TryAdd(capabilityName, 0)) return;
        Note(ConversationDirection.Sent, ConversationEntryKind.NeverSent, null,
            $"not asked: '{winnerId}' has priority for '{capabilityName}'");
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
    /// a line per keypress and bury exactly what this exists to surface. Server replies ContentModified
    /// (-32801) and RequestCancelled (-32800) are superseded requests too, and keep the warning unspent.
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
        if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested
            || ex is RemoteInvocationException { ErrorCode: -32801 or -32800 })
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
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _semanticWarnings = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _prioritySkips = new();

    /// <summary>Methods already reported as failing on this connection. Cleared wherever
    /// <see cref="_warnedCapabilities"/> is, so a reconnect to a different server reports afresh.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _warnedFailures = new();
}
