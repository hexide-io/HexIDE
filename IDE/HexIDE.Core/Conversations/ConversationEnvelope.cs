namespace HexIDE.Conversations;

/// <summary>Which way a thing travelled.</summary>
/// <remarks>
/// Named for the IDE's point of view rather than the protocol's, because a reader of a capture is sitting
/// in the IDE. "Client to server" requires knowing which end you are, and half the entries here are not
/// protocol messages at all — a process exit has no client and no server.
/// </remarks>
public enum ConversationDirection
{
    /// <summary>Something this IDE did: a request, a notification, a decision not to send one.</summary>
    Sent,

    /// <summary>Something that arrived: a reply, a notification, a line of standard error.</summary>
    Received,

    /// <summary>Neither. A process starting, a transport reporting what it cannot observe.</summary>
    Local,
}

/// <summary>What kind of thing an entry records.</summary>
/// <remarks>
/// <b>Five of these are not messages, and that is the point.</b> A conversation with a language server is
/// not only the frames: it is also the process that carried them, what the client decided not to ask, what
/// the server offered that nothing took up, and what this transport is structurally unable to show. Every
/// one of those is invisible today, and each is the answer to a question a wire trace alone cannot settle.
/// </remarks>
public enum ConversationEntryKind
{
    /// <summary>A request, awaiting a response that may or may not arrive.</summary>
    Request,

    /// <summary>A response to a request, successful.</summary>
    Response,

    /// <summary>A response carrying an error object.</summary>
    ErrorResponse,

    /// <summary>A notification, which by definition is never answered.</summary>
    Notification,

    /// <summary>
    /// A request the client declined to make, because the server advertised no such capability.
    /// </summary>
    /// <remarks>
    /// It never reached the wire, so no tap could see it, and its absence is indistinguishable from a
    /// broken feature. Naming the capability turns "nothing happened" into "we did not ask, and here is
    /// why", which is the single most common question a language feature raises.
    /// </remarks>
    NeverSent,

    /// <summary>
    /// A capability the server advertised that this client does not consume.
    /// </summary>
    /// <remarks>
    /// The inverse of <see cref="NeverSent"/>, and free: both sides are known at the handshake. For someone
    /// writing a server this is the more useful half, because it is a list of what they have offered that
    /// this IDE is not yet taking.
    /// </remarks>
    Unconsumed,

    /// <summary>The process starting, stopping, or reporting an exit code.</summary>
    Lifecycle,

    /// <summary>
    /// A line the server wrote to standard error.
    /// </summary>
    /// <remarks>
    /// A server speaking over standard output may write nothing else there, so this is its only channel for
    /// anything unstructured — a crash, a stack, a complaint about its own configuration. Measured against
    /// this project's own fixture servers: when they genuinely failed, the human-readable cause appeared
    /// here and in a protocol error, and nowhere in the protocol's user-facing message channels.
    /// </remarks>
    StandardError,

    /// <summary>
    /// Something the capture itself has to say — most often what this transport cannot show.
    /// </summary>
    /// <remarks>
    /// "This connection produced no lifecycle events" and "lifecycle for this transport cannot be observed"
    /// look identical if you print neither, and the first reading is the wrong one. A transport HexIDE did
    /// not start has no exit code to report and no standard error to read, and saying so once costs almost
    /// nothing.
    /// </remarks>
    Note,
}

/// <summary>How a request ended, once it has ended.</summary>
public enum ConversationOutcome
{
    /// <summary>Not applicable, or not yet known.</summary>
    None,

    /// <summary>Answered successfully.</summary>
    Answered,

    /// <summary>Answered with an error object.</summary>
    Failed,

    /// <summary>Cancelled by this client before an answer arrived.</summary>
    Cancelled,

    /// <summary>The connection went away with the request outstanding.</summary>
    Abandoned,
}

/// <summary>
/// Everything recorded about one moment in a conversation, except what was actually said.
/// </summary>
/// <remarks>
/// <b>Carries no content from the user's documents, and that is what makes it affordable to record
/// always.</b> A language server conversation is a time series of somebody's source; an envelope is a few
/// dozen bytes of metadata about it. Splitting the two is the whole reason capture can be on by default
/// while the thing that would disclose anything is off.
///
/// <para>
/// <paramref name="Sequence"/> rather than <paramref name="Timestamp"/> is what orders a merged view. Two
/// connections are recorded independently and a clock has finite resolution, so timestamps tie; a single
/// allocator gives a total order that survives being merged and does not depend on how good the clock is.
/// </para>
/// </remarks>
/// <param name="Sequence">Monotonic across every connection, allocated once, never reused.</param>
/// <param name="ConnectionId">Which server. Stable across a restart of that server's process.</param>
/// <param name="Timestamp">When, for display. Never for ordering.</param>
/// <param name="Direction">Whose doing.</param>
/// <param name="Kind">What sort of thing.</param>
/// <param name="Method">The protocol method, or null where the kind has none.</param>
/// <param name="CorrelationId">
/// The JSON-RPC id, as text. Text because the protocol allows a string or a number and a capture must not
/// normalise one into the other: two servers numbering their requests differently is a real thing to see.
/// </param>
/// <param name="SizeBytes">The size of the body this envelope describes, whether or not it was retained.</param>
/// <param name="Outcome">How a request ended. <see cref="ConversationOutcome.None"/> for everything else.</param>
/// <param name="Elapsed">
/// Request to response. Null until the response arrives, and null forever if it does not — which is itself
/// worth seeing, since a request that never came back is invisible in a log that only records replies.
/// </param>
/// <param name="Detail">
/// A short human-readable note: the capability a <see cref="ConversationEntryKind.NeverSent"/> entry was
/// refused for, an exit code, a line of standard error. Never document content.
/// </param>
public sealed record ConversationEnvelope(
    long Sequence,
    string ConnectionId,
    DateTimeOffset Timestamp,
    ConversationDirection Direction,
    ConversationEntryKind Kind,
    string? Method,
    string? CorrelationId,
    int SizeBytes,
    ConversationOutcome Outcome = ConversationOutcome.None,
    TimeSpan? Elapsed = null,
    string? Detail = null);
