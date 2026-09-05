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
    public VBLspClient(
        ILspTransport transport, ILogger<VBLspClient> logger, string languageId, ILspWorkspace? workspace = null)
    {
        _transport = transport;
        _logger = logger;
        _languageId = languageId;
        _workspace = workspace;
    }

    private readonly string _languageId;
    private readonly ILspWorkspace? _workspace;

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

    /// <summary>After a (re)connect, replay textDocument/didOpen for every tracked document so the
    /// freshly-initialised server regains its document set and republishes diagnostics.</summary>
    private async Task ReopenTrackedDocumentsAsync()
    {
        var rpc = _rpc;
        if (rpc is null || !_initialized) return;
        foreach (var (uri, doc) in _openDocuments)
        {
            var p = new DidOpenTextDocumentParams(new TextDocumentItem(uri, "vb6", doc.Version, doc.Text));
            try { await rpc.NotifyWithParameterObjectAsync("textDocument/didOpen", p); }
            catch (Exception ex) { _logger.LogDebug(ex, "re-open didOpen failed for {Uri}", uri); }
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
                    Hover: new HoverClientCapabilities(ContentFormat: ["plaintext"]))));

        // Deliberately received as a raw JsonElement, and interpreted separately below.
        //
        // These are two different failures and they must not share a catch. "The server did not complete
        // the handshake" is fatal — there is nothing to talk to. "We could not model the reply it sent" is
        // not: the server answered, the connection is good, and the worst honest outcome is that we know
        // less than we might about what it supports. Sharing one catch is what made a single unexpected
        // capability shape disable every language feature including diagnostics (#238).
        JsonElement raw;
        try
        {
            raw = await _rpc.InvokeWithParameterObjectAsync<JsonElement>(
                "initialize", initParams, cancellationToken);
            await _rpc.NotifyWithParameterObjectAsync("initialized", EmptyParams.Instance);
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
        _openDocuments[uri] = new TrackedDocument(1, text);
        var rpc = _rpc;
        if (rpc is null || !_initialized || !ServerCapabilities.AcceptsOpenClose(_capabilities?.Value)) return;
        var p = new DidOpenTextDocumentParams(
            new TextDocumentItem(uri, _languageId, 1, text));
        try { await rpc.NotifyWithParameterObjectAsync("textDocument/didOpen", p); }
        catch (Exception ex) { _logger.LogDebug(ex, "textDocument/didOpen failed"); }
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
