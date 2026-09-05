using System.Text.Json;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;

namespace HexIDE.Lsp;

/// <summary>
/// Routes documents to every language server that claims their language, and combines the answers.
///
/// <para>
/// <b>This is itself an <see cref="ILspClient"/>, and that is the whole trick.</b> That interface already
/// takes a URI and returns results without saying which server replied — its own capability documentation
/// says the seam exists so a backend can be replaced "without touching the editor". Routing therefore fits
/// behind it exactly, and no editor or view-model code changes to gain plurality. Each connection remains
/// an ordinary single-server client, which keeps routing, per-server state and several transports out of a
/// class whose job is one connection.
/// </para>
///
/// <para>
/// <b>Every claimant sees the document; results merge.</b> Routing to a single winner is simpler and wrong:
/// the arrangement it forecloses — a language server beside a linter on the same file — is ordinary rather
/// than exotic. The costs are also asymmetric. A merging router can be configured down to one server; a
/// picking router cannot be widened without changing every caller.
/// </para>
///
/// <para>
/// The exceptions are formatting and rename, where two answers cannot both be applied. Those select one
/// server by declared priority, then registration order.
/// </para>
/// </summary>
public sealed class LspClientRegistry : ILspClient, ILanguageConnectionRegistry
{
    private readonly List<Entry> _entries;
    private readonly ILogger<LspClientRegistry> _logger;

    public LspClientRegistry(
        IEnumerable<LanguageServerRegistration> registrations, ILogger<LspClientRegistry> logger)
    {
        _logger = logger;
        // Ordered once. OrderByDescending is stable, so equal priorities keep registration order — which is
        // the documented fallback rather than an accident of how the sort happened to behave.
        _entries = registrations
            .OrderByDescending(r => r.Priority)
            .Select(r => new Entry(r))
            .ToList();
    }

    public event EventHandler<PublishDiagnosticsParams>? DiagnosticsPublished;
    public event EventHandler? ConnectionsChanged;

    /// <summary>True when any connection is up — "is language intelligence available at all".</summary>
    /// <remarks>
    /// Callers use this as a cheap gate before asking for a feature, so the useful meaning is "is anything
    /// listening", not "is everything listening". Per-connection truth is what <see cref="Connections"/>
    /// is for, and conflating the two would make a second, unrelated server's failure look like a total
    /// outage.
    /// </remarks>
    public bool IsRunning => _entries.Any(e => e.Client is { IsRunning: true });

    /// <summary>
    /// Meaningless across several servers, so it is deliberately null. A caller wanting to know what a
    /// particular server advertised should read <see cref="Connections"/>, where the answer is attributed.
    /// </summary>
    public JsonElement? AdvertisedCapabilities => null;

    public IReadOnlyList<LanguageServerConnection> Connections =>
        _entries.Select(e => new LanguageServerConnection(
            e.Registration.Id,
            e.Registration.DisplayName,
            LanguageConnectionKind.LanguageServer,
            e.State,
            e.Registration.Extensions,
            e.Registration.LanguageId,
            e.Client?.AdvertisedCapabilities)).ToList();

