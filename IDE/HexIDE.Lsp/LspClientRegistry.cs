using System.Collections.Concurrent;
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
    private readonly ILspWorkspace? _workspace;

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
    private sealed record OpenDocument(bool IsProjectMember, string Text);

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
        IReadOnlyList<LanguageServerConfigProblem>? configurationProblems = null)
    {
        _logger = logger;
        _workspace = workspace;
        ConfigurationProblems = configurationProblems ?? [];
        // Ordered once. OrderByDescending is stable, so equal priorities keep registration order — which is
        // the documented fallback rather than an accident of how the sort happened to behave.
        _entries = registrations
            .OrderByDescending(r => r.Priority)
            .Select(r => new Entry(r))
            .ToList();
    }

    public event EventHandler<PublishDiagnosticsParams>? DiagnosticsPublished;
    public event EventHandler<ShowMessageParams>? MessageShown;
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

    public IReadOnlyList<LanguageServerConfigProblem> ConfigurationProblems { get; }

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
            (e.Client as VBLspClient)?.SentWorkspaceRootUri)).ToList();

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
            client.MessageShown -= OnInnerMessage;
            client.MessageLogged -= OnInnerLogMessage;
            client.TraceReceived -= OnInnerTrace;
            client.StateChanged -= OnInnerStateChanged;
            try { await client.StopAsync(); } catch (Exception ex) { _logger.LogDebug(ex, "Stop failed for {Id}", e.Registration.Id); }
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
        // Before anything starts or is used: a server told about one project must not go on serving
        // another. This document is named so the replay inside can skip it — it is opened below either
        // way, and opening it twice is the failure this argument exists to prevent.
        await RestartIfWorkspaceMovedAsync(uri, cancellationToken);

        // Recorded before routing, so change, close and save answer the same question this open did. The
        // registry deliberately holds no model of the project, and it could not work membership out for
        // itself: an `untitled:` name would have to be parsed back into project and document names, which
        // this change forbids everywhere else, and a path match cannot see a document that has no file.
        // The opener already knows -- the code window opens members, the carried-file editor does not.
        _open[uri] = new OpenDocument(isProjectMember, text);

        // The one place a server starts. Opening a document is the first moment its language is known to be
        // present, which is exactly the trigger lazy start is defined against.
        var claimants = ClaimantsFor(uri, isProjectMember);
        foreach (var e in claimants) await EnsureStartedAsync(e, cancellationToken);
        await Task.WhenAll(claimants
            .Where(e => e.Client is not null)
            .Select(e => e.Client!.OpenDocumentAsync(uri, text, cancellationToken)));
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
        // The connections first, so each drops its own state and a push server stops republishing under the
        // old name; then the ledger, which is what actually clears a mark no single connection owns.
        await Task.WhenAll(StartedClaimantsFor(uri)
            .Select(c => c.CloseDocumentForRenameAsync(uri, cancellationToken)));
        _open.TryRemove(uri, out _);
        ForgetDiagnosticsFor(uri);
    }

    public Task CloseDocumentAsync(string uri, CancellationToken cancellationToken = default)
    {
        // Routed BEFORE the record is dropped, or the close would be offered to a different set of servers
        // than the open was -- the exact asymmetry that leaves a server holding a document forever.
        var closing = Task.WhenAll(StartedClaimantsFor(uri).Select(c => c.CloseDocumentAsync(uri, cancellationToken)));
        _open.TryRemove(uri, out _);
        return closing;
    }

    public Task SaveDocumentAsync(string uri, CancellationToken cancellationToken = default) =>
        // No start here either, for the same reason as a change: saving a document nothing has opened is
        // not a reason to launch a server. Each claimant decides for itself whether it was asked for
        // saves, so of two servers holding one document only the one that asked hears about it — which is
        // why the gate belongs on the connection and not here.
        Task.WhenAll(StartedClaimantsFor(uri).Select(c => c.SaveDocumentAsync(uri, cancellationToken)));

    // ── One-answer features ─────────────────────────────────────────────────────────────────────────
    // A document has ONE structure and ONE set of folds, however many servers are looking at it. Two
    // servers answering describe the same thing twice, so the answers do not merge — they duplicate.
    // Asking only the top-priority claimant is what makes a second server attached to `.bas` an addition
    // rather than a corruption of what was already working.
    public Task<DocumentSymbol[]> RequestDocumentSymbolsAsync(string uri, CancellationToken ct = default) =>
        SoleClaimantFor(uri, "documentSymbolProvider") is { } c
            ? c.RequestDocumentSymbolsAsync(uri, ct)
            : Task.FromResult<DocumentSymbol[]>([]);

    // Folds have a second, sharper reason. AvaloniaEdit's FoldingManager takes a single ordered set of
    // sections; two servers' folds partially overlap in general, and a partially-overlapping section is
    // not merely a duplicate — it can take out folding for the whole document behind the debug-level
    // catch that wraps the update.
    public Task<FoldingRange[]> RequestFoldingRangesAsync(string uri, CancellationToken ct = default) =>
        SoleClaimantFor(uri, "foldingRangeProvider") is { } c
            ? c.RequestFoldingRangesAsync(uri, ct)
            : Task.FromResult<FoldingRange[]>([]);

    // ── Merged features ─────────────────────────────────────────────────────────────────────────────
    // Completions genuinely combine: two servers offer different candidates for one prefix, and dropping
    // either would hide something the user could have typed. This is the case the merge helpers exist for.
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

    public Task<Location[]?> RequestDeclarationAsync(string uri, Position position, CancellationToken ct = default) =>
        FirstAnswerAsync(uri, c => c.RequestDeclarationAsync(uri, position, ct));

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
    public Task<CodeLens[]> RequestCodeLensesAsync(string uri, CancellationToken ct = default) =>
        GatherAsync(uri, c => c.RequestCodeLensesAsync(uri, ct));

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
        DiagnosticsPublished?.Invoke(this, _diagnostics.Record(uri, owner, diagnostics));
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
        await StopAsync();
        foreach (var e in _entries) e.Gate.Dispose();
    }

    // ── Routing ─────────────────────────────────────────────────────────────────────────────────────

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
    /// Checked when a document opens rather than driven by a project event, because that is the moment the
    /// answer matters and it keeps this layer free of the project model. A workspace nobody has asked
    /// anything of costs nothing by being stale.
    /// </para>
    ///
    /// <para>
    /// A server that FAILED stays failed. The workspace changing is not a reason to believe a command that
    /// would not launch will launch now, and retrying on every project switch turns one broken entry into a
    /// cost paid repeatedly.
    /// </para>
    /// </summary>
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

        // Nothing is running, so whatever the workspace is now is what the first server will be told.
        if (!_entries.Any(e => e.Client is not null))
        {
            _rootedAt = current;
            return;
        }

        if (SameDirectory(current, _rootedAt)) return;

        _logger.LogInformation(
            "The workspace moved; restarting language servers so none keeps serving a project that is gone.");

        foreach (var e in _entries)
        {
            if (e.Client is not { } client) continue;

            // A failed entry keeps its client and its state. It is not restarted, so it must not be reset to
            // NotStarted — that would make it eligible to start again, which is the retry this is avoiding.
            if (e.State == LanguageConnectionState.Failed) continue;

            client.DiagnosticsPublished -= OnInnerDiagnostics;
            client.MessageShown -= OnInnerMessage;
            client.MessageLogged -= OnInnerLogMessage;
            client.TraceReceived -= OnInnerTrace;
            client.StateChanged -= OnInnerStateChanged;
            try { await client.StopAsync(); }
            catch (Exception ex) { _logger.LogDebug(ex, "Stop failed for {Id}", e.Registration.Id); }
            e.Client = null;
            // NotStarted rather than Stopped: this one is eligible to run again, at the new root.
            e.State = LanguageConnectionState.NotStarted;
            e.StateSince = DateTimeOffset.UtcNow;
        }

        _rootedAt = current;

        // Re-open everything that was open, before anything else is forwarded. The teardown above covers
        // EVERY entry, while the caller goes on to start and open only the claimants of the one document
        // that triggered it — so without this a Markdown server holding a carried file is stopped with
        // nothing to re-open it, and every later change to those documents reaches a server that never saw
        // them opened (hexide-io/HexIDE#469). A first save of a never-saved project moves the root, so
        // since #489 this is the ordinary path rather than an edge case.
        await ReopenKnownDocumentsAsync(triggeringUri, cancellationToken);

        ConnectionsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Opens every document the registry knows to be open on the connections that now claim it.
    /// </summary>
    /// <remarks>
    /// Driven from the record rather than from the triggering document's claimants: those are two
    /// different sets, and the teardown used the wider one.
    /// <para>
    /// <paramref name="triggeringUri"/> is skipped because its caller opens it immediately afterwards.
    /// It is not yet in the record on a first open — the record is written after this returns — but a
    /// re-open of a document already known would otherwise be sent twice.
    /// </para>
    /// </remarks>
    private async Task ReopenKnownDocumentsAsync(string? triggeringUri, CancellationToken cancellationToken)
    {
        foreach (var (uri, known) in _open)
        {
            if (triggeringUri is not null && string.Equals(uri, triggeringUri, StringComparison.Ordinal))
                continue;

            foreach (var e in ClaimantsFor(uri, known.IsProjectMember))
            {
                // A failed entry is not revived by a document arriving, which is the rule EnsureStartedAsync
                // already applies; the null check is what keeps that true here rather than restating it.
                await EnsureStartedAsync(e, cancellationToken);
                if (e.Client is { } client)
                    await client.OpenDocumentAsync(uri, known.Text, cancellationToken);
            }
        }
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
        try
        {
            if (e.Client is not null) return;

            e.State = LanguageConnectionState.Starting;
            e.StateSince = DateTimeOffset.UtcNow;
            ConnectionsChanged?.Invoke(this, EventArgs.Empty);

            var client = e.Registration.CreateClient();
            client.DiagnosticsPublished += OnInnerDiagnostics;
            client.MessageShown += OnInnerMessage;
            client.MessageLogged += OnInnerLogMessage;
            client.TraceReceived += OnInnerTrace;
            client.StateChanged += OnInnerStateChanged;
            e.Client = client;

            await client.StartAsync(cancellationToken);
            e.State = client.IsRunning ? LanguageConnectionState.Running : LanguageConnectionState.Failed;
            e.StateSince = DateTimeOffset.UtcNow;

            if (!client.IsRunning)
                _logger.LogWarning("Language server '{Id}' did not start; its languages have no support.",
                    e.Registration.Id);
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

    private void OnInnerMessage(object? sender, ShowMessageParams p) => MessageShown?.Invoke(this, p);

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
    private void OnInnerDiagnostics(object? sender, PublishDiagnosticsParams p) =>
        DiagnosticsPublished?.Invoke(this, _diagnostics.Record(p.Uri, OwnerOf(sender), p.Diagnostics));

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

    private sealed class Entry(LanguageServerRegistration registration)
    {
        public LanguageServerRegistration Registration { get; } = registration;
        public ILspClient? Client;
        public LanguageConnectionState State = LanguageConnectionState.NotStarted;

        /// <summary>When <see cref="State"/> was last written. Null until something happens, which is
        /// itself the honest answer for a registration nothing has started yet.</summary>
        public DateTimeOffset? StateSince;

        /// <summary>Set when the attempt failed before a client existed to hold it — a throw inside
        /// CreateClient, for instance. The client's own record wins whenever there is one.</summary>
        public LanguageConnectionAttempt? LastAttempt;

        public readonly SemaphoreSlim Gate = new(1, 1);
    }
}
