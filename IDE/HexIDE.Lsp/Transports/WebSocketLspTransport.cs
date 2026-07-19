using System.Net.WebSockets;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace HexIDE.Lsp;

/// <summary>
/// Network transport: speaks LSP as JSON-RPC over a WebSocket (WebSocket framing — one JSON-RPC
/// message per WebSocket message, no Content-Length headers). Used for the browser-online topology
/// and for remote desktop/mobile; on the only currently-runnable head (desktop) it is opt-in via the
/// <c>HEXIDE_LSP_WS_URL</c> env var or the <c>LspWebSocketUrl</c> setting.
/// </summary>
/// <remarks>
/// The WebSocket can drop without this transport observing it directly — StreamJsonRpc owns the
/// receive loop — so a drop is surfaced to the client via <c>JsonRpc.Disconnected</c>, not via
/// <see cref="Closed"/>. <see cref="CanReconnect"/> is therefore true, and the client re-dials.
/// </remarks>
public sealed class WebSocketLspTransport : ILspTransport
{
    private readonly string _endpoint;
    private readonly ILogger<WebSocketLspTransport> _logger;
    private ClientWebSocket? _socket;

    public WebSocketLspTransport(string endpoint, ILogger<WebSocketLspTransport> logger)
    {
        _endpoint = endpoint;
        _logger = logger;
    }

    public bool IsAlive => _socket is { State: WebSocketState.Open };

    public bool CanReconnect => true;

    // A WebSocket drop is detected via JsonRpc.Disconnected (StreamJsonRpc owns the receive loop),
    // so this transport never raises Closed itself — the accessors are intentionally no-ops.
    public event EventHandler? Closed { add { } remove { } }

    public async Task<IJsonRpcMessageHandler?> ConnectAsync(IJsonRpcMessageFormatter formatter, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(_endpoint, UriKind.Absolute, out var uri))
        {
            // A malformed endpoint degrades to "LSP disabled" rather than crashing IDE startup.
            _logger.LogWarning("Invalid WebSocket LSP endpoint '{Endpoint}' — LSP unavailable.", _endpoint);
            return null;
        }

        // Drop any previous socket (reconnect) so the server side observes the close and frees up,
        // rather than leaking a half-open connection.
        var previous = Interlocked.Exchange(ref _socket, null);
        if (previous is not null)
        {
            try { previous.Abort(); } catch { /* ignore */ }
            previous.Dispose();
        }

        var socket = new ClientWebSocket();
        try
        {
            _logger.LogInformation("Connecting to VB LSP server over WebSocket: {Uri}", uri);
            // Bound the connect so a wedged/unreachable endpoint fails fast (and the reconnect loop
            // can retry) instead of hanging on the ~2-minute default HTTP connect timeout.
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(TimeSpan.FromSeconds(10));
            await socket.ConnectAsync(uri, connectCts.Token);
        }
        catch (Exception ex)
        {
            // Graceful absence — the IDE runs with LSP features disabled (or, on reconnect, retries).
            _logger.LogWarning(ex, "WebSocket LSP connect failed ({Uri}) — LSP unavailable.", _endpoint);
            socket.Dispose();
            return null;
        }

        _socket = socket;
        // 3-arg ctor is required: the 1-/2-arg overloads default to the Newtonsoft formatter, which
        // would bypass the shared AOT-safe SystemTextJsonFormatter + LspJsonContext.
        return new WebSocketMessageHandler(socket, formatter);
    }

    public async ValueTask DisposeAsync()
    {
        var socket = _socket;
        _socket = null;
        if (socket is null) return;

        // StreamJsonRpc disposes the message handler but NOT the WebSocket itself, so close it here.
        try
        {
            if (socket.State == WebSocketState.Open)
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        }
        catch { /* best effort */ }

        socket.Abort();
        socket.Dispose();
    }
}
