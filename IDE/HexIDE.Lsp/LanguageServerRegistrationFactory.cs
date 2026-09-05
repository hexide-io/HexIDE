using Microsoft.Extensions.Logging;

namespace HexIDE.Lsp;

/// <summary>
/// Turns configuration entries into the registrations the router understands, each with the transport its
/// entry names.
///
/// <para>
/// Its own class rather than a few lines in the dependency-injection setup: this is where an entry's
/// transport is chosen and a client is built, which is logic, and a wiring table is a poor place to keep
/// logic that wants tests.
/// </para>
///
/// <para>
/// <b>Every transport is built per start, never once.</b> A transport is single-use — a spawned process
/// that exits is not respawned, a socket that closes is closed — and a client is rebuilt whenever the
/// workspace moves. Handing out one instance would give the second client a disposed transport.
/// </para>
/// </summary>
public sealed class LanguageServerRegistrationFactory(ILoggerFactory loggerFactory, ILspWorkspace? workspace = null)
{
    /// <summary>
    /// The registrations for these entries, skipping the ones that are switched off.
    ///
    /// <para>
    /// A disabled entry produces nothing at all rather than a registration that refuses to start: the
    /// router would otherwise carry a connection whose only purpose is to say no, and every "is anything
    /// claiming this file" answer would have to special-case it.
    /// </para>
    /// </summary>
    public IReadOnlyList<LanguageServerRegistration> Create(IReadOnlyList<LanguageServerEntry> entries)
    {
        var registrations = new List<LanguageServerRegistration>();

        foreach (var entry in entries)
        {
            if (entry.Enabled == false || entry.Id is not { } id) continue;
            if (TransportFactoryFor(entry) is not { } transport) continue;

            var languageId = entry.LanguageId ?? "";
            registrations.Add(new LanguageServerRegistration(
                Id: id,
                DisplayName: string.IsNullOrWhiteSpace(entry.DisplayName) ? id : entry.DisplayName,
                Extensions: entry.Extensions ?? [],
                LanguageId: languageId,
                CreateClient: () => new VBLspClient(
                    transport(), loggerFactory.CreateLogger<VBLspClient>(), languageId, workspace),
                Priority: entry.Priority ?? 0));
        }

        return registrations;
    }

    /// <summary>
    /// How to reach this entry's server, or null when it names no transport this build understands.
    ///
    /// <para>
    /// Returns a factory rather than a transport for the reason in the class summary. Null should not
    /// normally be reachable — the loader rejects an entry whose transport is unknown or whose required
    /// field is missing — but it is not this class's place to assume the loader ran.
    /// </para>
    /// </summary>
    private Func<ILspTransport>? TransportFactoryFor(LanguageServerEntry entry) =>
        entry.Transport?.Trim().ToLowerInvariant() switch
        {
            "stdio" when !string.IsNullOrWhiteSpace(entry.Command) => () => new StdioProcessLspTransport(
                new LspServerInfo(
                    entry.Command!.Trim(),
                    entry.Arguments?.Trim() ?? "",
                    // Empty, not the current directory: the transport then asks the workspace, so a server
                    // launched lazily runs in whatever project is open by then rather than wherever the IDE
                    // happened to start.
                    entry.WorkingDirectory?.Trim() ?? ""),
                loggerFactory.CreateLogger<StdioProcessLspTransport>(),
                workspace),

            "websocket" when !string.IsNullOrWhiteSpace(entry.Endpoint) => () => new WebSocketLspTransport(
                entry.Endpoint!.Trim(), loggerFactory.CreateLogger<WebSocketLspTransport>()),

            "pipe" when !string.IsNullOrWhiteSpace(entry.PipeName) => () => new NamedPipeLspTransport(
                entry.PipeName!.Trim(),
                // Connect by default: a server already running that owns the pipe is the ordinary case, and
                // listening means the IDE owns the endpoint and waits, which hangs if nothing dials in.
                string.Equals(entry.PipeRole?.Trim(), "listen", StringComparison.OrdinalIgnoreCase)
                    ? NamedPipeRole.Listen
                    : NamedPipeRole.Connect,
                loggerFactory.CreateLogger<NamedPipeLspTransport>()),

            _ => null,
        };
}
