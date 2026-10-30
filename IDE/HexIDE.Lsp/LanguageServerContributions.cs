using System.Text.Json;
using HexIDE.Addins;
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
    LspClientRegistry router,
    LanguageServerRegistrationFactory factory,
    LanguageServerConfigLoader loader,
    IReadOnlyCollection<string> configuredIds,
    ILogger<LanguageServerContributions> logger,
    LanguageServerCommandStore? seen = null,
    IWorkspaceArtifactProviderRegistry? artifactProviders = null)
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
    /// <exception cref="ArgumentException">
    /// The entry fails the checks a file entry gets, is switched off, or its prefix or one of its methods is not the
    /// add-in's to use. What was wrong with the entry is also a configuration problem.
    /// </exception>
    /// <exception cref="InvalidOperationException">The configuration names the id already, and keeps its own entry.</exception>
    public AddinServerConnection Register(AddinLanguageServer server, string? origin)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(server.Entry);
        CheckMessages(server);

        var entry = server.Entry;
        var problems = new List<LanguageServerConfigProblem>();
        if (!loader.ValidateContributed(entry, problems, seen, artifactProviders))
        {
            router.ReportContributionProblems(problems);
            throw new ArgumentException(problems.FirstOrDefault(p => p.EntryRejected)?.Message ?? "The entry was rejected.",
                nameof(server));
        }

        var id = entry.Id!;
        if (configuredIds.Contains(id) || router.Knows(id))
        {
            var kept = new LanguageServerConfigProblem(
                id,
                $"{origin ?? "An add-in"} contributes a language server with the id '{id}', which the configuration names already. "
              + "The configured entry is kept, and the contribution is not added.",
                true);
            router.ReportContributionProblems([kept]);
            throw new InvalidOperationException(kept.Message);
        }

        if (factory.Create([entry]) is not [var made])
        {
            router.ReportContributionProblems(problems);
            throw new ArgumentException($"The language server '{id}' is switched off, so there is nothing to add.", nameof(server));
        }

        // What was worth saying about an entry that is used all the same: a launch command not run before, a misspelt field.
        router.ReportContributionProblems(problems);

        var claimed = server.ServerMethods;
        var registration = made with
        {
            BeforeLaunch = server.BeforeLaunch,
            Origin = origin,
            CreateClient = claimed.Count == 0 ? made.CreateClient : () =>
            {
                var client = made.CreateClient();
                if (client is VBLspClient connection) connection.Claim(claimed);
                return client;
            },
        };

        logger.LogInformation("{Origin} contributes the language server '{Id}'.", origin ?? "An add-in", id);
        return new AddinServerConnection(router.Add(registration), server.MessagePrefix, logger);
    }

    // The prefix, and the methods claimed under it, checked before anything is added.
    private static void CheckMessages(AddinLanguageServer server)
    {
        if (server.MessagePrefix is not { } prefix)
        {
            if (server.ServerMethods.Count > 0)
                throw new ArgumentException("Methods can be claimed only under a message prefix.", nameof(server));
            return;
        }

        if (prefix.Length < 2 || !prefix.EndsWith('/') || prefix.Any(char.IsWhiteSpace))
            throw new ArgumentException($"'{prefix}' is not a message prefix: one is a name followed by '/', such as 'myserver/'.",
                nameof(server));

        if (ProtocolNamespaces.Any(ns => prefix.StartsWith(ns, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"'{prefix}' is a namespace the Language Server Protocol uses, so it is not an add-in's.",
                nameof(server));

        foreach (var method in server.ServerMethods.Keys)
        {
            if (!IsUnder(method, prefix))
                throw new ArgumentException($"'{method}' is not under the registration's prefix '{prefix}'.", nameof(server));
        }
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

    internal AddinServerConnection(LspClientRegistry.Contribution contribution, string? prefix, ILogger logger)
    {
        _contribution = contribution;
        _prefix = prefix;
        _logger = logger;
    }

    public string Id => _contribution.Id;

    public bool IsRunning => _contribution.Client is { IsRunning: true };

    /// <summary>Completes when the documents that were open when the server was added have been opened on it.</summary>
    public Task Opened => _contribution.Opened;

    public Task<JsonElement?> RequestAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default) =>
        Running(method).SendCustomRequestAsync(method, parameters, cancellationToken);

    public Task NotifyAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default) =>
        Running(method).SendCustomNotificationAsync(method, parameters);

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
