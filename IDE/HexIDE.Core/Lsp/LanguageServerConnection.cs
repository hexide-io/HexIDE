using System.Text.Json;

namespace HexIDE.Lsp;

/// <summary>What kind of external tool a connection speaks to.</summary>
/// <remarks>
/// Present from the start although only one value is implemented, because the surface a user interface
/// binds to should not need rebuilding to gain a second row type. A debug adapter has the same properties
/// worth showing — what it is, whether it is up, what it serves — even though its <em>client</em> is a
/// different protocol entirely and shares no interface with this one.
/// </remarks>
public enum LanguageConnectionKind
{
    LanguageServer,
    DebugAdapter,
}

/// <summary>Where a connection currently is.</summary>
public enum LanguageConnectionState
{
    /// <summary>Registered, and deliberately not started: nothing of its language has been opened yet.</summary>
    NotStarted,

    /// <summary>Starting, or connected but not through the handshake.</summary>
    Starting,

    /// <summary>Up and answering.</summary>
    Running,

    /// <summary>Tried and did not come up. Distinct from <see cref="NotStarted"/>, which is the point.</summary>
    Failed,

    /// <summary>Stopped, either deliberately or because the far end went away.</summary>
    Stopped,
}

/// <summary>
/// An inspectable view of one external language-service connection.
///
/// <para>
/// This exists so that the question people actually bring to a language service — <em>why is this file
/// getting no help?</em> — has an answer. Without it, a server that is quiet because nothing triggered it is
/// indistinguishable from one that is missing, misconfigured, or crashed, and those need different
/// responses from whoever is looking.
/// </para>
/// </summary>
/// <param name="Id">Stable across restarts. What a setting names, and what a message can refer to.</param>
/// <param name="DisplayName">For humans.</param>
/// <param name="Kind">Which protocol this connection speaks.</param>
/// <param name="State">Where it is now.</param>
/// <param name="LanguageIds">The languages it claims.</param>
/// <param name="Capabilities">
/// What it advertised, <b>as received</b>. Deliberately not reduced to a summary: any summary invented now
/// will be wrong for a server not yet met, and the raw answer is the only honest response to "why is hover
/// unavailable in this file". Null when nothing has been advertised yet, or when it could not be read.
/// </param>
public sealed record LanguageServerConnection(
    string Id,
    string DisplayName,
    LanguageConnectionKind Kind,
    LanguageConnectionState State,
    IReadOnlyList<string> LanguageIds,
    JsonElement? Capabilities);

/// <summary>
/// The inspectable set of language-service connections.
///
/// <para>
/// Separate from <see cref="ILspClient"/> on purpose. That interface answers "give me hover for this
/// document" and hides which server replied; this one answers "what is attached, and is it working". A
/// user interface binds to this; the editor binds to that; one object may implement both.
/// </para>
/// </summary>
public interface ILanguageConnectionRegistry
{
    /// <summary>Every registered connection, including those deliberately not started.</summary>
    IReadOnlyList<LanguageServerConnection> Connections { get; }

    /// <summary>Raised when any connection's state or advertised capabilities change.</summary>
    event EventHandler? ConnectionsChanged;
}
