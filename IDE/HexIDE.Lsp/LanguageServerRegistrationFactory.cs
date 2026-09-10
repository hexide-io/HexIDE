using Microsoft.Extensions.Logging;

using HexIDE.Lsp.Messages;

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
            if (TransportFor(entry) is not { } transport) continue;

            var languageId = entry.LanguageId ?? "";

            // An unreadable level is not a reason to refuse the server. It is reported by the loader and
            // falls back to off here, because "your trace setting is misspelled" and "your language server
            // will not start" are wildly different costs for the same typo.
            var trace = LspTraceValue.Normalise(entry.Trace) ?? LspTraceValue.Off;

            registrations.Add(new LanguageServerRegistration(
                Id: id,
                DisplayName: string.IsNullOrWhiteSpace(entry.DisplayName) ? id : entry.DisplayName,
                Extensions: entry.Extensions ?? [],
                LanguageId: languageId,
                CreateClient: () => new VBLspClient(
                    transport.Create(), loggerFactory.CreateLogger<VBLspClient>(), languageId, workspace,
                    trace: trace),
                Priority: entry.Priority ?? 0,
                Transport: transport.Kind,
                Endpoint: transport.Endpoint,
                Trace: trace));
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
    /// <summary>
    /// A transport factory together with what it is — the kind, and the address it resolved to.
    /// </summary>
    /// <remarks>
    /// One method returns all three deliberately. The obvious alternative is a second switch that describes
    /// what the first one built, and two switches over the same input drift: a transport added to one and
    /// forgotten in the other yields a working connection that reports itself as something else, which is
    /// worse than reporting nothing.
    /// </remarks>
    private sealed record TransportChoice(
        Func<ILspTransport> Create, LanguageConnectionTransport Kind, string? Endpoint);

    private TransportChoice? TransportFor(LanguageServerEntry entry)
    {
        if (TransportFactoryFor(entry) is not { } create) return null;

        return entry.Transport?.Trim().ToLowerInvariant() switch
        {
            // The command line as the user wrote it. This is the first thing anyone needs when a server
            // does not start, and it must be comparable to their own file character for character.
            "stdio" => new TransportChoice(create, LanguageConnectionTransport.Stdio,
                string.Join(' ', new[] { entry.Command?.Trim(), entry.Arguments?.Trim() }
                    .Where(part => !string.IsNullOrWhiteSpace(part)))),

            "websocket" => new TransportChoice(create, LanguageConnectionTransport.WebSocket,
                entry.Endpoint?.Trim()),

            // The role belongs with the name: connecting to a pipe and owning one fail in opposite ways,
            // and "nothing dialled in" reads identically to "nothing was listening" without it.
            "pipe" => new TransportChoice(create, LanguageConnectionTransport.Pipe,
                string.Equals(entry.PipeRole?.Trim(), "listen", StringComparison.OrdinalIgnoreCase)
                    ? $"{entry.PipeName?.Trim()} (listen)"
                    : $"{entry.PipeName?.Trim()} (connect)"),

            _ => null,
        };
    }

    /// <summary>
    /// How to start the server behind a pipe entry, or null when the entry connects to one that is
    /// already running.
    /// </summary>
    /// <remarks>
    /// <c>{pipe}</c> in the arguments is substituted with the agreed pipe name by the transport, which is
    /// what makes this usable at all: a server that is told which pipe to create cannot be given a fixed
    /// command line.
    ///
    /// <para>
    /// Note the consequence for reconnection: a launched transport reports <c>CanReconnect == false</c>,
    /// because HexIDE owns the process. If it dies, retrying the same pipe would wait for a server nobody
    /// is going to start.
    /// </para>
    /// </remarks>
    private static NamedPipeLaunch? LaunchFor(LanguageServerEntry entry) =>
        string.IsNullOrWhiteSpace(entry.Command)
            ? null
            : new NamedPipeLaunch(
                entry.Command!.Trim(),
                entry.Arguments?.Trim() ?? "",
                // Empty means "inherit", which for a server whose own file discovery is relative to its
                // working directory is rarely what anyone wants — but it is the caller's call, not ours.
                string.IsNullOrWhiteSpace(entry.WorkingDirectory) ? null : entry.WorkingDirectory.Trim());

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
                loggerFactory.CreateLogger<NamedPipeLspTransport>(),
                // The transport has always accepted this and nothing ever passed one, so from
                // configuration the pipe arm could only ever CONNECT — a server reached this way had to be
                // started by hand, outside the IDE, before the IDE would find it. A command makes the
                // entry self-contained; without one the behaviour is exactly as before.
                LaunchFor(entry)),

            _ => null,
        };
}
