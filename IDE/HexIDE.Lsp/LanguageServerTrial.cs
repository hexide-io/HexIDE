using System.Text.Json;
using Microsoft.Extensions.Logging;

using HexIDE.Conversations;
using HexIDE.Redaction;

namespace HexIDE.Lsp;

/// <summary>
/// Starts one language-server entry on its own, to see whether it starts and what it says it can do, then stops it
/// (#1209). It's the Language Servers window's Test connection, and nothing else registers or reuses its client.
/// </summary>
/// <remarks>
/// <para>
/// <b>A trial launches a command, so it has gates of its own.</b> The window reads <c>lsp-servers.json</c> again
/// after startup, and that file is one any process running as the person may write. So a command that
/// <see cref="LanguageServerCommandStore"/> hasn't seen, or has seen with other arguments or environment, is shown
/// through <see cref="ILanguageServerLaunchConfirmation"/> before it runs. Declining runs nothing, and agreeing
/// records it as seen, as saving the entry in the window does. A bundled entry's id earns no exemption: a person's
/// entry with the same id replaces it.
/// </para>
/// <para>
/// <b>The trial's client can't act on the IDE.</b> Its factory is built here, from parts that hold no editor
/// callbacks, so the server under test can't apply an edit, open a document or ask the person a question: the client
/// refuses or answers no to each. It isn't added to the router, so it never sees an open document, and it's recorded
/// in the protocol capture under <see cref="CaptureIdPrefix"/> and the entry's id, apart from the live connection.
/// </para>
/// <para>
/// <b>It writes no workspace file.</b> A real start writes an entry's declared <c>workspaceArtifact</c>, with the
/// person's consent, and marks the server active for later regeneration. A trial does neither, and says so in
/// <see cref="LanguageServerTrialResult.ArtifactNotGenerated"/>, so a server that fails here for want of that file
/// isn't taken for a broken one.
/// </para>
/// <para>
/// <b>A server it didn't launch keeps running.</b> For an entry with no command, the IDE only connects, and the
/// client's stop leaves such a server running, as it does on main.
/// </para>
/// </remarks>
public sealed class LanguageServerTrial
{
    /// <summary>What the protocol capture puts before the entry's id for a trial's connection.</summary>
    public const string CaptureIdPrefix = "test:";

    private readonly LanguageServerRegistrationFactory _factory;

    /// <summary>The trial's own factory, for the test that pins it holds no editor callbacks.</summary>
    internal LanguageServerRegistrationFactory Factory => _factory;
    private readonly LanguageServerCommandStore _seen;
    private readonly ILanguageServerLaunchConfirmation _confirmation;

