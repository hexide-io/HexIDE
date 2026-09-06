using System.Collections.Concurrent;
using System.Text.Json;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace HexIDE.Lsp;

public sealed class VBLspClient : ILspClient
{
    private readonly ILspTransport _transport;
    private readonly ILogger<VBLspClient> _logger;
    // Currently-open documents (uri -> latest version+text), so they can be replayed after a reconnect.
    private readonly ConcurrentDictionary<string, TrackedDocument> _openDocuments = new();
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
        ILspWorkspace? workspace = null, TimeSpan? initializeTimeout = null)
    {
        _transport = transport;
        _logger = logger;
        _languageId = languageId;
        _workspace = workspace;
        _initializeTimeout = initializeTimeout ?? DefaultInitializeTimeout;
    }

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
        var handler = await _transport.ConnectAsync(formatter, cancellationToken);
        if (handler is null)
        {
            // No server/endpoint available — run with LSP features disabled.
            return;
        }

        var rpc = new JsonRpc(handler, new LspNotificationReceiver(this));
        rpc.Disconnected += OnRpcDisconnected;
        _rpc = rpc;
        rpc.StartListening();

        await InitializeAsync(cancellationToken);

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
        catch (Exception ex) { _logger.LogDebug(ex, "textDocument/didOpen failed for {Uri}", uri); }
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

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_rpc is null) return;

        // Asked for now, not when this client was built: the server starts on first use, and the project
        // open at that moment is what it should be rooted at.
        var initParams = new InitializeParams(
            ProcessId: Environment.ProcessId,
            RootUri: WorkspaceRootUri(),
            Capabilities: new ClientCapabilities(
                new TextDocumentClientCapabilities(
                    PublishDiagnostics: new PublishDiagnosticsClientCapabilities(),
                    Hover: new HoverClientCapabilities(ContentFormat: ["plaintext"]),
                    // Claimed here so the server has something to answer. Declaring and gating are two
                    // halves of one negotiation: gate without declare and a conformant server withholds
                    // `save` because nothing asked for it, while we decline to send because it did not
                    // offer — both correct, nothing happening, and no error anywhere.
                    Synchronization: new TextDocumentSyncClientCapabilities(DidSave: true))));

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
            return;
        }
        catch (TimeoutException)
        {
            AbandonUnansweredHandshake();
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize VB6 LSP server");
            return;
        }

        _capabilities = ReadCapabilities(raw) is { } caps ? new CapabilitySnapshot(caps) : null;
        _initialized = true;
        _logger.LogInformation("VB6 LSP server initialized");
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
        catch (Exception ex) { _logger.LogDebug(ex, "textDocument/didChange failed"); }
    }

    public async Task CloseDocumentAsync(string uri, CancellationToken cancellationToken = default)
    {
        _openDocuments.TryRemove(uri, out _);
        var rpc = _rpc;
        if (rpc is null || !_initialized || !ServerCapabilities.AcceptsOpenClose(_capabilities?.Value)) return;
        var p = new DidCloseTextDocumentParams(new TextDocumentIdentifier(uri));
        try { await rpc.NotifyWithParameterObjectAsync("textDocument/didClose", p); }
        catch (Exception ex) { _logger.LogDebug(ex, "textDocument/didClose failed"); }
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
        catch (Exception ex) { _logger.LogDebug(ex, "textDocument/didSave failed"); }
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
            _logger.LogDebug(ex, "textDocument/hover request failed");
            return null;
        }
    }

    public async Task<DocumentSymbol[]> RequestDocumentSymbolsAsync(string uri, CancellationToken cancellationToken = default)
    {
        if (_rpc is null || !_initialized || !CanServe("documentSymbolProvider")) return [];
        var p = new DocumentSymbolParams(new TextDocumentIdentifier(uri));
        try
        {
            return await _rpc.InvokeWithParameterObjectAsync<DocumentSymbol[]>(
                "textDocument/documentSymbol", p, cancellationToken) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "textDocument/documentSymbol request failed");
            return [];
        }
    }

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
            _logger.LogDebug(ex, "textDocument/foldingRange request failed");
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
            _logger.LogDebug(ex, "textDocument/completion request failed");
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
            _logger.LogDebug(ex, "textDocument/signatureHelp request failed");
            return null;
        }
    }

    public async Task<Location[]?> RequestDefinitionAsync(string uri, Position position, CancellationToken cancellationToken = default)
    {
        if (_rpc is null || !_initialized || !CanServe("definitionProvider")) return null;
        var p = new TextDocumentPositionParams(new TextDocumentIdentifier(uri), position);
        try
        {
            return await _rpc.InvokeWithParameterObjectAsync<Location[]?>(
                "textDocument/definition", p, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "textDocument/definition request failed");
            return null;
        }
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
            _logger.LogDebug(ex, "textDocument/documentHighlight request failed");
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
            _logger.LogDebug(ex, "textDocument/rename request failed");
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
            _logger.LogDebug(ex, "textDocument/formatting request failed");
            return [];
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
            _logger.LogDebug(ex, "vb/builtinSymbols request failed");
            return [];
        }
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
            try { await rpc.InvokeAsync("shutdown"); } catch { }
            try { await rpc.NotifyWithParameterObjectAsync("exit", EmptyParams.Instance); } catch { }
        }
        DisposeRpc();

        _transport.Closed -= OnTransportClosed;
        await _transport.DisposeAsync();
        _initialized = false;
        _capabilities = null;
        _warnedCapabilities.Clear();
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private void OnTransportClosed(object? sender, EventArgs e)
    {
        _initialized = false;
        _capabilities = null;
        _warnedCapabilities.Clear();
    }

    internal void RaisePublishDiagnostics(PublishDiagnosticsParams p) =>
        DiagnosticsPublished?.Invoke(this, p);

    internal void RaiseMessageShown(ShowMessageParams p) => MessageShown?.Invoke(this, p);

    public Task InjectDiagnosticsAsync(string uri, Diagnostic[] diagnostics)
    {
        RaisePublishDiagnostics(new PublishDiagnosticsParams(uri, diagnostics));
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
            _client.RaisePublishDiagnostics(p);
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
        public void OnLogMessage(LogMessageParams p) =>
            _client._logger.Log(LevelOf(p.Type), "[{Language} server] {Message}", _client._languageId, p.Message);

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
        /// Maps the protocol's four levels onto the logger's.
        ///
        /// <para>
        /// <c>Log</c> becomes Debug rather than Information: it is the level a server uses for running
        /// commentary, and promoting it would bury the two levels that mean something. An unrecognised
        /// value becomes Information — a server saying something we cannot rank is still a server saying
        /// something, and dropping it would recreate the bug in miniature.
        /// </para>
        /// </summary>
        private static LogLevel LevelOf(LspMessageType type) => type switch
        {
            LspMessageType.Error => LogLevel.Error,
            LspMessageType.Warning => LogLevel.Warning,
            LspMessageType.Info => LogLevel.Information,
            LspMessageType.Log => LogLevel.Debug,
            _ => LogLevel.Information,
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
        _logger.LogWarning(
            "The connected language server did not advertise '{Capability}'; that feature is unavailable. "
          + "If it should be supported, check which server binary was resolved — a stale one advertises "
          + "little and disables features silently.",
            capabilityName);
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _warnedCapabilities = new();
}
