using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HexIDE.Lsp.Messages;
using HexIDE.Conversations;
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
    // Replaced whole, never changed in place: an add-in's server arrives while documents are being routed (#2760), and a
    // reader holding the old list keeps a consistent one. Written only under _entriesGate.
    private volatile IReadOnlyList<Entry> _entries;
    private readonly object _entriesGate = new();
    private readonly ILogger<LspClientRegistry> _logger;
    private readonly HashSet<string> _unknownCommands = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly ILspWorkspace? _workspace;
    private readonly IWorkspaceArtifactService? _artifacts;
    private readonly ConversationLog? _capture;

    // Each connection is a source of its own, and so is anything injecting through this router. Without
    // this, two servers claiming one document overwrote each other and a build erased both (#358).
    private readonly DiagnosticLedger _diagnostics = new();

    /// <summary>
    /// What the registry remembers about each document a caller has opened, for as long as it is open.
    /// </summary>
    /// <remarks>
    /// <b>Ordinal, deliberately, and not <see cref="LspDocumentUri.Comparer"/>.</b> The per-connection
    /// tracker this shadows (<c>VBLspClient._openDocuments</c>) is an ordinary <c>ConcurrentDictionary</c>
    /// with the default comparer, and two stores keyed differently desynchronise on exactly the
    /// case-folding case a URI comparer exists for: one document reached under two spellings would be one
    /// entry here and two there, so a close would clear this and leave the connection tracking a document
    /// that a later reconnect then replays. They must agree, and the cheaper one to agree with is ordinal.
    /// </remarks>
    private readonly ConcurrentDictionary<string, OpenDocument> _open = new(StringComparer.Ordinal);

    /// <summary>What is known about a document that is open.</summary>
    /// <param name="IsProjectMember">
    /// Whether it belongs to a VB6 project. Stated by the caller rather than worked out here: see
    /// <see cref="OpenDocumentAsync(string, string, bool, CancellationToken)"/>.
    /// </param>
    /// <param name="Text">
    /// The latest text this registry has been given for it. Kept because a workspace move replaces every
    /// client, and a fresh client has no history to replay from — the per-connection tracker went with the
    /// connection. Updated on every change <b>before</b> the change is routed, so text that arrives while
    /// no client is up is still what the re-open sends. Routed-path-only bookkeeping would re-open the
    /// document with the text from before that change and silently undo it.
    /// </param>
    /// <param name="HasVb6ClassHeader">The content claim at open, retained through edits.</param>
    private sealed record OpenDocument(bool IsProjectMember, string Text, bool HasVb6ClassHeader);

    /// <summary>
    /// Serialises workspace-move handling. Without it two opens can both pass the
    /// <see cref="SameDirectory"/> check before either reaches <c>_rootedAt = current</c> — which is
    /// ordinary, since loading a <c>.vbg</c> opens several editors and nothing gates
    /// <c>LspDocumentSession.Start</c>. Before this change that raced to a duplicate <c>StopAsync</c> and
    /// was swallowed; now it would replay every open document twice onto the new connection.
    /// </summary>
    private readonly SemaphoreSlim _restartGate = new(1, 1);

    /// <summary>The workspace the running servers were told about, so a move can be noticed.</summary>
    private string? _rootedAt;

    public LspClientRegistry(
        IEnumerable<LanguageServerRegistration> registrations, ILogger<LspClientRegistry> logger,
        ILspWorkspace? workspace = null,
        IReadOnlyList<LanguageServerConfigProblem>? configurationProblems = null,
        IWorkspaceArtifactService? artifacts = null,
        TimeProvider? timeProvider = null,
        ConversationLog? capture = null)
    {
        _logger = logger;
        _capture = capture;
        _diagnosticLifecycle = new(logger);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _workspace = workspace;
        _artifacts = artifacts;
        if (artifacts is not null)
        {
            artifacts.RestartRequested += OnArtifactRestartRequested;
            artifacts.FileChanged += NotifyWatchedFile;
            artifacts.ConfigurationProblemsChanged += OnArtifactProblemsChanged;
        }
        _configurationProblems = configurationProblems ?? [];
        // Ordered once. OrderByDescending is stable, so equal priorities keep registration order — which is
        // the documented fallback rather than an accident of how the sort happened to behave.
        _entries = registrations
            .OrderByDescending(r => r.Priority)
            .Select(r => new Entry(r))
            .ToList();
    }

    public IReadOnlyList<OwnedDiagnostic> Diagnostics => _diagnostics.Snapshot();

    private readonly DiagnosticLifecycle _diagnosticLifecycle;

    public event EventHandler? CodeLensRefreshRequested;
    public event EventHandler<PublishDiagnosticsParams>? DiagnosticsPublished;
    public event EventHandler<string>? DocumentRetired;
    public event EventHandler<ShowMessageParams>? MessageShown;
    public event EventHandler<ProgressParams>? ProgressReceived;
    public event EventHandler<LogMessageParams>? MessageLogged;
    public event EventHandler<LogTraceParams>? TraceReceived;
    public event EventHandler? ConnectionsChanged;

    /// <summary>
    /// The <see cref="ILspClient"/> half of the same signal. A caller holding this object as a single
    /// client wants to know when "is anything listening" changed; a caller holding it as a registry wants
    /// to know when any row changed. Those are the same moment, so this forwards rather than duplicating.
    /// </summary>
    public event EventHandler? StateChanged;

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

    /// <summary>
    /// Null for the same reason as <see cref="AdvertisedCapabilities"/>: several servers may be attached
    /// and there is no honest single answer to "what did the server call itself". The attributed answer
    /// is on each <see cref="Connections"/> row.
    /// </summary>
    public ServerIdentity? ReportedIdentity => null;

    /// <summary>Null for the same reason as the two above: attributed per row, meaningless in aggregate.</summary>
    public LanguageConnectionAttempt? LastAttempt => null;

    /// <summary>Empty, not null. Aggregating refusals across servers would say a feature was declined
    /// when one server declined it and another served it.</summary>
    public IReadOnlyList<string> DeclinedCapabilities => [];

    private void OnArtifactProblemsChanged() => ConnectionsChanged?.Invoke(this, EventArgs.Empty);

    private readonly ConcurrentDictionary<string, LanguageServerConfigProblem> _importProblems = new();
    private readonly IReadOnlyList<LanguageServerConfigProblem> _configurationProblems;
    public IReadOnlyList<LanguageServerConfigProblem> ConfigurationProblems
    {
        get
        {
            LanguageServerConfigProblem[] contributed;
            lock (_contributionProblems) contributed = _contributionProblems.SelectMany(p => p.Problems).ToArray();
            LanguageServerRegistration[] registrations;
            lock (_entriesGate) registrations = _entries.Select(entry => entry.Registration).ToArray();
            return _configurationProblems.Concat(contributed).Concat(_importProblems.Values)
                .Concat(registrations.SelectMany(registration =>
                    _artifacts?.GetConfigurationProblems(registration) ?? [])).ToArray();
        }
    }

    // What was wrong with the servers add-ins contributed (#2760), by the id each was contributed under, in the order they
    // were found: reported beside the file's, since a contributed entry is checked as one of the file's is. Only the latest
    // attempt's are kept, so an add-in that tries again replaces what its last try reported rather than adding to it.
    private readonly List<(string Id, LanguageServerConfigProblem[] Problems)> _contributionProblems = [];

    /// <summary>
    /// Reports what was wrong with a server an add-in contributed, beside the configuration file's problems. It replaces
    /// what was reported for the same id before, and goes when that server is taken out.
    /// </summary>
    public void ReportContributionProblems(string id, IEnumerable<LanguageServerConfigProblem> problems)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(problems);
        var reported = problems.ToArray();
        bool changed;
        lock (_contributionProblems)
        {
            changed = _contributionProblems.RemoveAll(p => p.Id == id) > 0 | reported.Length > 0;
            if (reported.Length > 0) _contributionProblems.Add((id, reported));
        }

        if (changed) ConnectionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ForgetContributionProblems(string id)
    {
        lock (_contributionProblems) _contributionProblems.RemoveAll(p => p.Id == id);
        // Its project imports' too, keyed by the id and the project (EnsureStartedAsync).
        foreach (var key in _importProblems.Keys)
            if (key.StartsWith(id + "\0", StringComparison.Ordinal)) _importProblems.TryRemove(key, out _);
    }

    /// <summary>Whether a server with this id is known, configured or contributed.</summary>
    public bool Knows(string id) =>
        _entries.Any(e => string.Equals(e.Registration.Id, id, StringComparison.Ordinal));

    public IReadOnlyList<LanguageServerConnection> Connections =>
        _entries.Select(e => new LanguageServerConnection(
            e.Registration.Id,
            e.Registration.DisplayName,
            LanguageConnectionKind.LanguageServer,
            ProjectedState(e),
            e.Registration.Extensions,
            e.Registration.LanguageId,
            e.Client?.AdvertisedCapabilities,
            e.Registration.Transport,
            e.Registration.Endpoint,
            e.Registration.Priority,
            e.StateSince,
            e.Client?.ReportedIdentity,
            e.Client?.LastAttempt ?? e.LastAttempt,
            e.Client?.DeclinedCapabilities,
            (e.Client as VBLspClient)?.OpenDocumentCount ?? 0,
            (e.Client as VBLspClient)?.SentWorkspaceRootUri,
            e.Registration.Origin)).ToList();

    /// <summary>
    /// What a connection's state is <em>now</em>, rather than what it was when something last wrote it down.
    ///
    /// <para>
    /// <see cref="Entry.State"/> is written in five places and <b>none of them is a death path</b>: when a
    /// transport closes, the client clears its own flags and tells the registry nothing. So a server that
    /// crashed after starting kept reporting <see cref="LanguageConnectionState.Running"/> for the rest of
    /// the session — beside <c>Capabilities</c>, which is read live from the client and had already gone
    /// null. <c>Running</c> with nothing advertised is the most confusing pairing this record can produce,
    /// and it is what the projection used to emit.
    /// </para>
    ///
    /// <para>
    /// It corrects both directions, because the same staleness reads the other way too: a pipe or websocket
    /// server whose reconnect loop came back up is live again while the cached state still says
    /// <see cref="LanguageConnectionState.Failed"/>.
    /// </para>
    ///
    /// <para>
    /// <b>This deliberately does not touch <see cref="Entry.State"/>.</b> <c>EnsureStartedAsync</c> gates on
    /// Failed being terminal for the session, and that is an argued decision rather than an accident — a
    /// projection fix must not repeal it by a side effect. What is <em>reported</em> becomes honest; what is
    /// <em>retried</em> is unchanged, and a crashed server still is not restarted.
    /// </para>
    /// </summary>
    private static LanguageConnectionState ProjectedState(Entry e) =>
        e.Client is { IsRunning: true } ? LanguageConnectionState.Running
        : e.State == LanguageConnectionState.Running ? LanguageConnectionState.Stopped
        : e.State;

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
            client.CodeLensRefreshRequested -= OnInnerCodeLensRefresh;
            client.MessageShown -= OnInnerMessage;
            client.MessageLogged -= OnInnerLogMessage;
            client.TraceReceived -= OnInnerTrace;
            client.StateChanged -= OnInnerStateChanged;
            try { await client.StopAsync(); } catch (Exception ex) { _logger.LogDebug(ex, "Stop failed for {Id}", e.Registration.Id); }
            client.ProgressReceived -= OnInnerProgress;
            e.Client = null;
            e.State = LanguageConnectionState.Stopped;
            e.StateSince = DateTimeOffset.UtcNow;
        }
        ConnectionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task OpenDocumentAsync(string uri, string text, CancellationToken cancellationToken = default) =>
        OpenDocumentAsync(uri, text, isProjectMember: false, cancellationToken);

    public async Task OpenDocumentAsync(
        string uri, string text, bool isProjectMember, CancellationToken cancellationToken = default)
    {
        _diagnosticLifecycle.Open(uri);
        // Recorded before routing, so change, close and save answer the same question this open did. The
        // registry deliberately holds no model of the project, and it could not work membership out for
        // itself: an `untitled:` name would have to be parsed back into project and document names, which
        // this change forbids everywhere else, and a path match cannot see a document that has no file.
        // The opener already knows -- the code window opens members, the carried-file editor does not.
        //
        // And recorded before the restart below, not after it, so that a change made while the restart stops
        // the old servers has a record to land in. No connection can hold it then, and the open at the end
        // sends what this records (#719).
        _open[uri] = new OpenDocument(isProjectMember, text,
            !isProjectMember && DocumentLanguage.ExtensionOf(uri) == ".cls" && HasVb6ClassHeader(text));

        // Also before the first await: from here until each claimant has been opened on the document, a request
        // about it waits rather than reaching a server that has not been told it is open (#2909). A code window
        // asks as soon as it is shown, and this method has a server's start, its workspace folders and its
        // project import to get through first.
        var claimants = ClaimantsFor(uri, isProjectMember);
        foreach (var e in claimants) HoldOpen(e, uri);
        try
        {
            // Before anything starts or is used: a server told about one project must not go on serving
            // another. This document is named so the replay inside can skip it — it is opened below either
            // way, and opening it twice is the failure this argument exists to prevent.
            await RestartIfWorkspaceMovedAsync(uri, cancellationToken);

            // The one place a server starts. Opening a document is the first moment its language is known to be
            // present, which is exactly the trigger lazy start is defined against.
            //
            // Each claimant is opened as soon as it has started, not once all of them have. A request now waits
            // for its server's open, so waiting for every start would make the first server's answers wait for
            // the slowest server's start as well (#2909).
            foreach (var e in claimants)
            {
                await EnsureStartedAsync(e, cancellationToken);

                // The text as recorded now rather than as it was when this began. Starting a server takes long
                // enough for the developer to type, and a change made meanwhile is newer than `text`: sending
                // `text` would undo it on every server that had not yet recorded it for itself (#719).
                var current = _open.TryGetValue(uri, out var known) ? known.Text : text;
                if (e.Client is { } client) await client.OpenDocumentAsync(uri, current, cancellationToken);
                ReleaseOpen(e, uri);
            }
        }
        finally
        {
            // A start that threw or was cancelled opens nothing more, so nothing may go on waiting for it.
            foreach (var e in claimants) ReleaseOpen(e, uri);
        }
        // Startup was announced before the document was tracked. Refresh the card's count too (#2485).
        ConnectionsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Makes a request about <paramref name="uri"/> to <paramref name="e"/>'s server wait until
    /// <see cref="ReleaseOpen"/>, which follows the document's open there (hexide-io/HexIDE#2909).
    /// </summary>
    private static void HoldOpen(Entry e, string uri) =>
        e.PendingOpens.GetOrAdd(uri, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

    private static void ReleaseOpen(Entry e, string uri)
    {
        if (e.PendingOpens.TryRemove(uri, out var held)) held.TrySetResult();
    }

    /// <summary>Releases every request waiting on an open of <paramref name="uri"/>, on any server.</summary>
    private void ReleaseOpens(string uri)
    {
        foreach (var e in _entries) ReleaseOpen(e, uri);
    }

    public Task ChangeDocumentAsync(string uri, int version, string text, CancellationToken cancellationToken = default)
    {
        // Recorded BEFORE routing, and deliberately even when routing reaches nobody. Between a workspace
        // move tearing the clients down and the next one starting, StartedClaimantsFor yields nothing and
        // this change goes nowhere — so if the record were only updated on the routed path, the re-open
        // would send the text from before it and quietly undo the developer's typing. Same reasoning as
        // VBLspClient tracking a document before its own gate.
        if (_open.TryGetValue(uri, out var known)) _open[uri] = known with { Text = text };

        // No start here: a change to a document nothing has opened is not a reason to launch a server.
        return Task.WhenAll(StartedClaimantsFor(uri).Select(c => c.ChangeDocumentAsync(uri, version, text, cancellationToken)));
    }

    /// <summary>
    /// Withdraws every diagnostic recorded under a name the document is about to stop answering to, and
    /// raises the empty set so consumers clear it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For a RENAME, and called by the close half of one. An ordinary close must not do this: a build's
    /// claim about a form outlives the editor that was showing it, and the document still exists under
    /// that name.
    /// </para>
    /// <para>
    /// Necessary because an empty publication from a language server does not clear the document. The
    /// ledger removes that server's row and raises the union of the rest, so a form carrying both a server
    /// diagnostic and an injected compiler one still publishes a non-empty set — and under a name nothing
    /// answers to any more, those survivors are marks nothing will ever clear. That is #269's shape
    /// arriving by a different route.
    /// </para>
    /// </remarks>
    public void ForgetDiagnosticsFor(string uri)
    {
        if (_diagnostics.Forget(uri) is { } cleared)
            DiagnosticsPublished?.Invoke(this, cleared);
    }

    public async Task CloseDocumentForRenameAsync(string uri, CancellationToken cancellationToken = default)
    {
        _diagnosticLifecycle.Retire(uri);
        // The connections first, so each drops its own state and a push server stops republishing under the
        // old name; then the ledger, which is what actually clears a mark no single connection owns.
        await Task.WhenAll(StartedClaimantsFor(uri)
            .Select(c => c.CloseDocumentForRenameAsync(uri, cancellationToken)));
        _open.TryRemove(uri, out _);
        ReleaseOpens(uri);
        _diagnosticLifecycle.Rename(uri, () =>
        {
            ForgetDiagnosticsFor(uri);
            DocumentRetired?.Invoke(this, uri);
        });
        ConnectionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task CloseDocumentAsync(string uri, CancellationToken cancellationToken = default)
    {
        // Routed BEFORE the record is dropped, or the close would be offered to a different set of servers
        // than the open was -- the exact asymmetry that leaves a server holding a document forever.
        var closing = Task.WhenAll(StartedClaimantsFor(uri).Select(c => c.CloseDocumentAsync(uri, cancellationToken)));
        _open.TryRemove(uri, out _);
        // A request still waiting for this document's open has nothing left to wait for.
        ReleaseOpens(uri);
        await closing;
        ConnectionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void NotifyWatchedFile(string path, int type)
    {
        foreach (var entry in _entries)
            if (entry.Client is { IsRunning: true } client) client.NotifyWatchedFile(path, type);
    }

    public Task SaveDocumentAsync(string uri, CancellationToken cancellationToken = default) =>
        // No start here either, for the same reason as a change: saving a document nothing has opened is
        // not a reason to launch a server. Each claimant decides for itself whether it was asked for
        // saves, so of two servers holding one document only the one that asked hears about it — which is
        // why the gate belongs on the connection and not here.
        Task.WhenAll(StartedClaimantsFor(uri).Select(c => c.SaveDocumentAsync(uri, cancellationToken)));

    public Task DidCreateFilesAsync(string[] uris, CancellationToken cancellationToken = default) =>
        RouteFileOperationsAsync(uris, "didCreate", uri => uri,
            (client, files) => client.DidCreateFilesAsync(files, cancellationToken));

    public Task DidDeleteFilesAsync(string[] uris, CancellationToken cancellationToken = default) =>
        RouteFileOperationsAsync(uris, "didDelete", uri => uri,
            (client, files) => client.DidDeleteFilesAsync(files, cancellationToken));

    public Task DidRenameFilesAsync(FileRename[] files, CancellationToken cancellationToken = default) =>
        RouteFileOperationsAsync(files, "didRename", file => file.OldUri,
            (client, matching) => client.DidRenameFilesAsync(matching, cancellationToken), file => file.NewUri);

    private Task RouteFileOperationsAsync<T>(T[] files, string operation, Func<T, string> uri,
        Func<ILspClient, T[], Task> send, Func<T, string>? newUri = null) =>
        // Generated workspace artefacts already describe the project; keep a single writer (#2248).
        // Other workspace events follow operation filters, not language claims, and never launch a server.
        Task.WhenAll(_entries.Where(e => (e.Registration.WorkspaceArtifact is null || e.Registration.ProjectImport is not null)
            && e.Client is { IsRunning: true }).Select(e =>
        {
            var client = e.Client!;
            var matching = files.Where(file =>
                FileOperationFilters.Matches(client.AdvertisedCapabilities, operation, uri(file))
                || newUri is not null && FileOperationFilters.Matches(client.AdvertisedCapabilities, operation, newUri(file)))
                .ToArray();
            return matching.Length == 0 ? Task.CompletedTask : send(client, matching);
        }));

    // ── One-answer features ─────────────────────────────────────────────────────────────────────────
    // A document has ONE structure and ONE set of folds, however many servers are looking at it. Two
    // servers answering describe the same thing twice, so the answers do not merge — they duplicate.
    // Asking only the top-priority claimant is what makes a second server attached to `.bas` an addition
    // rather than a corruption of what was already working.
    public async Task<DocumentSymbol[]> RequestDocumentSymbolsAsync(string uri, CancellationToken ct = default) =>
        await TopClaimantAdvertisingAsync(uri, "documentSymbolProvider", ct) is { } c
            ? await c.RequestDocumentSymbolsAsync(uri, ct)
            : [];

    public bool SupportsSelectionRanges(string uri) =>
        StartedClaimantsFor(uri).Any(c => c.SupportsSelectionRanges(uri));

    public async Task<SelectionRange[]> RequestSelectionRangesAsync(string uri, Position[] positions, CancellationToken ct = default) =>
        await TopClaimantAdvertisingAsync(uri, "selectionRangeProvider", ct) is { } c
            ? await c.RequestSelectionRangesAsync(uri, positions, ct)
            : [];

    // Folds have a second, sharper reason. AvaloniaEdit's FoldingManager takes a single ordered set of
    // sections; two servers' folds partially overlap in general, and a partially-overlapping section is
    // not merely a duplicate — it can take out folding for the whole document behind the debug-level
    // catch that wraps the update.
    public async Task<FoldingRange[]> RequestFoldingRangesAsync(string uri, CancellationToken ct = default) =>
        await TopClaimantAdvertisingAsync(uri, "foldingRangeProvider", ct) is { } c
            ? await c.RequestFoldingRangesAsync(uri, ct)
            : [];

    public async Task<SemanticTokenResult?> RequestSemanticTokensAsync(string uri, CancellationToken ct = default) =>
        ClaimantsFor(uri, IsProjectMember(uri))
                .FirstOrDefault(e => e.Client is { } c && ServerCapabilities.SupportsSemanticTokensFull(c.AdvertisedCapabilities)) is { } entry
            && await OpenedOnAsync(entry, uri, ct) is { } client
            ? await client.RequestSemanticTokensAsync(uri, ct)
            : null;

    // ── Merged features ─────────────────────────────────────────────────────────────────────────────
    // Completions genuinely combine: two servers offer different candidates for one prefix, and dropping
    // either would hide something the user could have typed. This is the case the merge helpers exist for.
    public IReadOnlyList<string> CompletionTriggerCharacters(string uri) =>
        StartedClaimantsFor(uri).SelectMany(CompletionTriggers).Distinct(StringComparer.Ordinal).ToArray();

    public IReadOnlyList<string> SignatureHelpTriggerCharacters(string uri) =>
        StartedClaimantsFor(uri).SelectMany(client => ProviderTriggers(client, "signatureHelpProvider"))
            .Distinct(StringComparer.Ordinal).ToArray();

    private static IEnumerable<string> CompletionTriggers(ILspClient client)
        => ProviderTriggers(client, "completionProvider");

    private static IEnumerable<string> ProviderTriggers(ILspClient client, string capability)
    {
        if (client.AdvertisedCapabilities is not { } caps
            || !ServerCapabilities.Supports(caps, capability)) yield break;
        var provider = caps.GetProperty(capability);
        if (provider.ValueKind != JsonValueKind.Object
            || !provider.TryGetProperty("triggerCharacters", out var triggers)
            || triggers.ValueKind != JsonValueKind.Array) yield break;
        foreach (var trigger in triggers.EnumerateArray())
            if (trigger.ValueKind == JsonValueKind.String && trigger.GetString() is { } value)
                yield return value;
    }

    public Task<CompletionItem[]> RequestCompletionForTriggerAsync(string uri, Position position,
        string triggerCharacter, CancellationToken ct = default) =>
        GatherCompletionAsync(uri, position, triggerCharacter, ct);

    public Task<CompletionItem[]> RequestCompletionAsync(string uri, Position position, CancellationToken ct = default) =>
        GatherCompletionAsync(uri, position, null, ct);

    private async Task<CompletionItem[]> GatherCompletionAsync(
        string uri, Position position, string? triggerCharacter, CancellationToken ct)
    {
        var budget = TimeSpan.FromSeconds(1);
        var started = _timeProvider.GetTimestamp();
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var deadline = Task.Delay(budget, _timeProvider, waiting.Token);
        var pending = ClaimantsFor(uri, IsProjectMember(uri))
            .Where(e => e.Client is { } c && (triggerCharacter is null
                || CompletionTriggers(c).Contains(triggerCharacter, StringComparer.Ordinal)))
            .Select(AskAsync).ToArray();
        try
        {
            await Task.WhenAny(Task.WhenAll(pending), deadline);
            ct.ThrowIfCancellationRequested();
            // Registration order is already priority order. Never let response order choose the winner.
            return pending.Where(t => t.IsCompletedSuccessfully).SelectMany(t => t.Result)
                .DistinctBy(i => (i.Label, i.Kind)).ToArray();
        }
        finally { waiting.Cancel(); }

        async Task<CompletionItem[]> AskAsync(Entry e)
        {
            // A server whose open of this document is still to come is waited for within the popup's budget, as
            // its answer is, so one server's import never holds up another's candidates (#2909).
            if (await OpenedOnAsync(e, uri, waiting.Token) is not { } client) return [];
            try
            {
                // Keep the request's own deadline and cancellation; the popup budget only stops waiting.
                var timely = false;
                var items = await client.RequestCompletionAsync(uri, position, ct, () =>
                {
                    timely = _timeProvider.GetElapsedTime(started) <= budget;
                    if (timely || ct.IsCancellationRequested) return;
                    _logger.LogDebug("Completion from {Id} answered after the popup opened; discarded.", OwnerOf(client));
                    client.NoteLateCompletion();
                });
                if (timely) return items;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Completion failed for {Id}", OwnerOf(client));
            }
            return [];
        }
    }

    // ── First-answer features ───────────────────────────────────────────────────────────────────────
    // Nothing here can usefully merge two answers into one, but any claimant may legitimately have it, so
    // the highest-priority server that actually answers wins rather than the highest-priority server alone.
    public Task<HoverResult?> RequestHoverAsync(string uri, Position position, CancellationToken ct = default) =>
        FirstAnswerAsync(uri, c => c.RequestHoverAsync(uri, position, ct), ct);

    public async Task<SignatureHelp?> RequestSignatureHelpAsync(
        string uri, Position position, CancellationToken ct = default, string? triggerCharacter = null)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(1));

        // Start every eligible request before waiting. WhenAll preserves priority order, not reply order.
        var requests = ClaimantsFor(uri, IsProjectMember(uri))
            .Where(e => e.Client is { } c && (triggerCharacter is null || AdvertisesSignatureTrigger(c, triggerCharacter)))
            .Select(AskAsync).ToArray();
        var answers = await Task.WhenAll(requests);
        ct.ThrowIfCancellationRequested();
        return answers.FirstOrDefault(a => a is { Signatures.Length: > 0 });

        async Task<SignatureHelp?> AskAsync(Entry e)
        {
            // Within the same budget as the answer, for the reason completion gives (#2909).
            if (await OpenedOnAsync(e, uri, budget.Token) is not { } client) return null;
            try
            {
                // WaitAsync also bounds a client that ignores cancellation.
                return await client.RequestSignatureHelpAsync(uri, position, budget.Token)
                    .WaitAsync(budget.Token);
            }
            catch (OperationCanceledException) when (budget.IsCancellationRequested) { return null; }
        }
    }

    private static bool AdvertisesSignatureTrigger(ILspClient client, string triggerCharacter) =>
        client.AdvertisedCapabilities is { ValueKind: JsonValueKind.Object } caps &&
        caps.TryGetProperty("signatureHelpProvider", out var provider) &&
        provider.ValueKind == JsonValueKind.Object &&
        provider.TryGetProperty("triggerCharacters", out var triggers) &&
        triggers.ValueKind == JsonValueKind.Array &&
        triggers.EnumerateArray().Any(t => t.ValueKind == JsonValueKind.String && t.GetString() == triggerCharacter);

    public async Task<Location[]> RequestReferencesAsync(string uri, Position position, CancellationToken ct = default) =>
        (await FindReferencesAsync(uri, position, ct)).Locations;

    public async Task<ReferenceSearchResult> FindReferencesAsync(string uri, Position position, CancellationToken ct = default)
    {
        var answers = await GatherAsync(uri, async client =>
        {
            try
            {
                return new[] { new ReferenceSearchResult(await client.RequestReferencesAsync(uri, position, ct), []) };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                return new[] { new ReferenceSearchResult([], [new ReferenceSearchError(OwnerOf(client), ex.Message)]) };
            }
        }, ct);
        return new ReferenceSearchResult(answers.SelectMany(a => a.Locations).Distinct().ToArray(),
            answers.SelectMany(a => a.Errors).ToArray());
    }

    public bool SupportsCallHierarchy(string uri) => ClaimantsFor(uri, IsProjectMember(uri))
        .Any(e => e.Client is { IsRunning: true } c
            && ServerCapabilities.Supports(c.AdvertisedCapabilities, "callHierarchyProvider"));

    public async Task<CallHierarchyResult> FindCallHierarchyAsync(string uri, Position position, CancellationToken ct = default)
    {
        var answers = await GatherAsync(uri, async client =>
        {
            if (!ServerCapabilities.Supports(client.AdvertisedCapabilities, "callHierarchyProvider")) return Array.Empty<CallHierarchyResult>();
            try
            {
                var items = await client.PrepareCallHierarchyAsync(uri, position, ct);
                return new[] { new CallHierarchyResult(items.Select(i => new CallHierarchyRoot(client, i, OwnerOf(client))).ToArray(), []) };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                return new[] { new CallHierarchyResult([], [new ReferenceSearchError(OwnerOf(client), ex.Message)]) };
            }
        }, ct);
        return new(answers.SelectMany(a => a.Roots).ToArray(), answers.SelectMany(a => a.Errors).ToArray());
    }

    public Task<Location[]?> RequestDefinitionAsync(string uri, Position position, CancellationToken ct = default) =>
        FirstAnswerAsync(uri, c => c.RequestDefinitionAsync(uri, position, ct), ct);

    public bool SupportsCapability(string uri, string capability) =>
        ClaimantsFor(uri, IsProjectMember(uri)).Any(e =>
            e.Client is { IsRunning: true } client && client.SupportsCapability(uri, capability));

    public Task<Location[]?> RequestImplementationAsync(string uri, Position position, CancellationToken ct = default) =>
        FirstAnswerAsync(uri, c => c.RequestImplementationAsync(uri, position, ct), ct);

    public Task<Location[]?> RequestTypeDefinitionAsync(string uri, Position position, CancellationToken ct = default) =>
        FirstAnswerAsync(uri, c => c.RequestTypeDefinitionAsync(uri, position, ct), ct);

    public Task<Location[]?> RequestDeclarationAsync(string uri, Position position, CancellationToken ct = default) =>
        FirstAnswerAsync(uri, c => c.RequestDeclarationAsync(uri, position, ct), ct);

    public Task<DocumentHighlight[]?> RequestDocumentHighlightAsync(string uri, Position position, CancellationToken ct = default) =>
        FirstAnswerAsync(uri, c => c.RequestDocumentHighlightAsync(uri, position, ct), ct);

    public Task<OfferedCodeAction[]> RequestCodeActionsAsync(string uri, HexIDE.Lsp.Messages.Range range, Diagnostic[] diagnostics,
        CancellationToken ct = default) => GatherAsync(uri, async client =>
    {
        if (!ServerCapabilities.Supports(client.AdvertisedCapabilities, "codeActionProvider")) return [];
        var owner = OwnerOf(client);
        var ownDiagnostics = _diagnostics.Snapshot()
            .Where(d => d.Owner == owner && LspDocumentUri.AreSame(d.Uri, uri)
                && diagnostics.Any(candidate => ReferenceEquals(candidate, d.Diagnostic)))
            .Select(d => d.Diagnostic).ToArray();
        if (ownDiagnostics.Length == 0) return [];
        var actions = await client.RequestCodeActionsAsync(uri, ownDiagnostics[0].Range, ownDiagnostics, ct);
        var name = _entries.First(e => ReferenceEquals(e.Client, client)).Registration.DisplayName;
        return actions.Select(action => action with { Server = owner, ServerDisplayName = name }).ToArray();
    }, ct);

    // ── Pick-one features ───────────────────────────────────────────────────────────────────────────
    public async Task<PrepareRenameResult?> RequestPrepareRenameAsync(string uri, Position position, CancellationToken ct = default)
    {
        PrepareRenameResult? first = null;
        PrepareRenameResult? refusal = null;
        foreach (var client in await OpenedClaimantsAsync(uri, ct))
        {
            if (client.AdvertisedCapabilities is not { } caps
                || !caps.TryGetProperty("renameProvider", out var rename) || rename.ValueKind != JsonValueKind.Object
                || !rename.TryGetProperty("prepareProvider", out var prepare) || prepare.ValueKind != JsonValueKind.True)
                continue;
            var answer = await client.RequestPrepareRenameAsync(uri, position, ct);
            if (answer is null) continue;
            first ??= answer;
            if (!answer.Allowed && (refusal is null || refusal.Refusal is null && answer.Refusal is not null))
                refusal = answer;
        }
        return refusal ?? first;
    }

    // Two sets of edits to one document cannot both be applied, so these need a winner rather than a merge.
    // Only the top-priority claimant is asked; a second server's edits are not a fallback, they are a
    // different opinion about the same text.
    public async Task<RenameResult?> RequestRenameAsync(string uri, Position position, string newName, CancellationToken ct = default) =>
        await TopClaimantAdvertisingAsync(uri, "renameProvider", ct) is { } c ? await c.RequestRenameAsync(uri, position, newName, ct) : null;

    public bool SupportsOnTypeFormatting(string uri, string trigger) => ClaimantsFor(uri, IsProjectMember(uri))
        .Any(e => e.Client is { IsRunning: true } c
            && ServerCapabilities.SupportsOnTypeFormatting(c.AdvertisedCapabilities, trigger));

    public async Task<TextEdit[]> RequestOnTypeFormattingAsync(string uri, Position position, string trigger, FormattingOptions options, CancellationToken ct = default)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(VBLspClient.OnTypeFormattingTimeout);
        var advertisers = ClaimantsFor(uri, IsProjectMember(uri))
            .Where(e => e.Client is { IsRunning: true } c
                && ServerCapabilities.SupportsOnTypeFormatting(c.AdvertisedCapabilities, trigger)).ToList();
        if (advertisers.Count == 0) return [];
        var winner = advertisers[0];
        foreach (var skipped in advertisers.Skip(1))
            skipped.Client!.NotePrioritySkip("documentOnTypeFormattingProvider", OwnerOf(winner.Client!));
        return await OpenedOnAsync(winner, uri, budget.Token) is { } client
            ? await client.RequestOnTypeFormattingAsync(uri, position, trigger, options, budget.Token) : [];
    }

    public bool SupportsRangeFormatting(string uri) => ClaimantsFor(uri, IsProjectMember(uri))
        .Any(e => e.Client is { IsRunning: true } c
            && ServerCapabilities.Supports(c.AdvertisedCapabilities, "documentRangeFormattingProvider"));

    public async Task<TextEdit[]> RequestRangeFormattingAsync(string uri, HexIDE.Lsp.Messages.Range range, FormattingOptions options, CancellationToken ct = default) =>
        await TopClaimantAdvertisingAsync(uri, "documentRangeFormattingProvider", ct) is { } c ? await c.RequestRangeFormattingAsync(uri, range, options, ct) : [];

    public async Task<TextEdit[]> RequestFormattingAsync(string uri, FormattingOptions options, CancellationToken ct = default) =>
        await TopClaimantAdvertisingAsync(uri, "documentFormattingProvider", ct) is { } c ? await c.RequestFormattingAsync(uri, options, ct) : [];

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
    /// Every capable server's matches for <paramref name="query"/>, concatenated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Routed by capability, because there is no document to route by.</b> That makes it the second
    /// request of this shape after <c>vb/builtinSymbols</c> — and unlike that one it asks EVERY capable
    /// server rather than the first, because they do not answer the same question. A workspace holding VB6
    /// and LaTeX has two servers that each know a disjoint part of it; taking the first answer would drop a
    /// whole language from the results and look like the symbols simply were not there.
    /// </para>
    ///
    /// <para>
    /// <b>This is the opposite of the rule for document symbols, and deliberately so.</b> The note on
    /// lenses below says gathering would be wrong "for symbols": that is about
    /// <c>textDocument/documentSymbol</c>, where two claimants describe the SAME file and concatenating
    /// duplicates every entry. Here each server covers a different part of the workspace, so concatenating
    /// unions rather than duplicates. The distinction is document scope, not the word "symbol".
    /// </para>
    ///
    /// <para>
    /// <b>Only servers that are already running are asked, and this must not change.</b> A server starts
    /// when a document of its language is opened, because that is the first moment the language is known to
    /// be present; a search box establishes no such thing, and waking every registration on a keystroke
    /// would launch a LaTeX process because someone typed in a VB6 project. The cost is real and lands on
    /// the caller: with nothing open there are no results, and a surface showing that must say "nothing is
    /// running" rather than "no matches".
    /// </para>
    ///
    /// <para>
    /// Two servers that genuinely overlap — both claiming <c>.bas</c>, both indexing it — would produce a
    /// duplicate entry. That is accepted: a duplicate is visible and harmless, where the alternative loses
    /// entire languages silently, and this client has no way to tell two servers' identical answers apart
    /// from one server's two genuine hits at the same name.
    /// </para>
    /// </remarks>
    public async Task<SymbolInformation[]> RequestWorkspaceSymbolsAsync(
        string query, CancellationToken ct = default)
    {
        var all = new List<SymbolInformation>();
        foreach (var e in _entries)
        {
            if (e.Client is not { IsRunning: true } client) continue;
            if (!Declares(client, "workspaceSymbolProvider")) continue;

            all.AddRange(await client.RequestWorkspaceSymbolsAsync(query, ct));
        }
        return [.. all];
    }

    /// <summary>
    /// Asks every running server to change how much it says about itself.
    /// </summary>
    /// <remarks>
    /// <b>Every one, because this interface has no way to name a single one.</b> Hiding which server
    /// answered is the whole point of the router, and the cost lands here: a per-server level is a real
    /// requirement and it cannot be expressed through this method. It belongs on the registry interface,
    /// which does know one connection from another, and is added there when something needs it.
    ///
    /// <para>
    /// A server that has not started is skipped rather than started. Waking a process to tell it how
    /// talkative to be would be an odd reason to spend one, and the level it will start at comes from
    /// configuration anyway.
    /// </para>
    /// </remarks>
    public async Task SetTraceAsync(string value, CancellationToken ct = default)
    {
        foreach (var e in _entries)
        {
            if (e.Client is not { IsRunning: true } client) continue;
            await client.SetTraceAsync(value, ct);
        }
    }

    /// <summary>
    /// Whether a running client's advertised capabilities carry <paramref name="capability"/> as anything
    /// other than an explicit refusal.
    /// </summary>
    /// <remarks>
    /// Most capabilities are <c>boolean | XxxOptions</c> in the protocol, so a conformant server may answer
    /// either — modelling one narrowly is what caused a total silent blackout once already (#238). Present
    /// and not <c>false</c> is therefore the test.
    /// </remarks>
    private static bool Declares(ILspClient client, string capability) =>
        client.AdvertisedCapabilities is { } caps
        && caps.TryGetProperty(capability, out var value)
        && value.ValueKind != JsonValueKind.False
        && value.ValueKind != JsonValueKind.Null
        && value.ValueKind != JsonValueKind.Undefined;

    /// <summary>
    /// Every claimant's lenses, concatenated.
    /// </summary>
    /// <remarks>
    /// <b>Gathering is right here, where it would be wrong for symbols.</b> Two servers listing the same
    /// procedure produce one duplicated entry in a dropdown; two servers offering a lens on the same line
    /// produce two genuinely different offers — "Run test" from one and "Show references" from another —
    /// and a client that dropped either would be hiding a thing the user could have done.
    /// </remarks>
    public Task<InlayHint[]> RequestInlayHintsAsync(string uri, HexIDE.Lsp.Messages.Range range, CancellationToken ct = default) =>
        GatherAsync(uri, c => c.RequestInlayHintsAsync(uri, range, ct), ct);

    // Identity matters: two servers can return equal ranges and data with different meanings.
    // Weak keys let discarded document/viewport results release their connection provenance.
    private readonly ConditionalWeakTable<CodeLens, ILspClient> _codeLensOwners = new();

    public Task<CodeLens[]> RequestCodeLensesAsync(string uri, CancellationToken ct = default) =>
        GatherAsync(uri, async client =>
        {
            var lenses = await client.RequestCodeLensesAsync(uri, ct);
            foreach (var lens in lenses)
                _codeLensOwners.GetValue(lens, _ => client);
            return lenses;
        }, ct);

    public async Task<CodeLens?> ResolveCodeLensAsync(CodeLens lens, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested || !_codeLensOwners.TryGetValue(lens, out var client)
            || !client.IsRunning || !_entries.Any(e => ReferenceEquals(e.Client, client))) return null;
        var resolved = await client.ResolveCodeLensAsync(lens, ct);
        if (resolved is not null) _codeLensOwners.GetValue(resolved, _ => client);
        return resolved;
    }

    /// <summary>
    /// Sends a command to the server that declared it.
    /// </summary>
    /// <remarks>
    /// <b>The one request here with no document to route by, and no capability flag either.</b> A command
    /// belongs to whichever server named it in <c>executeCommandProvider.commands</c>, so that list is the
    /// routing key — see <see cref="ServerCapabilities.DeclaresCommand"/>. Every other helper on this class
    /// asks "who claims this file"; a command has no file, and asking the wrong server would not fail
    /// quietly, it would run something.
    ///
    /// <para>
    /// A command nobody declares goes nowhere and answers null. That is deliberate: the alternative is
    /// picking a server, and the whole point of routing by declaration is that no such pick is safe.
    /// </para>
    /// </remarks>
    public async Task<JsonElement?> ExecuteCommandAsync(
        string command, JsonElement[]? arguments = null, CancellationToken ct = default)
    {
        foreach (var e in _entries)
        {
            if (e.Client is not { IsRunning: true } client) continue;
            if (!ServerCapabilities.DeclaresCommand(client.AdvertisedCapabilities, command)) continue;
            return await client.ExecuteCommandAsync(command, arguments, ct);
        }

        lock (_unknownCommands)
            if (_unknownCommands.Add(command))
                _logger.LogDebug(
                    "No started language server declares the command {Command}; it was not sent.", command);
        return null;
    }

    /// <summary>
    /// Raised on this registry directly, not routed. Injection is a client-side side channel used by an
    /// external compiler, and it works with no server connected at all — routing it to one would make it
    /// depend on something it deliberately does not need.
    /// </summary>
    public Task InjectDiagnosticsAsync(string uri, Diagnostic[] diagnostics, string owner)
    {
        var published = _diagnostics.Record(uri, owner, diagnostics);
        DiagnosticsPublished?.Invoke(this, published);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Expires one source's marks everywhere it left them. Raised on this registry directly, for the same
    /// reason injection is: the source that needs this is an external compiler, and it must not depend on
    /// a server being connected to take back what it published.
    /// </summary>
    public Task ClearDiagnosticsFromAsync(string owner)
    {
        foreach (var p in _diagnostics.Withdraw(owner))
            DiagnosticsPublished?.Invoke(this, p);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_artifacts is not null)
        {
            _artifacts.RestartRequested -= OnArtifactRestartRequested;
            _artifacts.FileChanged -= NotifyWatchedFile;
            _artifacts.ConfigurationProblemsChanged -= OnArtifactProblemsChanged;
        }
        if (_artifacts is IDisposable disposable) disposable.Dispose();
        foreach (var e in _entries) await e.Lifetime.CancelAsync();
        await StopAsync();
        // Lifetime is cancelled, not disposed: a replay still finishing reads its token, and it holds no timer to release.
        foreach (var e in _entries)
        {
            foreach (var uri in e.PendingOpens.Keys) ReleaseOpen(e, uri);
            e.Gate.Dispose();
        }
    }

    // ── Routing ─────────────────────────────────────────────────────────────────────────────────────

    private static bool HasVb6ClassHeader(string text)
    {
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
                return line.Equals("VERSION 1.0 CLASS", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    /// <summary>
    /// Every server that claims this document. Keyed on the EXTENSION, not on a language name, so two
    /// servers claiming one extension are both offered it even when they disagree about what to call it.
    ///
    /// <para>
    /// There used to be a branch above this one for HexIDE's own <c>vb6://</c> scheme, which carried no
    /// extension. #273 retired the scheme, and the branch became unreachable the moment nothing minted one
    /// — taking the <c>.cls</c> gate that lived inside it with it. The gate is now
    /// <paramref name="isProjectMember"/>, below, and is reached on every document rather than on one
    /// spelling of one.
    /// </para>
    /// </summary>
    private List<Entry> ClaimantsFor(string? uri, bool isProjectMember)
    {
        if (DocumentLanguage.ExtensionOf(uri) is not { } extension) return [];

        return _entries
            .Where(e => e.Registration.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            // A carried class keeps its opening content claim through edits and server restarts (#279).
            .Where(e => isProjectMember || extension != ".cls"
                        || !DocumentLanguage.EstablishesVb6(e.Registration.Extensions, e.Registration.LanguageId)
                        || uri is not null && _open.TryGetValue(uri, out var record) && record.HasVb6ClassHeader)
            .Where(e => !isProjectMember
                        || DocumentLanguage.EstablishesVb6(e.Registration.Extensions, e.Registration.LanguageId))
            .ToList();
    }

    /// <summary>
    /// Whether this document is a member of a VB6 project, as the caller that opened it stated.
    /// Defaults to <c>false</c> for a document nothing has opened, which is the safe answer: an ungated
    /// route offers strictly fewer servers than a gated one would wrongly exclude.
    /// </summary>
    private bool IsProjectMember(string? uri) =>
        uri is not null && _open.TryGetValue(uri, out var record) && record.IsProjectMember;

    private IEnumerable<ILspClient> StartedClaimantsFor(string? uri) =>
        ClaimantsFor(uri, IsProjectMember(uri)).Select(e => e.Client).Where(c => c is not null).Select(c => c!);

    /// <summary>
    /// The servers to ask about <paramref name="uri"/>, in priority order, each only once this registry has
    /// opened the document on it (hexide-io/HexIDE#2909).
    /// </summary>
    /// <remarks>
    /// <para>
    /// LSP has a client open a document before asking about it (3.17, Text Document Synchronization). The code
    /// window asks for folds and semantic tokens as soon as it is shown, and again after any server's state
    /// changes, so without this a request reached a running server during its project import, or while a second
    /// server claiming the same document started, before the document's <c>didOpen</c>.
    /// </para>
    /// <para>
    /// A server that is running and has an open of this document still to come is waited for. One still starting
    /// is left out, as it was before this existed: it has not answered <c>initialize</c>, so it has nothing to say
    /// yet, and asking it now could race the end of its handshake.
    /// </para>
    /// </remarks>
    private async Task<List<ILspClient>> OpenedClaimantsAsync(string uri, CancellationToken ct)
    {
        var opened = new List<ILspClient>();
        foreach (var e in ClaimantsFor(uri, IsProjectMember(uri)))
            if (await OpenedOnAsync(e, uri, ct) is { } client) opened.Add(client);
        return opened;
    }

    /// <summary>
    /// <paramref name="e"/>'s server once <paramref name="uri"/> is open on it, or null if it is still starting
    /// or has no client. See <see cref="OpenedClaimantsAsync"/>.
    /// </summary>
    /// <remarks>
    /// Null too when <paramref name="ct"/> is cancelled during the wait, so the request answers as a cancelled
    /// request to a server already does: each client catches its own cancellation and returns its empty answer.
    /// </remarks>
    private static async Task<ILspClient?> OpenedOnAsync(Entry e, string uri, CancellationToken ct)
    {
        if (e.PendingOpens.TryGetValue(uri, out var held))
        {
            if (e.Client is not { IsRunning: true }) return null;
            try { await held.Task.WaitAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return null; }
        }
        return e.Client;
    }

    /// <summary>
    /// The one server chosen for a feature that cannot merge two answers. Selection is among claimants that
    /// actually ADVERTISE the feature, not simply the top-priority claimant: otherwise a higher-priority
    /// server with no formatter would silently block a lower one that has it. That is also how the
    /// established editor ecosystem behaves — its formatter conflict prompt lists only formatters.
    /// </summary>
    private async Task<ILspClient?> TopClaimantAdvertisingAsync(string uri, string capability, CancellationToken ct)
    {
        // Chosen by what each server advertised, which a server still starting has not done, so the winner is
        // known before any open is waited for, and only the winner's is (#2909).
        var advertisers = ClaimantsFor(uri, IsProjectMember(uri))
            .Where(e => e.Client is { } c && ServerCapabilities.Supports(c.AdvertisedCapabilities, capability)).ToList();
        if (advertisers.Count == 0) return null;

        var winner = advertisers[0];
        foreach (var skipped in advertisers.Skip(1))
            skipped.Client!.NotePrioritySkip(capability, OwnerOf(winner.Client!));
        return await OpenedOnAsync(winner, uri, ct);
    }

    private async Task<T[]> GatherAsync<T>(string uri, Func<ILspClient, Task<T[]>> call, CancellationToken ct)
    {
        var clients = await OpenedClaimantsAsync(uri, ct);
        if (clients.Count == 0) return [];
        if (clients.Count == 1) return await call(clients[0]);
        var results = await Task.WhenAll(clients.Select(call));
        return results.SelectMany(r => r).ToArray();
    }

    private async Task<T?> FirstAnswerAsync<T>(string uri, Func<ILspClient, Task<T?>> call, CancellationToken ct) where T : class
    {
        // Each server waited for in turn, so a lower-priority server's open never holds up a higher one's answer.
        foreach (var e in ClaimantsFor(uri, IsProjectMember(uri)))
        {
            if (await OpenedOnAsync(e, uri, ct) is { } client && await call(client) is { } answer) return answer;
        }
        return null;
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tears down running servers when the workspace has moved, so the next document starts them afresh at
    /// the new root.
    ///
    /// <para>
    /// <b>A root is sent once, at initialize, and never revised.</b> So closing one project and opening
    /// another leaves every running server rooted at the project that is gone — reading its configuration
    /// from the wrong workspace and reporting results with nothing to indicate why. That has nothing to do
    /// with saving a project for the first time: switching projects is ordinary and permanent.
    /// </para>
    ///
    /// <para>
    /// Checked on project saves and startup-project changes as well as document opens.
    /// </para>
    ///
    /// <para>
    /// A server that FAILED stays failed. The workspace changing is not a reason to believe a command that
    /// would not launch will launch now, and retrying on every project switch turns one broken entry into a
    /// cost paid repeatedly.
    /// </para>
    /// </summary>
    public Task WorkspaceChangedAsync(CancellationToken cancellationToken = default) =>
        RestartIfWorkspaceMovedAsync(null, cancellationToken);

    private async Task RestartIfWorkspaceMovedAsync(string? triggeringUri, CancellationToken cancellationToken)
    {
        if (_workspace is null) return;

        await _restartGate.WaitAsync(cancellationToken);
        try
        {
            await RestartIfWorkspaceMovedCoreAsync(triggeringUri, cancellationToken);
        }
        finally
        {
            _restartGate.Release();
        }
    }

    private async Task RestartIfWorkspaceMovedCoreAsync(string? triggeringUri, CancellationToken cancellationToken)
    {
        var current = _workspace!.Directory;

        // A waiting entry has no client yet, but a first save supplies the root it needs. Reopen the
        // known documents even when there is nothing to stop (#1382).
        if (SameDirectory(current, _rootedAt)) return;

        _logger.LogInformation(
            "The workspace moved; restarting language servers so none keeps serving a project that is gone.");

        // Every open document is about to be opened again, on servers that start afresh, so a request about one
        // waits for that as it does for a first open (#2909). The triggering document's opener holds its own.
        foreach (var (uri, known) in _open)
        {
            if (triggeringUri is not null && string.Equals(uri, triggeringUri, StringComparison.Ordinal)) continue;
            foreach (var e in ClaimantsFor(uri, known.IsProjectMember)) HoldOpen(e, uri);
        }

        foreach (var e in _entries)
        {
            if (e.Client is null) continue;

            // A failed entry keeps its client and its state. It is not restarted, so it must not be reset to
            // NotStarted — that would make it eligible to start again, which is the retry this is avoiding.
            if (e.State == LanguageConnectionState.Failed) continue;

            await StopForRestartAsync(e);
        }

        _rootedAt = current;

        // Re-open everything that was open, before anything else is forwarded. The teardown above covers
        // EVERY entry, while the caller goes on to start and open only the claimants of the one document
        // that triggered it — so without this a Markdown server holding a carried file is stopped with
        // nothing to re-open it, and every later change to those documents reaches a server that never saw
        // them opened (hexide-io/HexIDE#469). A first save of a never-saved project moves the root, so
        // since #489 this is the ordinary path rather than an edge case.
        //
        // Under the gate, which every open passes through: a server with an add-in's gate before launch is
        // reopened off it (#2760), since that gate waits as long as the add-in takes, and holding every open of
        // every language for it would stall the editor.
        await ReopenKnownDocumentsAsync(triggeringUri, cancellationToken);

        ConnectionsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Stops a connection so that it can run again: NotStarted rather than Stopped, so the next document it claims, or
    /// the re-open that follows, starts it afresh.
    /// </summary>
    private async Task StopForRestartAsync(Entry e)
    {
        if (e.Client is not { } client) return;
        client.DiagnosticsPublished -= OnInnerDiagnostics;
        client.CodeLensRefreshRequested -= OnInnerCodeLensRefresh;
        client.MessageShown -= OnInnerMessage;
        client.MessageLogged -= OnInnerLogMessage;
        client.TraceReceived -= OnInnerTrace;
        client.StateChanged -= OnInnerStateChanged;
        try { await client.StopAsync(); }
        catch (Exception ex) { _logger.LogDebug(ex, "Stop failed for {Id}", e.Registration.Id); }
        client.ProgressReceived -= OnInnerProgress;
        e.Client = null;
        e.State = LanguageConnectionState.NotStarted;
        e.StateSince = DateTimeOffset.UtcNow;
    }

    /// <summary>Restarts only the entry the user selected, including one that stopped or failed.</summary>
    public async Task RestartAsync(string id, CancellationToken cancellationToken = default)
    {
        await _restartGate.WaitAsync(cancellationToken);
        try
        {
            if (_entries.FirstOrDefault(e => e.Registration.Id == id) is not { } entry) return;
            try
            {
                _capture?.Record(id, ConversationDirection.Local, ConversationEntryKind.Lifecycle,
                    null, null, 0, detail: "restart requested by user");
            }
            catch (Exception) { /* a capture is never worth breaking a connection for */ }
            await RestartEntryAsync(entry, cancellationToken, startWithoutDocuments: true);
        }
        finally { _restartGate.Release(); }
    }

    private async Task RestartEntryAsync(Entry entry, CancellationToken cancellationToken, bool startWithoutDocuments = false)
    {
        foreach (var (uri, known) in _open)
            if (ClaimantsFor(uri, known.IsProjectMember).Contains(entry)) HoldOpen(entry, uri);
        await StopForRestartAsync(entry);
        entry.State = LanguageConnectionState.NotStarted;
        entry.StateSince = DateTimeOffset.UtcNow;
        try
        {
            if (startWithoutDocuments) await EnsureStartedAsync(entry, cancellationToken);
            await ReopenKnownDocumentsAsync(null, cancellationToken, only: entry);
        }
        finally
        {
            if (startWithoutDocuments)
                foreach (var uri in _open.Keys) ReleaseOpen(entry, uri);
            ConnectionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnArtifactRestartRequested(LanguageServerRegistration server) => _ = RestartForArtifactAsync(server);

    /// <summary>
    /// Restarts the one server that opted into restarts for workspace artefacts or project membership changes,
    /// then re-opens the documents it held.
    /// </summary>
    /// <remarks>
    /// Only that server: the others neither read its file nor should lose their state. One that isn't running is left
    /// alone, since it reads the new file when it next starts, and a failed one stays failed. Serialised with a workspace
    /// move, which restarts everything.
    /// </remarks>
    private async Task RestartForArtifactAsync(LanguageServerRegistration server)
    {
        var serverId = server.Id;
        try
        {
            await _restartGate.WaitAsync();
            try
            {
                // By registration: one added again under the id since is another server, whose file did not change.
                if (_entries.FirstOrDefault(x => ReferenceEquals(x.Registration, server)) is not
                    { Client: not null, State: LanguageConnectionState.Running } e) return;
                _logger.LogInformation("Restarting {Server}: a workspace update requested a restart.", serverId);
                await RestartEntryAsync(e, CancellationToken.None, startWithoutDocuments: server.RestartOnProjectChange);
            }
            finally { _restartGate.Release(); }
        }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { _logger.LogWarning(ex, "Restarting {Server} for its workspace artefact failed.", serverId); }
    }

    /// <summary>
    /// Opens every document the registry knows to be open on the connections that now claim it.
    /// </summary>
    /// <remarks>
    /// Driven from the record rather than from the triggering document's claimants: those are two
    /// different sets, and the teardown used the wider one.
    /// <para>
    /// <paramref name="triggeringUri"/> is skipped because its caller opens it immediately afterwards. It is
    /// already in the record, which its caller writes first, so without the skip it would be sent twice.
    /// </para>
    /// <para>
    /// <paramref name="only"/> limits it to one connection, restarted alone: the others still hold their documents, and a
    /// second didOpen for a URI is not something a server has to tolerate.
    /// </para>
    /// <para>
    /// A server with an add-in's gate before launch is reopened on its own, as a late registration's is, and this does
    /// not wait for it: the gate takes as long as the add-in takes, and the open that moved the workspace is of some
    /// other document. Its documents stay held until that reopen reaches them. The rest are reopened before this returns.
    /// </para>
    /// </remarks>
    private async Task ReopenKnownDocumentsAsync(string? triggeringUri, CancellationToken cancellationToken,
        Entry? only = null)
    {
        var gated = new HashSet<Entry>();
        var reached = false;
        try
        {
            foreach (var (uri, known) in _open)
            {
                if (triggeringUri is not null && string.Equals(uri, triggeringUri, StringComparison.Ordinal))
                    continue;

                foreach (var e in ClaimantsFor(uri, known.IsProjectMember))
                {
                    if (only is not null && e != only) continue;
                    if (e.Registration.BeforeLaunch is not null)
                    {
                        gated.Add(e);
                        continue;
                    }

                    // A failed entry is not revived by a document arriving, which is the rule EnsureStartedAsync
                    // already applies; the null check is what keeps that true here rather than restating it.
                    await EnsureStartedAsync(e, cancellationToken);

                    // Read again after the start: the text may have changed, or the document closed.
                    if (e.Client is { } client && _open.TryGetValue(uri, out var current))
                        await client.OpenDocumentAsync(uri, current.Text, cancellationToken);
                    ReleaseOpen(e, uri);
                }
            }
            reached = true;
        }
        finally
        {
            // What the restart held and this did not reach, because a start threw or was cancelled. A gated server's
            // are released by its own reopen, which begins only when this got to the end: otherwise nothing will reopen
            // them, and theirs go too.
            foreach (var uri in _open.Keys)
            {
                if (triggeringUri is not null && string.Equals(uri, triggeringUri, StringComparison.Ordinal)) continue;
                foreach (var e in _entries)
                    if ((only is null || e == only) && !(reached && gated.Contains(e))) ReleaseOpen(e, uri);
            }
        }

        foreach (var e in gated) _ = OpenKnownDocumentsOnAsync(e, triggeringUri);
    }

    /// <summary>
    /// Whether two workspace directories are the same place. Case-folded on Windows only — the filesystem
    /// decides this, and assuming either answer everywhere either restarts servers that did not move or
    /// fails to restart ones that did.
    /// </summary>
    private static bool SameDirectory(string? a, string? b)
    {
        if (a is null || b is null) return a is null && b is null;
        return string.Equals(
            a.TrimEnd('/', '\\'), b.TrimEnd('/', '\\'),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private async Task EnsureStartedAsync(Entry e, CancellationToken cancellationToken)
    {
        // A server that failed stays failed for the session. Retrying on every document open would turn one
        // broken registration into a repeated startup cost paid by the user, on a path where nothing has
        // changed to make the next attempt more likely to work.
        if (e.State is LanguageConnectionState.Running or LanguageConnectionState.Failed) return;

        await e.Gate.WaitAsync(cancellationToken);
        // The caller's token, and the server's own lifetime: taking the server out ends a start it is waiting for.
        using var start = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, e.Lifetime.Token);
        var starting = start.Token;
        try
        {
            if (e.Client is not null || e.Removed) return;

            e.State = LanguageConnectionState.Starting;
            e.StateSince = DateTimeOffset.UtcNow;
            ConnectionsChanged?.Invoke(this, EventArgs.Empty);

            e.LastAttempt = null;
            if (_artifacts is not null && e.Registration.ProjectImport is null)
            {
                try { await _artifacts.PrepareAsync(e.Registration, starting); }
                catch (OperationCanceledException) when (starting.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Workspace artefact for {Server} failed; launching anyway.", e.Registration.Id);
                }
            }

            // An add-in's gate (#2760), unlike the artefact above, decides whether the server starts: one that throws
            // fails the start, below, as a launch that fails does, and its message is the attempt's.
            if (e.Registration.BeforeLaunch is { } beforeLaunch)
                await beforeLaunch(starting);

            var client = e.Registration.CreateClient();

            // Heard only while it is listed. A removal takes the server out of the list and stops hearing its client under
            // the same lock, so a client made after that, by a start the removal has not yet called off, is let go unheard.
            bool listed;
            lock (_entriesGate)
            {
                listed = _entries.Contains(e);
                if (listed)
                {
                    client.DiagnosticsPublished += OnInnerDiagnostics;
                    client.CodeLensRefreshRequested += OnInnerCodeLensRefresh;
                    client.MessageShown += OnInnerMessage;
                    client.ProgressReceived += OnInnerProgress;
                    client.MessageLogged += OnInnerLogMessage;
                    client.TraceReceived += OnInnerTrace;
                    client.StateChanged += OnInnerStateChanged;
                    e.Client = client;
                }
            }
            if (!listed)
            {
                await client.DisposeAsync();
                return;
            }

            await client.StartAsync(starting);
            // A client stopped during its handshake returns rather than throws; the start was called off all the same.
            starting.ThrowIfCancellationRequested();
            if (!client.IsRunning && client.WaitingForWorkspace)
            {
                e.LastAttempt = client.LastAttempt;
                client.DiagnosticsPublished -= OnInnerDiagnostics;
                client.CodeLensRefreshRequested -= OnInnerCodeLensRefresh;
                client.MessageShown -= OnInnerMessage;
                client.MessageLogged -= OnInnerLogMessage;
                client.TraceReceived -= OnInnerTrace;
                client.StateChanged -= OnInnerStateChanged;
                await client.DisposeAsync();
                e.Client = null;
                e.State = LanguageConnectionState.NotStarted;
                e.StateSince = DateTimeOffset.UtcNow;
                if (!e.WarnedWaitingForWorkspace)
                {
                    _logger.LogWarning("Language server '{Id}' is waiting for a workspace: {Reason}",
                        e.Registration.Id, e.LastAttempt?.Steps.LastOrDefault()?.Detail);
                    e.WarnedWaitingForWorkspace = true;
                }
                return;
            }

            e.State = client.IsRunning ? LanguageConnectionState.Running : LanguageConnectionState.Failed;
            e.StateSince = DateTimeOffset.UtcNow;
            if (client.IsRunning && _artifacts is not null)
                await _artifacts.TrackFoldersAsync(e.Registration, token =>
                    ReferenceEquals(e.Client, client) && client.IsRunning
                        ? client.UpdateWorkspaceFoldersAsync(token) : Task.CompletedTask, cancellationToken);
            if (client.IsRunning && _artifacts is not null && e.Registration.ProjectImport is { } spec)
                await _artifacts.ImportAsync(e.Registration, async (project, token) =>
                {
                    // A stopped/replaced connection must not be revived by the debounce callback.
                    if (!ReferenceEquals(e.Client, client) || !client.IsRunning) return;
                    var problemKey = e.Registration.Id + "\0" + (project.Project.DefinitionPath ?? project.WorkspaceRoot);
                    try
                    {
                        if (project.Project.DefinitionPath is not { } path)
                            throw new InvalidOperationException("Project import requires a saved project path and in-memory project text.");
                        // A saved project HexIDE has no text of its own for, such as an add-in's project type's, whose
                        // file is in its add-in's format, has nothing to import, and isn't a problem.
                        if (project.ProjectText is { } render)
                            await client.ImportProjectAsync(spec, new Uri(path).AbsoluteUri, await render(token), token);
                        _importProblems.TryRemove(problemKey, out _);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        // Only for a server still listed, checked under the lock a removal withdraws its problems under: an
                        // import that ends after its server was taken out reports to nobody.
                        lock (_entriesGate)
                        {
                            if (_entries.Contains(e))
                                _importProblems[problemKey] = new LanguageServerConfigProblem(e.Registration.Id,
                                    $"Project import failed for {project.Project.Name}: {ex.Message}", false)
                                    .WithDetails("ImportFailed", project.Project.Name, ex.Message);
                        }
                        _logger.LogWarning(ex, "Project import failed for {Server}.", e.Registration.Id);
                    }
                    ConnectionsChanged?.Invoke(this, EventArgs.Empty);
                }, cancellationToken);

            if (!client.IsRunning)
                _logger.LogWarning("Language server '{Id}' did not start; its languages have no support.",
                    e.Registration.Id);
        }
        catch (OperationCanceledException) when (starting.IsCancellationRequested)
        {
            // Called off, not failed: an open that was given up, or the server being taken out. Failed is never retried,
            // so a cancelled open would otherwise have cost the server its languages for the session; NotStarted lets the
            // next document it claims start it again. A removal writes its own state after this.
            if (e.Client is { } abandoned)
            {
                Unsubscribe(abandoned);
                try { await abandoned.StopAsync(); }
                catch (Exception ex) { _logger.LogDebug(ex, "Stop failed for {Id}", e.Registration.Id); }
                e.Client = null;
            }
            e.State = LanguageConnectionState.NotStarted;
            e.StateSince = DateTimeOffset.UtcNow;

            // The caller's cancellation is the caller's to see. A removal is not: the open that was starting this server
            // goes on to its other servers, as it would had this one never been there.
            if (cancellationToken.IsCancellationRequested) throw;
        }
        catch (Exception ex)
        {
            // Graceful absence, as everywhere else on this seam: a server that will not start disables its
            // own languages, not the IDE.
            _logger.LogWarning(ex, "Language server '{Id}' failed to start.", e.Registration.Id);
            // A throw here can precede the client existing at all, so the registry has to hold the
            // reason itself; otherwise the row says Failed and nothing else.
            e.LastAttempt ??= new LanguageConnectionAttempt(
                LanguageConnectionStage.Connecting,
                [new LanguageConnectionStep(
                    LanguageConnectionStage.Connecting,
                    LanguageConnectionStepOutcome.StoppedHere, null, ex.Message)],
                DateTimeOffset.UtcNow);
            e.State = LanguageConnectionState.Failed;
            e.StateSince = DateTimeOffset.UtcNow;
        }
        finally
        {
            e.Gate.Release();
            ConnectionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Forwarded as-is. Which server spoke is already in the message the client logged; a subscriber's job
    /// is to put it in front of someone, not to work out who it came from.
    /// </summary>
    private void OnInnerLogMessage(object? sender, LogMessageParams e) => MessageLogged?.Invoke(this, e);
    private void OnInnerTrace(object? sender, LogTraceParams e) => TraceReceived?.Invoke(this, e);

    private void OnInnerProgress(object? sender, ProgressParams p) => ProgressReceived?.Invoke(sender,
        p with { ServerDisplayName = _entries.FirstOrDefault(e => ReferenceEquals(e.Client, sender))?.Registration.DisplayName });

    private void OnInnerMessage(object? sender, ShowMessageParams p) => MessageShown?.Invoke(this,
        p.ServerDisplayName is null ? p : p with
        {
            ServerDisplayName = _entries.FirstOrDefault(e => ReferenceEquals(e.Client, sender))?.Registration.DisplayName
                ?? p.ServerDisplayName,
        });

    private void OnInnerCodeLensRefresh(object? sender, EventArgs e) =>
        CodeLensRefreshRequested?.Invoke(sender, e);

    /// <summary>
    /// A connection came up or went away on its own — a transport closing, or a reconnect loop getting
    /// through. Nothing in the registry writes a state on those paths, which is exactly why the projection
    /// reconciles rather than trusting the cache; this is the other half, so a view refreshes when it
    /// happens instead of when something else happens to ask.
    /// </summary>
    private void OnInnerStateChanged(object? sender, EventArgs e)
    {
        ConnectionsChanged?.Invoke(this, EventArgs.Empty);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Merged and forwarded with this registry as the sender: subscribers key on the URI, and which server
    /// produced a diagnostic is not something the editor should have to reason about.
    ///
    /// <para>
    /// <b>Which server it came from still has to be remembered here, and forwarding verbatim is where that
    /// was lost.</b> Rendering does not care; <em>expiry</em> does, and they arrive on the same event. Two
    /// servers claiming one document each replaced the other's marks, and an injected clear from a build
    /// replaced both. So the sender is recorded as the owner of what it published, and what goes out is the
    /// document's whole set — which is the shape subscribers were already written for.
    /// </para>
    /// </summary>
    private void OnInnerDiagnostics(object? sender, PublishDiagnosticsParams p)
    {
        // Whose it is, decided as it arrives: by the time it is delivered its server may have been taken out, and its id
        // given to another. A server the registry no longer lists says nothing more, then or now (#2760).
        if (_entries.FirstOrDefault(e => ReferenceEquals(e.Client, sender)) is not { } from)
        {
            _logger.LogDebug("Diagnostics arrived from a connection this registry no longer lists.");
            return;
        }

        _diagnosticLifecycle.Publish(p.Uri, () =>
        {
            // In the order deliveries run, a removal's withdrawal among them: listed here, this comes before it.
            if (!_entries.Contains(from)) return;
            var published = _diagnostics.Record(p.Uri, from.Registration.Id, p.Diagnostics);
            DiagnosticsPublished?.Invoke(this, published);
        });
    }

    /// <summary>
    /// The owner key for a connection: its registration id, which is unique across the registry and is
    /// what every log line about that connection already names.
    /// </summary>
    private string OwnerOf(object? client)
    {
        foreach (var e in _entries)
        {
            if (ReferenceEquals(e.Client, client)) return e.Registration.Id;
        }

        // Unreachable by construction — only clients this registry created are subscribed to. Falling back
        // to a shared key rather than throwing keeps a diagnostic reaching the editor either way; the cost
        // of the impossible case is that two such publishers would clear each other, which is still better
        // than an exception on a notification path.
        _logger.LogDebug("Diagnostics arrived from a connection this registry does not own.");
        return DiagnosticOwner.LanguageServer;
    }

    internal sealed class Entry(LanguageServerRegistration registration)
    {
        public LanguageServerRegistration Registration { get; } = registration;
        public ILspClient? Client;
        public bool WarnedWaitingForWorkspace;
        public LanguageConnectionState State = LanguageConnectionState.NotStarted;

        /// <summary>When <see cref="State"/> was last written. Null until something happens, which is
        /// itself the honest answer for a registration nothing has started yet.</summary>
        public DateTimeOffset? StateSince;

        /// <summary>A failure before a client existed, or a workspace refusal whose client was released.
        /// The client's own record wins whenever there is one.</summary>
        public LanguageConnectionAttempt? LastAttempt;

        public readonly SemaphoreSlim Gate = new(1, 1);

        /// <summary>Set, under <see cref="Gate"/>, when the server is taken out: nothing starts it again.</summary>
        public bool Removed;

        /// <summary>
        /// Cancelled when the server is taken out, which ends a start in progress, an add-in's gate included: a removal
        /// waits for the start under <see cref="Gate"/>, and a gate nobody could cancel would make it wait for good.
        /// </summary>
        public readonly CancellationTokenSource Lifetime = new();

        /// <summary>
        /// Documents this registry has begun opening on this entry's server and has not yet opened there, each
        /// with what a request about it waits on (hexide-io/HexIDE#2909).
        /// </summary>
        public readonly ConcurrentDictionary<string, TaskCompletionSource> PendingOpens = new(StringComparer.Ordinal);
    }

    // ── Servers that arrive while the IDE runs (#457, #2760) ─────────────────────────────────────────

    /// <summary>
    /// Adds a server while the IDE runs: an add-in's, which registers after this router is built — at start-up, or on
    /// the consent pass after the window opens. The documents already open that it claims are opened on it, which starts
    /// it, as the first document it claims would.
    /// </summary>
    /// <returns>The server as it was added: what takes it out again, and what says when the open documents reached it.</returns>
    /// <exception cref="InvalidOperationException">A server with the registration's id is known already.</exception>
    public Contribution Add(LanguageServerRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        var entry = new Entry(registration);
        lock (_entriesGate)
        {
            if (_entries.Any(e => string.Equals(e.Registration.Id, registration.Id, StringComparison.Ordinal)))
                throw new InvalidOperationException($"A language server with the id '{registration.Id}' is known already.");

            // Ordered as the constructor orders them, and as stably: an equal priority comes after the ones already here.
            _entries = _entries.Append(entry).OrderByDescending(e => e.Registration.Priority).ToList();
        }

        _logger.LogInformation("Language server '{Id}' was added while running, from {Origin}.",
            registration.Id, registration.Origin ?? "the host");
        ConnectionsChanged?.Invoke(this, EventArgs.Empty);
        return new Contribution(this, entry, OpenKnownDocumentsOnAsync(entry));
    }

    // Each document already open that the new server claims, opened on it.
    //
    // NOT under _restartGate, though a workspace move's replay is. Every open passes through that gate, and holding it
    // while this server starts — its gate before launch included — would stall the opening of every other document, of
    // any language, for as long as one add-in's server takes to come up. Nothing here needs it: an open of the same
    // document racing in from the editor reaches the same client, which opens a document once and keeps the newest text
    // it was given (VBLspClient.TrackOpen), and a document closed meanwhile is not in _open when it is read again below.
    //
    // A workspace move reopens an add-in's gated server this way too, for the same reason (ReopenKnownDocumentsAsync);
    // `skip` is then the document whose open moved it, which that open sends itself.
    private async Task OpenKnownDocumentsOnAsync(Entry entry, string? skip = null)
    {
        // Held before the first await, as an open holds them, so a request about one waits for its open here (#2909).
        var held = _open.Where(d => !string.Equals(d.Key, skip, StringComparison.Ordinal)
                                    && ClaimantsFor(d.Key, d.Value.IsProjectMember).Contains(entry))
            .Select(d => d.Key).ToList();
        foreach (var uri in held) HoldOpen(entry, uri);

        // Off the caller's stack: the caller is an add-in's initialisation, which must not wait for a server to start.
        await Task.Yield();
        try
        {
            foreach (var uri in held)
            {
                if (!_open.ContainsKey(uri)) continue;

                await EnsureStartedAsync(entry, CancellationToken.None);

                // Read again after the start, which can take a while: the text may have changed, or the document closed.
                if (entry.Client is { } client && _open.TryGetValue(uri, out var current))
                    await client.OpenDocumentAsync(uri, current.Text);
                ReleaseOpen(entry, uri);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The documents already open could not all be opened on '{Id}'.", entry.Registration.Id);
        }
        finally
        {
            foreach (var uri in held) ReleaseOpen(entry, uri);
        }
    }

    private void Unsubscribe(ILspClient client)
    {
        client.DiagnosticsPublished -= OnInnerDiagnostics;
        client.CodeLensRefreshRequested -= OnInnerCodeLensRefresh;
        client.MessageShown -= OnInnerMessage;
        client.ProgressReceived -= OnInnerProgress;
        client.MessageLogged -= OnInnerLogMessage;
        client.TraceReceived -= OnInnerTrace;
        client.StateChanged -= OnInnerStateChanged;
    }

    // A server taken out: what it said about each document and its configuration problems are withdrawn, a start in
    // progress is called off — its gate before launch with it — and ends, then the server is stopped and its row goes.
    private async Task RemoveAsync(Entry entry)
    {
        // The withdrawals with the removal, under the lock Add takes: the id is free from here, and a server added under
        // it again says its own things, which a withdrawal by id after the old one's shutdown would take away with it.
        // The diagnostics' is queued with the deliveries rather than raised here, so it comes after whatever the old
        // server said before this and before anything a new one says, and no handler runs under the lock.
        bool deliver;
        lock (_entriesGate)
        {
            _entries = _entries.Where(e => !ReferenceEquals(e, entry)).ToList();
            if (entry.Client is { } speaking) speaking.DiagnosticsPublished -= OnInnerDiagnostics;
            ForgetContributionProblems(entry.Registration.Id);
            deliver = _diagnosticLifecycle.Queue(() =>
            {
                foreach (var p in _diagnostics.Withdraw(entry.Registration.Id))
                    DiagnosticsPublished?.Invoke(this, p);
            });
        }
        if (deliver) _diagnosticLifecycle.DeliverQueued();

        // Before the wait, not after: the start holds the gate, and would hold it for as long as an add-in's gate waits.
        await entry.Lifetime.CancelAsync();
        await entry.Gate.WaitAsync();
        try
        {
            entry.Removed = true;

            // After the start has ended, which is what could make it active again, and before the stop, which can take
            // the whole of a shutdown's time: nothing is generated, imported or tracked for a server that is going.
            if (_artifacts is not null) await _artifacts.ForgetAsync(entry.Registration);

            if (entry.Client is { } client)
            {
                client.DiagnosticsPublished -= OnInnerDiagnostics;
                client.CodeLensRefreshRequested -= OnInnerCodeLensRefresh;
                client.MessageShown -= OnInnerMessage;
                client.MessageLogged -= OnInnerLogMessage;
                client.TraceReceived -= OnInnerTrace;
                client.StateChanged -= OnInnerStateChanged;
                try { await client.StopAsync(); }
                catch (Exception ex) { _logger.LogDebug(ex, "Stop failed for {Id}", entry.Registration.Id); }
                client.ProgressReceived -= OnInnerProgress;
                entry.Client = null;
            }

            entry.State = LanguageConnectionState.Stopped;
            entry.StateSince = DateTimeOffset.UtcNow;
        }
        finally
        {
            entry.Gate.Release();
        }

        ConnectionsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// A server added while the IDE runs (<see cref="Add"/>): its client, once it has one, and what takes it out.
    /// </summary>
    public sealed class Contribution : IAsyncDisposable
    {
        private readonly LspClientRegistry _registry;
        private readonly Entry _entry;
        private int _disposed;

        internal Contribution(LspClientRegistry registry, Entry entry, Task opened)
        {
            _registry = registry;
            _entry = entry;
            Opened = opened;
        }

        /// <summary>The id it was added under.</summary>
        public string Id => _entry.Registration.Id;

        /// <summary>Completes when the documents that were open when it was added have been opened on it.</summary>
        public Task Opened { get; }

        /// <summary>Its client, once it has started; null before, and after it is taken out.</summary>
        public ILspClient? Client => _entry.Client;

        /// <summary>Cancelled when it is taken out.</summary>
        public CancellationToken Lifetime => _entry.Lifetime.Token;

        /// <summary>Stops the server, if it is running, and takes it out. Once; a second call does nothing.</summary>
        public ValueTask DisposeAsync() =>
            Interlocked.Exchange(ref _disposed, 1) == 0 ? new ValueTask(_registry.RemoveAsync(_entry)) : ValueTask.CompletedTask;
    }
}
