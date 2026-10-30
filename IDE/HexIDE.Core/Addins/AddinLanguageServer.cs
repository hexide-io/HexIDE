using System.Text.Json;
using HexIDE.Lsp;

namespace HexIDE.Addins;

/// <summary>
/// Answers a method a language server calls on the client, which an add-in claimed: the method's parameters as the
/// server sent them, and the result to send back. For a notification the result is not sent.
/// </summary>
/// <remarks>
/// Called on a thread-pool thread, not the UI thread, and possibly while another of the add-in's requests to the same
/// server is waiting for its answer. One that throws is answered with an error carrying its message only.
/// </remarks>
public delegate Task<JsonElement?> AddinServerMethod(JsonElement? parameters, CancellationToken cancellationToken);

/// <summary>
/// A language server an add-in contributes (#457): what an <c>lsp-servers.json</c> entry holds, and what a file cannot
/// hold — a gate before the server starts, and the methods of its own the add-in and the server exchange.
/// </summary>
/// <remarks>
/// <para>
/// <b>A registration, not a client.</b> The connection is HexIDE's own, so the Protocol Inspector captures it, the
/// Language Servers window lists it and reports its problems, and it is restarted and failed as any other is. The add-in
/// talks to its server through the <see cref="IAddinServerConnection"/> it is given, which passes its messages through
/// uninterpreted.
/// </para>
/// </remarks>
/// <param name="Entry">
/// The server, exactly as an entry of <c>lsp-servers.json</c> describes one, and checked as one is. An entry of the
/// user's file with the same id wins: the contribution is not added, and a configuration problem says so.
/// </param>
public sealed record AddinLanguageServer(LanguageServerEntry Entry)
{
    /// <summary>
    /// Runs before the server's process starts, every time it starts — the first document it claims, and a restart after
    /// the workspace moves. The start waits for it, with the start's own cancellation. One that throws fails the start as a
    /// launch that fails does, with its message on the server's row.
    /// </summary>
    public Func<CancellationToken, Task>? BeforeLaunch { get; init; }

    /// <summary>
    /// Runs after each start's <c>initialize</c> handshake, once <c>initialized</c> is sent: the first start, a restart
    /// after the workspace moves, and a reconnect after the server's process exits. Each is a new process, so a handshake
    /// of the add-in's own with its server belongs here. Called on a thread-pool thread with the connection it is about;
    /// nothing waits for it, and the token is cancelled when the server is taken out. One that throws is logged.
    /// </summary>
    public Func<IAddinServerConnection, CancellationToken, Task>? AfterStart { get; init; }

    /// <summary>
    /// The prefix of every method the add-in sends and claims, such as <c>"myserver/"</c>: what makes them the add-in's
    /// own. It must end with <c>/</c>, and it cannot be a namespace the Language Server Protocol itself uses. Null when
    /// the add-in exchanges no messages of its own with its server.
    /// </summary>
    public string? MessagePrefix { get; init; }

    /// <summary>
    /// The methods under <see cref="MessagePrefix"/> the server calls on the client, each with what answers it. A method
    /// the server calls that nothing claims gets <em>Method not found</em>, as any unknown method does.
    /// </summary>
    public IReadOnlyDictionary<string, AddinServerMethod> ServerMethods { get; init; } = new Dictionary<string, AddinServerMethod>();
}

/// <summary>
/// The connection to a language server an add-in contributed: what sends the add-in's own messages to it. Disposing it
/// stops the server, if it is running, and removes it.
/// </summary>
public interface IAddinServerConnection : IDisposable
{
    /// <summary>The id the server was registered under.</summary>
    string Id { get; }

    /// <summary>Whether the server is running now. It starts on the first document it claims, not when it is registered.</summary>
    bool IsRunning { get; }

    /// <summary>
    /// Sends a request under the registration's <see cref="AddinLanguageServer.MessagePrefix"/>, and returns the server's
    /// result, as it sent it.
    /// </summary>
    /// <param name="method">The method, under the registration's prefix.</param>
    /// <param name="parameters">The parameters, sent as they are.</param>
    /// <param name="timeout">
    /// How long to wait for the answer. Null is HexIDE's own request timeout, as its own requests have.
    /// <see cref="Timeout.InfiniteTimeSpan"/> waits until the server answers or the token is cancelled, which is what a
    /// request that waits on a person needs, such as one that runs a program that shows a dialog.
    /// </param>
    /// <param name="cancellationToken">Cancels the request, and tells the server so.</param>
    /// <exception cref="ArgumentException">The method is not under the registration's prefix.</exception>
    /// <exception cref="InvalidOperationException">The server is not running.</exception>
    /// <exception cref="TimeoutException">The server did not answer within the timeout.</exception>
    Task<JsonElement?> RequestAsync(
        string method, JsonElement? parameters = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

    /// <summary>Sends a notification under the registration's <see cref="AddinLanguageServer.MessagePrefix"/>.</summary>
    /// <exception cref="ArgumentException">The method is not under the registration's prefix.</exception>
    /// <exception cref="InvalidOperationException">The server is not running.</exception>
    Task NotifyAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default);
}
