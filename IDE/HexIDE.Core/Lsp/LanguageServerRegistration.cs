namespace HexIDE.Lsp;

/// <summary>
/// One language server the IDE knows how to talk to.
///
/// <para>
/// The client is supplied as a factory rather than an instance because servers start lazily — on the first
/// document of a language they claim — and because each owns its own transport. A single global transport
/// choice cannot describe two servers that communicate differently, and how a server is reached is a
/// property of that server rather than of the IDE.
/// </para>
/// </summary>
/// <param name="Id">
/// Stable across restarts, and the thing anything else refers to this server by. Without one, a server can
/// only be identified by its position in a list — which cannot be named in a setting, quoted in a message,
/// or remembered between sessions. That gap is why the pick-one features could not be configured at all in
/// at least one widely-deployed editor until an identity was retrofitted.
/// </param>
/// <param name="DisplayName">For humans, in a connections view or a log line.</param>
/// <param name="Extensions">
/// The file extensions this server claims, leading dot included. Routing keys on these rather than on a
/// language name, so two servers may claim one extension without having to agree about what it is called.
/// A document is offered to EVERY claimant.
/// </param>
/// <param name="LanguageId">
/// What this server should be told a document is. Per-server rather than global: each server has its own
/// connection, and nothing in the protocol requires two connections be told the same thing — so one
/// server's <c>python</c> and another's <c>python3</c> can both be right about the same file.
///
/// <para>
/// It doubles as the claim for HexIDE's own <c>vb6://</c> documents, which carry no extension to match on.
/// </para>
/// </param>
/// <param name="CreateClient">Builds the single-server client, transport and all. Called at most once.</param>
/// <param name="Priority">
/// Higher wins where exactly one server must be chosen — formatting and rename, which cannot merge two
/// answers. Equal priorities fall back to registration order, which is deterministic but accidental: it
/// varies with discovery, and discovery changes when a server is installed or removed.
/// </param>
public sealed record LanguageServerRegistration(
    string Id,
    string DisplayName,
    IReadOnlyList<string> Extensions,
    string LanguageId,
    Func<ILspClient> CreateClient,
    int Priority = 0);