    /// <summary>
    /// Starts nothing. Servers start on the first document of a language they claim, so that a project
    /// containing no documents of some language never pays for its server — and a broken server for an
    /// unused language cannot degrade startup for someone who never opens that file type.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Language server registry ready with {Count} registration(s); each starts on first use.",
            _entries.Count);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        foreach (var e in _entries)
        {
            if (e.Client is not { } client) continue;
            client.DiagnosticsPublished -= OnInnerDiagnostics;
            try { await client.StopAsync(); } catch (Exception ex) { _logger.LogDebug(ex, "Stop failed for {Id}", e.Registration.Id); }
            e.Client = null;
            e.State = LanguageConnectionState.Stopped;
        }
        ConnectionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task OpenDocumentAsync(string uri, string text, CancellationToken cancellationToken = default)
    {
        // The one place a server starts. Opening a document is the first moment its language is known to be
        // present, which is exactly the trigger lazy start is defined against.
        var claimants = ClaimantsFor(uri);
        foreach (var e in claimants) await EnsureStartedAsync(e, cancellationToken);
        await Task.WhenAll(claimants
            .Where(e => e.Client is not null)
            .Select(e => e.Client!.OpenDocumentAsync(uri, text, cancellationToken)));
    }

    public Task ChangeDocumentAsync(string uri, int version, string text, CancellationToken cancellationToken = default) =>
        // No start here: a change to a document nothing has opened is not a reason to launch a server.
        Task.WhenAll(StartedClaimantsFor(uri).Select(c => c.ChangeDocumentAsync(uri, version, text, cancellationToken)));

    public Task CloseDocumentAsync(string uri, CancellationToken cancellationToken = default) =>
        Task.WhenAll(StartedClaimantsFor(uri).Select(c => c.CloseDocumentAsync(uri, cancellationToken)));

    // ── Merged features ─────────────────────────────────────────────────────────────────────────────
    public Task<DocumentSymbol[]> RequestDocumentSymbolsAsync(string uri, CancellationToken ct = default) =>
        GatherAsync(uri, c => c.RequestDocumentSymbolsAsync(uri, ct));

    public Task<FoldingRange[]> RequestFoldingRangesAsync(string uri, CancellationToken ct = default) =>
        GatherAsync(uri, c => c.RequestFoldingRangesAsync(uri, ct));

    public Task<CompletionItem[]> RequestCompletionAsync(string uri, Position position, CancellationToken ct = default) =>
        GatherAsync(uri, c => c.RequestCompletionAsync(uri, position, ct));

    // ── First-answer features ───────────────────────────────────────────────────────────────────────
    // Nothing here can usefully merge two answers into one, but any claimant may legitimately have it, so
    // the highest-priority server that actually answers wins rather than the highest-priority server alone.
    public Task<HoverResult?> RequestHoverAsync(string uri, Position position, CancellationToken ct = default) =>
        FirstAnswerAsync(uri, c => c.RequestHoverAsync(uri, position, ct));

    public Task<SignatureHelp?> RequestSignatureHelpAsync(string uri, Position position, CancellationToken ct = default) =>
        FirstAnswerAsync(uri, c => c.RequestSignatureHelpAsync(uri, position, ct));

    public Task<Location[]?> RequestDefinitionAsync(string uri, Position position, CancellationToken ct = default) =>
        FirstAnswerAsync(uri, c => c.RequestDefinitionAsync(uri, position, ct));

    public Task<DocumentHighlight[]?> RequestDocumentHighlightAsync(string uri, Position position, CancellationToken ct = default) =>
        FirstAnswerAsync(uri, c => c.RequestDocumentHighlightAsync(uri, position, ct));

    // ── Pick-one features ───────────────────────────────────────────────────────────────────────────
    // Two sets of edits to one document cannot both be applied, so these need a winner rather than a merge.
    // Only the top-priority claimant is asked; a second server's edits are not a fallback, they are a
    // different opinion about the same text.
    public Task<WorkspaceEdit?> RequestRenameAsync(string uri, Position position, string newName, CancellationToken ct = default) =>
        SoleClaimantFor(uri, "renameProvider") is { } c ? c.RequestRenameAsync(uri, position, newName, ct) : Task.FromResult<WorkspaceEdit?>(null);

    public Task<TextEdit[]> RequestFormattingAsync(string uri, CancellationToken ct = default) =>
        SoleClaimantFor(uri, "documentFormattingProvider") is { } c ? c.RequestFormattingAsync(uri, ct) : Task.FromResult<TextEdit[]>([]);

    /// <summary>
    /// Routed by advertised capability rather than by language, because it has no document to route by.
    /// The server that declares <c>experimental.vbBuiltinSymbols</c> is the one that can answer it.
    /// </summary>
    public async Task<VbaBuiltinSymbol[]> RequestBuiltinSymbolsAsync(CancellationToken ct = default)
    {
        foreach (var e in _entries)
        {
            if (e.Client is not { IsRunning: true } client) continue;
            if (client.AdvertisedCapabilities is not { } caps) continue;
            if (!caps.TryGetProperty("experimental", out var experimental)) continue;
            if (!experimental.TryGetProperty("vbBuiltinSymbols", out var flag) || !flag.ValueKind.Equals(JsonValueKind.True)) continue;
            return await client.RequestBuiltinSymbolsAsync(ct);
        }
        return [];
    }

    /// <summary>
    /// Raised on this registry directly, not routed. Injection is a client-side side channel used by an
    /// external compiler, and it works with no server connected at all — routing it to one would make it
    /// depend on something it deliberately does not need.
    /// </summary>
    public Task InjectDiagnosticsAsync(string uri, Diagnostic[] diagnostics)
    {
        DiagnosticsPublished?.Invoke(this, new PublishDiagnosticsParams(uri, diagnostics));
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        foreach (var e in _entries) e.Gate.Dispose();
    }

    // ── Routing ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every server that claims this document. Keyed on the EXTENSION, not on a language name, so two
    /// servers claiming one extension are both offered it even when they disagree about what to call it.
    /// </summary>
    private List<Entry> ClaimantsFor(string? uri)
    {
        // A scheme that names a language wins: HexIDE's own documents carry no extension to match on, and
        // a server claims them by declaring that language identifier.
        if (DocumentLanguage.SchemeLanguageOf(uri) is { } schemeLanguage)
            return _entries
                .Where(e => e.Registration.LanguageId.Equals(schemeLanguage, StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (DocumentLanguage.ExtensionOf(uri) is not { } extension) return [];

        return _entries
            .Where(e => e.Registration.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private IEnumerable<ILspClient> StartedClaimantsFor(string? uri) =>
        ClaimantsFor(uri).Select(e => e.Client).Where(c => c is not null).Select(c => c!);

    /// <summary>
    /// The one server chosen for a feature that cannot merge two answers. Selection is among claimants that
    /// actually ADVERTISE the feature, not simply the top-priority claimant: otherwise a higher-priority
    /// server with no formatter would silently block a lower one that has it. That is also how the
    /// established editor ecosystem behaves — its formatter conflict prompt lists only formatters.
    /// </summary>
    private ILspClient? SoleClaimantFor(string? uri, string capability) =>
        StartedClaimantsFor(uri)
            .FirstOrDefault(c => ServerCapabilities.Supports(c.AdvertisedCapabilities, capability));

    private async Task<T[]> GatherAsync<T>(string uri, Func<ILspClient, Task<T[]>> call)
    {
        var clients = StartedClaimantsFor(uri).ToList();
        if (clients.Count == 0) return [];
        if (clients.Count == 1) return await call(clients[0]);
        var results = await Task.WhenAll(clients.Select(call));
        return results.SelectMany(r => r).ToArray();
    }

    private async Task<T?> FirstAnswerAsync<T>(string uri, Func<ILspClient, Task<T?>> call) where T : class
    {
        foreach (var client in StartedClaimantsFor(uri))
        {
            if (await call(client) is { } answer) return answer;
        }
        return null;
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────────────────────────────

    private async Task EnsureStartedAsync(Entry e, CancellationToken cancellationToken)
    {
        // A server that failed stays failed for the session. Retrying on every document open would turn one
        // broken registration into a repeated startup cost paid by the user, on a path where nothing has
        // changed to make the next attempt more likely to work.
        if (e.State is LanguageConnectionState.Running or LanguageConnectionState.Failed) return;

        await e.Gate.WaitAsync(cancellationToken);
        try
        {
            if (e.Client is not null) return;

            e.State = LanguageConnectionState.Starting;
            ConnectionsChanged?.Invoke(this, EventArgs.Empty);

            var client = e.Registration.CreateClient();
            client.DiagnosticsPublished += OnInnerDiagnostics;
            e.Client = client;

            await client.StartAsync(cancellationToken);
            e.State = client.IsRunning ? LanguageConnectionState.Running : LanguageConnectionState.Failed;

            if (!client.IsRunning)
                _logger.LogWarning("Language server '{Id}' did not start; its languages have no support.",
                    e.Registration.Id);
        }
        catch (Exception ex)
        {
            // Graceful absence, as everywhere else on this seam: a server that will not start disables its
            // own languages, not the IDE.
            _logger.LogWarning(ex, "Language server '{Id}' failed to start.", e.Registration.Id);
            e.State = LanguageConnectionState.Failed;
        }
        finally
        {
            e.Gate.Release();
            ConnectionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnInnerDiagnostics(object? sender, PublishDiagnosticsParams p) =>
        // Forwarded with this registry as the sender: subscribers key on the URI, and which server produced
        // a diagnostic is not something the editor should have to reason about.
        DiagnosticsPublished?.Invoke(this, p);

    private sealed class Entry(LanguageServerRegistration registration)
    {
        public LanguageServerRegistration Registration { get; } = registration;
        public ILspClient? Client;
        public LanguageConnectionState State = LanguageConnectionState.NotStarted;
        public readonly SemaphoreSlim Gate = new(1, 1);
    }
}
