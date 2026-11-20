using System.Globalization;
using System.Text.Json;
using HexIDE.Addins;
using HexIDE.IDE;
using Microsoft.Extensions.Logging;

namespace HexIDE.Lsp;

/// <summary>
/// Language servers add-ins contribute (#457, #2760): each checked as an entry of <c>lsp-servers.json</c> is, made into a
/// registration as one is, and added to the router while the IDE runs.
/// </summary>
/// <remarks>
/// <para>
/// <b>The user's configuration wins.</b> An id the configuration names already — an entry of the user's file, switched
/// off or not, or the bundled server — keeps its entry, as a user's entry outranks a default. The contribution is not
/// added, and a configuration problem says why.
/// </para>
/// <para>
/// <b>The messages of an add-in's own are kept under its prefix.</b> A prefix in a namespace the protocol itself uses
/// would let an add-in speak for HexIDE's client, or answer a method in its place, so one is refused, and so is a send
/// or a claim outside the registration's prefix.
/// </para>
/// </remarks>
public sealed class LanguageServerContributions(
    ISettingsService settings,
    LspClientRegistry router,
    LanguageServerRegistrationFactory factory,
    LanguageServerConfigLoader loader,
    IReadOnlyCollection<string> configuredIds,
    ILogger<LanguageServerContributions> logger,
    IAddinCodeEditorActions? codeEditorActions,
    LanguageServerCommandStore? seen = null,
    IWorkspaceArtifactProviderRegistry? artifactProviders = null,
    TimeProvider? clock = null)
{
    /// <summary>
    /// The namespaces the Language Server Protocol's own methods are in (3.17): a prefix in one of them is not an add-in's.
    /// </summary>
    public static IReadOnlyList<string> ProtocolNamespaces { get; } =
    [
        "$/", "callHierarchy/", "client/", "codeAction/", "codeLens/", "completionItem/", "documentLink/", "inlayHint/",
        "notebookDocument/", "telemetry/", "textDocument/", "typeHierarchy/", "window/", "workspace/", "workspaceSymbol/",
    ];

    /// <summary>
    /// Adds an add-in's server, and returns the connection its messages go over. The server starts on the first document
    /// it claims, or at once when one it claims is open already.
    /// </summary>
    /// <param name="server">The server, as the add-in describes it.</param>
    /// <param name="origin">Who contributed it, for the Language Servers window: the add-in's title.</param>
    /// <param name="originId">The add-in's id, which keys the one notice about a publisher record without namespaces.</param>
    /// <param name="publisherNamespaces">
    /// The namespaces the add-in's verified publisher record issues (<see cref="PublisherNamespaces"/>). When given, the
    /// message prefix must be <c>&lt;ns&gt;/</c> and the command prefix <c>&lt;ns&gt;.</c> for one of them. Null for a record
    /// issued before the field existed, or an add-in with no verified record: today's rule applies, with a notice.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The entry fails the checks a file entry gets, is switched off, or its prefix or one of its methods is not the
    /// add-in's to use. What was wrong with the entry is also a configuration problem.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The id is taken: the configuration names it, and keeps its own entry, or another add-in contributed it first.
    /// </exception>
    private ServerChatToolsService? _chatTools;

    /// <summary>
    /// AI Chat's tools and context from the running servers that offer them (#4257). Held here, beside the servers an add-in
    /// contributes, because the host reaches the registry through this.
    /// </summary>
    public IServerChatTools ChatTools =>
        _chatTools ??= new ServerChatToolsService(() => router.ChatToolsServers(ChatToolsSetting), logger);

    /// <summary>
    /// The person's own setting for a server's AI Chat tools, by its id, which wins over its entry's (#4257). Read on every
    /// list, call and context request, so a change in Options applies to the next one.
    /// </summary>
    public Func<string, ServerChatToolsPolicy?>? ChatToolsSetting { get; init; }

    public AddinServerConnection Register(AddinLanguageServer server, string? origin, string? originId = null,
        IReadOnlyList<string>? publisherNamespaces = null)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(server.Entry);

        // Copied once, here, so the methods checked are the methods claimed: the add-in keeps its own dictionary, and one
        // added to it later, outside the prefix, would otherwise be answered by the add-in in the client's place.
        var claimed = new Dictionary<string, AddinServerMethod>(server.ServerMethods, StringComparer.Ordinal);
        CheckMessages(server.MessagePrefix, claimed);
        var commands = new Dictionary<string, AddinClientCommand>(server.ClientCommands, StringComparer.Ordinal);
        CheckCommands(server.CommandPrefix, commands);
        CheckNamespaces(server.MessagePrefix, server.CommandPrefix, publisherNamespaces, origin);
        if (server.BeforeLaunchTimeout <= TimeSpan.Zero || server.BeforeLaunchTimeout > AddinLanguageServer.MaxBeforeLaunchTimeout)
            throw new ArgumentException(
                $"The wait for a gate before launch must be positive and at most {AddinLanguageServer.MaxBeforeLaunchTimeout}.",
                nameof(server));

        var entry = server.Entry;
        var problems = new List<LanguageServerConfigProblem>();
        if (!loader.ValidateContributed(entry, problems, seen, artifactProviders))
        {
            router.ReportContributionProblems(entry.Id ?? "", problems);
            throw new ArgumentException(problems.FirstOrDefault(p => p.EntryRejected)?.Message ?? "The entry was rejected.",
                nameof(server));
        }

        var id = entry.Id!;
        if (configuredIds.Contains(id) || router.Knows(id))
        {
            var kept = new LanguageServerConfigProblem(
                id,
                configuredIds.Contains(id)
                    ? $"{origin ?? "An add-in"} contributes a language server with the id '{id}', which the configuration names "
                      + "already. The configured entry is kept, and the contribution is not added."
                    : $"{origin ?? "An add-in"} contributes a language server with the id '{id}', which another add-in has "
                      + "contributed already. The first is kept, and this one is not added.",
                true);
            router.ReportContributionProblems(id, [kept]);
            throw new InvalidOperationException(kept.Message);
        }

        if (factory.Create([entry]) is not [var made])
        {
            router.ReportContributionProblems(id, problems);
            throw new ArgumentException($"The language server '{id}' is switched off, so there is nothing to add.", nameof(server));
        }

        // A record issued before publisher namespaces existed binds no prefix: say so, once for each add-in.
        if (publisherNamespaces is null && (server.MessagePrefix is not null || server.CommandPrefix is not null)
            && NoticeOnce(originId ?? origin ?? id))
            problems.Add(new LanguageServerConfigProblem(id,
                $"{origin ?? "An add-in"} uses the prefix {string.Join(" and ", new[] { server.MessagePrefix, server.CommandPrefix }.OfType<string>().Select(p => $"'{p}'"))}, "
                + "and its publisher's record names no namespaces, so the prefix isn't checked against its publisher. "
                + "Records issued before v1 don't carry them.", false));

        // What was worth saying about an entry that is used all the same: a launch command not run before, a misspelt field.
        router.ReportContributionProblems(id, problems);

        // The connection exists only once the router has the server, but a start can finish before then, so what runs
        // after a start waits for it rather than finding it missing.
        var connection = new TaskCompletionSource<AddinServerConnection>(TaskCreationOptions.RunContinuationsAsynchronously);
        var afterStart = server.AfterStart;
        var registration = made with
        {
            BeforeLaunch = server.BeforeLaunch is { } gate
                ? token => WithinAsync(gate, server.BeforeLaunchTimeout, origin, id, token)
                : null,
            Origin = origin,
            // The user's object, then the profile's default, then what the add-in's own entry carries (#4255).
            Settings = settings.GetLanguageServerSettings(id) ?? made.Settings,
            CreateClient = claimed.Count == 0 && commands.Count == 0 && afterStart is null ? made.CreateClient : () =>
            {
                var client = made.CreateClient();
                if (client is VBLspClient lsp)
                {
                    if (claimed.Count > 0) lsp.Claim(claimed);
                    if (commands.Count > 0)
                    {
                        lsp.ClaimCommands(commands);
                        lsp.SetCommandLifetime(() => connection.Task.IsCompletedSuccessfully ? connection.Task.Result.Lifetime : default);
                    }
                    // On the pool, not after a yield: a yield returns to the context of the start, and a document opened
                    // from the code window starts its server on the UI thread.
                    if (afterStart is not null)
                        lsp.Connected += (_, _) => _ = Task.Run(() => RunAfterStartAsync(afterStart, connection.Task, id));
                }
                return client;
            },
        };

        logger.LogInformation("{Origin} contributes the language server '{Id}'.", origin ?? "An add-in", id);
        var added = new AddinServerConnection(router.Add(registration), server.MessagePrefix, logger, settings, router, codeEditorActions, made.Settings);
        connection.SetResult(added);
        return added;
    }

    // The add-in's gate, for as long as it said at most. Not by its token alone: a gate that never looks at it would
    // still hold the start, so the wait ends at the limit whether the gate does or not, and the token tells it to stop.
    //
    // The message is the row's detail, which is machine text quoted as it was said, never a localization key
    // (LanguageConnectionStep), so its number is written the same in every culture.
    private async Task WithinAsync(Func<CancellationToken, Task> gate, TimeSpan limit, string? origin, string id,
        CancellationToken cancellationToken)
    {
        var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task running;
        try
        {
            running = gate(bounded.Token);
        }
        catch
        {
            bounded.Dispose();
            throw;
        }

        try
        {
            await running.WaitAsync(limit, clock ?? TimeProvider.System, cancellationToken);
        }
        catch (TimeoutException ex) when (!ReferenceEquals(ex, running.Exception?.InnerException))
        {
            // The limit's, not one the gate threw itself, which is the gate's to word. The gate is told to stop and runs on
            // until it does; its token's source lives as long, and what it ends with is logged rather than lost.
            await bounded.CancelAsync();
            _ = running.ContinueWith(ended =>
            {
                if (ended.Exception?.InnerException is { } late and not OperationCanceledException)
                    logger.LogWarning(late, "{Origin}'s gate before launching '{Id}' failed after its limit.", origin ?? "An add-in", id);
                bounded.Dispose();
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            throw new TimeoutException(string.Create(CultureInfo.InvariantCulture,
                $"{origin ?? "The add-in"} did not let '{id}' start within {limit.TotalSeconds:0.#} seconds."));
        }
        catch
        {
            bounded.Dispose();
            throw;
        }

        bounded.Dispose();
    }

    // On the thread pool, so a start never waits for an add-in and the add-in never holds the UI thread.
    private async Task RunAfterStartAsync(
        Func<IAddinServerConnection, CancellationToken, Task> afterStart, Task<AddinServerConnection> connection, string id)
    {
        try
        {
            var added = await connection;
            await afterStart(added, added.Lifetime);
        }
        catch (OperationCanceledException)
        {
            // The server was taken out while the add-in was still answering its start.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The add-in's step after '{Id}' started failed.", id);
        }
    }

    private readonly HashSet<string> _noticed = new(StringComparer.OrdinalIgnoreCase);

    private bool NoticeOnce(string key)
    {
        lock (_noticed) return _noticed.Add(key);
    }

    // #4258 part D: with a verified publisher record that issues namespaces, the prefixes must be under one of them.
    private static void CheckNamespaces(string? messagePrefix, string? commandPrefix, IReadOnlyList<string>? namespaces,
        string? origin)
    {
        const string server = "server";
        if (namespaces is null) return;
        string Issued() => namespaces.Count == 0 ? "none" : string.Join(", ", namespaces.Select(ns => $"'{ns}'"));
        if (messagePrefix is { } message && !PublisherNamespaces.AllowsMessagePrefix(namespaces, message))
            throw new ArgumentException($"{origin ?? "The add-in"}'s message prefix '{message}' isn't under a namespace its "
                + $"publisher was issued ({Issued()}): it must be '<namespace>/' for one of them.", server);
        if (commandPrefix is { } command && !PublisherNamespaces.AllowsCommandPrefix(namespaces, command))
            throw new ArgumentException($"{origin ?? "The add-in"}'s command prefix '{command}' isn't under a namespace its "
                + $"publisher was issued ({Issued()}): it must be '<namespace>.' for one of them.", server);
    }

    // The prefix, and the methods claimed under it, checked before anything is added.
    private static void CheckMessages(string? messagePrefix, IReadOnlyDictionary<string, AddinServerMethod> claimed)
    {
        // Register's parameter, which is what the add-in passed and got wrong.
        const string server = "server";
        if (messagePrefix is not { } prefix)
        {
            if (claimed.Count > 0)
                throw new ArgumentException("Methods can be claimed only under a message prefix.", server);
            return;
        }

        if (prefix.Length < 2 || !prefix.EndsWith('/') || prefix.Any(char.IsWhiteSpace))
            throw new ArgumentException($"'{prefix}' is not a message prefix: one is a name followed by '/', such as 'myserver/'.",
                server);

        if (ProtocolNamespaces.Any(ns => prefix.StartsWith(ns, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"'{prefix}' is a namespace the Language Server Protocol uses, so it is not an add-in's.",
                server);

        // Core's own (hexide/projectProperties, #4024), whatever a publisher record says.
        if (prefix.StartsWith("hexide/", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"'{prefix}' is a reserved message namespace, so it is not an add-in's.", server);

        foreach (var method in claimed.Keys)
        {
            if (!IsUnder(method, prefix))
                throw new ArgumentException($"'{method}' is not under the registration's prefix '{prefix}'.", server);
        }
    }

    private static void CheckCommands(string? commandPrefix, IReadOnlyDictionary<string, AddinClientCommand> claimed)
    {
        const string server = "server";
        if (commandPrefix is not { } prefix)
        {
            if (claimed.Count > 0)
                throw new ArgumentException("Commands can be claimed only under a command prefix.", server);
            return;
        }

        if (prefix.Length < 2 || !prefix.EndsWith('.') || prefix.Any(char.IsWhiteSpace))
            throw new ArgumentException($"'{prefix}' is not a command prefix: one is a name followed by '.', such as 'myserver.'.",
                server);

        if (new[] { "editor.", "vscode.", "hexide." }.Any(ns => prefix.StartsWith(ns, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"'{prefix}' is a reserved command namespace, so it is not an add-in's.", server);

        foreach (var command in claimed.Keys)
            if (!IsUnder(command, prefix))
                throw new ArgumentException($"'{command}' is not under the registration's prefix '{prefix}'.", server);
    }

    internal static bool IsUnder(string method, string prefix) =>
        method.Length > prefix.Length && method.StartsWith(prefix, StringComparison.Ordinal);
}

/// <summary>
/// The connection to a language server an add-in contributed (<see cref="IAddinServerConnection"/>), over the router's
/// own connection to it.
/// </summary>
public sealed class AddinServerConnection : IAddinServerConnection, IAsyncDisposable
{
    private readonly LspClientRegistry.Contribution _contribution;
    private readonly string? _prefix;
    private readonly ILogger _logger;
    private readonly ISettingsService _settings;
    private readonly LspClientRegistry _router;
    private readonly IAddinCodeEditorActions? _codeEditorActions;

    private readonly JsonElement? _entrySettings;

    internal AddinServerConnection(LspClientRegistry.Contribution contribution, string? prefix, ILogger logger, ISettingsService settings, LspClientRegistry router,
        IAddinCodeEditorActions? codeEditorActions, JsonElement? entrySettings = null)
    {
        _entrySettings = entrySettings?.Clone();
        _contribution = contribution;
        _prefix = prefix;
        _logger = logger;
        _settings = settings;
        _router = router;
        _codeEditorActions = codeEditorActions;
    }

    public JsonElement? GetSettings() => _settings.GetLanguageServerSettings(Id) ?? _entrySettings?.Clone();

    public async Task SetSettingsAsync(JsonElement settings, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Lifetime.IsCancellationRequested, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (settings.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Language server settings must be a JSON object.", nameof(settings));
        _settings.SetLanguageServerSettings(Id, settings);
        await _router.UpdateServerSettingsAsync(Id, GetSettings());
    }

    /// <summary>Removes the user's object and notifies the server with the resolved fallback.</summary>
    public async Task ResetSettingsAsync()
    {
        _settings.RemoveLanguageServerSettings(Id);
        await _router.UpdateServerSettingsAsync(Id, GetSettings(), forceNotification: true);
    }

    public string Id => _contribution.Id;

    public bool IsRunning => _contribution.Client is { IsRunning: true };

    /// <summary>Completes when the documents that were open when the server was added have been opened on it.</summary>
    public Task Opened => _contribution.Opened;

    /// <summary>Cancelled when the server is taken out.</summary>
    internal CancellationToken Lifetime => _contribution.Lifetime;

    /// <summary>Whether the server has been taken out, as when its add-in is disabled.</summary>
    public bool IsTakenOut => Lifetime.IsCancellationRequested;

    /// <summary>The router's client for the server, once it has started.</summary>
    internal ILspClient? Client => _contribution.Client;

    public Task<JsonElement?> RequestAsync(
        string method, JsonElement? parameters = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        Running(method).SendCustomRequestAsync(method, parameters, timeout, cancellationToken);

    public Task NotifyAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default) =>
        Running(method).SendCustomNotificationAsync(method, parameters, cancellationToken);

    public Task<HexIDE.Lsp.Messages.ApplyWorkspaceEditResult> ApplyEditAsync(
        HexIDE.Lsp.Messages.WorkspaceEdit edit, CancellationToken cancellationToken) =>
        _contribution.Client is VBLspClient client
            ? client.ApplyClaimedEditAsync(edit, cancellationToken)
            : Task.FromResult(new HexIDE.Lsp.Messages.ApplyWorkspaceEditResult(false,
                HexIDE.IDE.Distribution.DisplayName + " applies a server's edits only while a command the user started is running."));

    public async Task<bool> StartRenameAsync(string uri, HexIDE.Lsp.Messages.Position position,
        CancellationToken cancellationToken = default)
    {
        using var lifetime = ActionLifetime(cancellationToken);
        return await _codeEditorActions!.StartRenameAsync(uri, position, lifetime.Token);
    }

    public async Task<bool> GoToLocationAsync(string uri, HexIDE.Lsp.Messages.Range range,
        CancellationToken cancellationToken = default)
    {
        using var lifetime = ActionLifetime(cancellationToken);
        return await _codeEditorActions!.GoToLocationAsync(uri, range, lifetime.Token);
    }

    public async Task ShowReferencesAsync(IReadOnlyList<HexIDE.Lsp.Messages.Location> locations,
        CancellationToken cancellationToken = default)
    {
        using var lifetime = ActionLifetime(cancellationToken);
        await _codeEditorActions!.ShowReferencesAsync(locations, lifetime.Token);
    }

    private CancellationTokenSource ActionLifetime(CancellationToken cancellationToken)
    {
        if (_contribution.Client is not VBLspClient client)
            throw new InvalidOperationException("Code-window actions require a running claimed-command handler.");
        var handler = client.ClaimedCommandLifetime();
        if (_codeEditorActions is null) throw new InvalidOperationException("No code-window host is available.");
        return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, handler);
    }

    private VBLspClient Running(string method)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (_prefix is null || !LanguageServerContributions.IsUnder(method, _prefix))
            throw new ArgumentException(_prefix is null
                ? "The registration has no message prefix, so it has no messages of its own to send."
                : $"'{method}' is not under the registration's prefix '{_prefix}'.", nameof(method));

        return _contribution.Client is VBLspClient { IsRunning: true } client
            ? client
            : throw new InvalidOperationException($"The language server '{Id}' is not running.");
    }

    /// <summary>Stops the server, if it is running, and takes it out; the taking out goes on after this returns.</summary>
    public void Dispose()
    {
        var removing = _contribution.DisposeAsync().AsTask();
        _ = removing.ContinueWith(t => _logger.LogWarning(t.Exception, "The language server '{Id}' could not be taken out.", Id),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    /// <summary>Stops the server, if it is running, and takes it out.</summary>
    public ValueTask DisposeAsync() => _contribution.DisposeAsync();
}
