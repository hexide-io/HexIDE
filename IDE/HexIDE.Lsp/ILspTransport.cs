using StreamJsonRpc;

namespace HexIDE.Lsp;

/// <summary>What kind of thing a transport is reporting about its process.</summary>
public enum TransportNoticeKind
{
    /// <summary>A line the server wrote to standard error.</summary>
    StandardError,

    /// <summary>The process starting, stopping, or reporting an exit code.</summary>
    Lifecycle,
}

/// <summary>Something the server's process said or did that is not a protocol message.</summary>
/// <param name="Kind">Which of the two channels this came from.</param>
/// <param name="Text">Verbatim. A stderr line as written, or a lifecycle fact in plain words.</param>
public sealed record TransportNotice(TransportNoticeKind Kind, string Text);

/// <summary>
/// A platform-specific connection to the LSP server. The transport owns the underlying channel
/// (e.g. a child process's stdio, a WebSocket, or an in-memory pipe) and produces the
/// <see cref="IJsonRpcMessageHandler"/> that <see cref="VBLspClient"/> binds JSON-RPC to.
/// The transport — not the client — chooses the wire framing (Content-Length for byte streams,
/// WebSocket framing for sockets), which is why it returns a fully-formed message handler rather
/// than raw streams.
/// </summary>
public interface ILspTransport : IAsyncDisposable
{
    /// <summary>True while the underlying channel is connected and usable.</summary>
    bool IsAlive { get; }

    /// <summary>
    /// True if the client should auto-reconnect this transport after an unexpected drop (network
    /// transports such as WebSocket); false for one-shot transports (a spawned subprocess), which
    /// preserves the original "server crash = LSP disabled until restart" desktop behaviour.
    /// </summary>
    bool CanReconnect { get; }

    /// <summary>
    /// Why the last <see cref="ConnectAsync"/> returned null, in the transport's own words — or null if
    /// it has not failed.
    ///
    /// <para>
    /// A string rather than a structured result, deliberately. All three transports already <em>compose</em>
    /// this sentence for their log line and then throw it away; the text is the thing that was lost, and
    /// modelling it would mean inventing a taxonomy for failures that arrive as arbitrary
    /// <see cref="Exception"/>s from three unrelated stacks.
    /// </para>
    ///
    /// <para>
    /// It exists because <see cref="ConnectAsync"/> returning <c>null</c> is the transport's entire
    /// vocabulary for failure: a command that does not exist, a pipe nothing is listening on, and a
    /// malformed URL are one value by the time anything above can look.
    /// </para>
    /// </summary>
    string? LastFailure { get; }

    /// <summary>
    /// What a record of this connection structurally cannot contain, in a sentence, or null when it can
    /// contain everything.
    /// </summary>
    /// <remarks>
    /// <b>Because "nothing was reported" and "nothing could be reported" read identically, and the first
    /// reading is the wrong one.</b> A conversation captured over a transport HexIDE did not start has no
    /// exit code to show and no standard error to read, and a reader who is not told that will conclude the
    /// server exited cleanly and said nothing on its way out.
    ///
    /// <para>
    /// The transport answers rather than the client, because the client cannot know: a named pipe HexIDE
    /// launched and a named pipe it merely dialled are the same type with the same interface, and only one
    /// of them owns a process.
    /// </para>
    ///
    /// <para>
    /// Prose rather than a set of flags, for the same reason <see cref="LastFailure"/> is. Anything read by
    /// a person, once, at the top of a record does not need a taxonomy — and inventing one now would fix
    /// the shape of an answer before a third kind of gap has been met.
    /// </para>
    /// </remarks>
    string? Unobservable { get; }

    /// <summary>Raised when the underlying channel closes unexpectedly (e.g. the server process exits).</summary>
    event EventHandler? Closed;

    /// <summary>
    /// Raised for something the process said or did that is not a protocol message.
    /// </summary>
    /// <remarks>
    /// <b>An event rather than the transport writing to the record itself.</b> The transport does not know
    /// which connection it is — a named pipe HexIDE launched and one it merely dialled are the same type —
    /// and the client is the thing that holds the connection id the record attributes entries to. It is
    /// the shape <see cref="Closed"/> already set.
    ///
    /// <para>
    /// <b>Why it exists at all.</b> A server that fails badly does not explain itself in the protocol.
    /// Standard error is its only channel for a crash, because a server speaking over standard output may
    /// write nothing else there — measured against this project's own fixture servers, the human-readable
    /// cause appeared there and in a protocol error reply, and nowhere in the protocol's user-facing
    /// message channels. Without this the record shows a conversation that simply stops.
    /// </para>
    ///
    /// <para>
    /// A transport that owns no process never raises it, and says so through
    /// <see cref="Unobservable"/> instead.
    /// </para>
    /// </remarks>
    event EventHandler<TransportNotice>? Notice;

    /// <summary>
    /// Establishes the connection and returns the JSON-RPC message handler to bind, or
    /// <c>null</c> when no server/endpoint is available (graceful absence — the IDE then runs
    /// with LSP features disabled). The supplied <paramref name="formatter"/> carries the
    /// AOT-safe serialization configuration and is shared across all transports.
    /// </summary>
    Task<IJsonRpcMessageHandler?> ConnectAsync(IJsonRpcMessageFormatter formatter, CancellationToken cancellationToken = default);
}