    /// <param name="seen">The store the Language Servers window records saved commands in, so one saved there isn't asked about again.</param>
    /// <param name="confirmation">Required. With none, nothing can be confirmed, so nothing unseen could run.</param>
    public LanguageServerTrial(
        ILoggerFactory loggerFactory,
        LanguageServerCommandStore seen,
        ILanguageServerLaunchConfirmation confirmation,
        ILspWorkspace? workspace = null,
        ConversationLog? capture = null,
        IWorkspaceArtifactProviderRegistry? artifactProviders = null,
        ConversationRedactor? workspaceNames = null,
        Func<string>? locale = null,
        HexIDE.IDE.ISettingsService? settings = null)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(seen);
        ArgumentNullException.ThrowIfNull(confirmation);
        _seen = seen;
        _confirmation = confirmation;
        // No showDocument, showMessageRequest or applyEdit: least privilege, by construction.
        _factory = new LanguageServerRegistrationFactory(loggerFactory, workspace, capture, artifactProviders,
            workspaceNames, locale, settings);
    }

    /// <summary>
    /// Confirms <paramref name="entry"/>'s command if it's unseen, then starts it, reads what it reported, and stops it.
    /// The command shown, and the one launched, both come from this one <paramref name="entry"/>.
    /// </summary>
    public async Task<LanguageServerTrialResult> RunAsync(LanguageServerEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Id is not { Length: > 0 } id)
            return LanguageServerTrialResult.NotRun(LanguageServerTrialOutcome.NotLaunchable, "", null, entry);

        var command = LanguageServerConfigLoader.LaunchedCommandOf(entry);
        if (command is not null && _seen.IsNewOrChanged(id, command))
        {
            var name = string.IsNullOrWhiteSpace(entry.DisplayName) ? id : entry.DisplayName;
            bool agreed;
            try { agreed = await _confirmation.ConfirmLaunchAsync(id, name, command, cancellationToken); }
            catch (OperationCanceledException) { agreed = false; }
            if (!agreed)
                return LanguageServerTrialResult.NotRun(LanguageServerTrialOutcome.Declined, id, command, entry);
            _seen.Record(id, command);
        }

        if (_factory.Create([entry], CaptureIdPrefix, includeDisabled: true) is not [var registration])
            return LanguageServerTrialResult.NotRun(LanguageServerTrialOutcome.NotLaunchable, id, command, entry);

        var client = registration.CreateClient();
        string? startError = null, stopError = null;
        try
        {
            try { await client.StartAsync(cancellationToken); }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                startError = ex.Message;
            }

            var outcome = client.IsRunning ? LanguageServerTrialOutcome.Initialized
                : client.WaitingForWorkspace ? LanguageServerTrialOutcome.WaitingForWorkspace
                : LanguageServerTrialOutcome.Failed;
            var result = new LanguageServerTrialResult(outcome, id, command,
                Disabled: entry.Enabled == false,
                ArtifactNotGenerated: entry.WorkspaceArtifact is not null || entry.ProjectImport is not null,
                Attempt: client.LastAttempt,
                Identity: client.IsRunning ? client.ReportedIdentity : null,
                AdvertisedCapabilities: client.IsRunning ? CapabilityNames(client.AdvertisedCapabilities) : [],
                DeclinedCapabilities: client.IsRunning ? [.. client.DeclinedCapabilities] : [],
                StartError: startError,
                StopError: null);

            try { await client.StopAsync(); }
            catch (Exception ex) { stopError = ex.Message; }
            return result with { StopError = stopError };
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    /// <summary>Top-level capability names the server advertised as present, sorted: absent, null and <c>false</c> left out.</summary>
    internal static IReadOnlyList<string> CapabilityNames(JsonElement? capabilities) =>
        capabilities is { ValueKind: JsonValueKind.Object } object_
            ? [.. object_.EnumerateObject()
                .Where(p => p.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.False or JsonValueKind.Undefined))
                .Select(p => p.Name)
                .Order(StringComparer.Ordinal)]
            : [];
}

/// <summary>How a trial ended (#1209).</summary>
public enum LanguageServerTrialOutcome
{
    /// <summary>The person declined to run a command the IDE hadn't seen. Nothing ran.</summary>
    Declined,
    /// <summary>The entry has no id, or names no transport this build can reach. Nothing ran.</summary>
    NotLaunchable,
    /// <summary>The server started and answered <c>initialize</c>.</summary>
    Initialized,
    /// <summary>The server needs a workspace, and no project is open. Not a failure.</summary>
    WaitingForWorkspace,
    /// <summary>The server didn't start or didn't initialize. <see cref="LanguageServerTrialResult.Attempt"/> says where.</summary>
    Failed,
}

/// <summary>What a trial found (#1209).</summary>
/// <param name="Command">The command identity confirmed or run, or null for an entry the IDE only connects to.</param>
/// <param name="Disabled">The entry is switched off in the file, so the IDE won't start it until it's switched on.</param>
/// <param name="ArtifactNotGenerated">
/// The entry declares a <c>workspaceArtifact</c> or <c>projectImport</c>, which a real start generates with consent
/// and a trial doesn't. A server that needs that file can fail a trial and still work in a real start.
/// </param>
/// <param name="Attempt">The start's steps, as the window shows them for a live connection.</param>
public sealed record LanguageServerTrialResult(
    LanguageServerTrialOutcome Outcome,
    string ServerId,
    string? Command,
    bool Disabled,
    bool ArtifactNotGenerated,
    LanguageConnectionAttempt? Attempt,
    ServerIdentity? Identity,
    IReadOnlyList<string> AdvertisedCapabilities,
    IReadOnlyList<string> DeclinedCapabilities,
    string? StartError,
    string? StopError)
{
    /// <summary>A trial failed, and the entry declares a workspace file only a real start generates.</summary>
    public bool MayNeedWorkspaceArtifact => Outcome == LanguageServerTrialOutcome.Failed && ArtifactNotGenerated;

    internal static LanguageServerTrialResult NotRun(LanguageServerTrialOutcome outcome, string id, string? command,
        LanguageServerEntry entry) =>
        new(outcome, id, command, entry.Enabled == false,
            entry.WorkspaceArtifact is not null || entry.ProjectImport is not null,
            null, null, [], [], null, null);
}
