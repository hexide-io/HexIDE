using System.Net;
using System.Net.WebSockets;

namespace HexIDE.TwinBasic;

/// <summary>Bounds for one message, including all receive operations used to assemble it.</summary>
public sealed record CompilerTransportLimits(int MaximumMessageBytes = 16 * 1024 * 1024,
    int MaximumReceiveOperations = 4096, TimeSpan? MessageTimeout = null);

public sealed record CompilerTransportMessage(WebSocketMessageType Type, byte[] Bytes);

/// <summary>
/// An owned, loopback-only connection. Interrupted I/O aborts the connection: callers must
/// establish a new session explicitly, never replay a possibly delivered compiler command.
/// One send and one receive may run concurrently. Disposal aborts pending operations.
/// </summary>
public sealed class CompilerWebSocketTransport : IDisposable
{
    private readonly WebSocket socket;
    private readonly CompilerTransportLimits limits;
    private readonly TimeSpan timeout;
    private readonly SemaphoreSlim sendGate = new(1);
    private readonly SemaphoreSlim receiveGate = new(1);
    private HttpMessageInvoker? http;
    private int disposed;

    internal CompilerWebSocketTransport(WebSocket socket, CompilerTransportLimits? limits = null)
    {
        this.limits = limits ?? new();
        timeout = this.limits.MessageTimeout ?? TimeSpan.FromSeconds(30);
        if (this.limits.MaximumMessageBytes < 1 || this.limits.MaximumReceiveOperations < 1 ||
            timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(limits));
        this.socket = socket;
    }

    /// <summary>
    /// Connect to an explicit ws endpoint with a loopback IP literal. Hostnames, proxies,
    /// redirects, credentials and query strings are not supported. The path may contain
    /// the compiler passkey and is deliberately excluded from connection errors.
    /// </summary>
    public static async Task<CompilerWebSocketTransport> ConnectAsync(Uri endpoint,
        CompilerTransportLimits? limits = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != "ws" ||
            !IPAddress.TryParse(endpoint.DnsSafeHost, out var address) || !IPAddress.IsLoopback(address) ||
            endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
            throw new ArgumentException("A plain WebSocket endpoint with a loopback IP literal is required.", nameof(endpoint));

        var client = new ClientWebSocket();
        CompilerWebSocketTransport transport;
        try { transport = new(client, limits); }
        catch { client.Dispose(); throw; }
        transport.http = new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false });
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(transport.timeout);
        try
        {
            await client.ConnectAsync(endpoint, transport.http, deadline.Token).ConfigureAwait(false);
            return transport;
        }
        catch (OperationCanceledException)
        {
            transport.Dispose();
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw new TimeoutException("Compiler WebSocket connection timed out.");
        }
        catch
        {
            transport.Dispose();
            // Do not retain an inner exception that could include the authenticated URL.
            throw new IOException("Compiler WebSocket connection failed.");
        }
    }

    public Task SendAsync(ReadOnlyMemory<byte> bytes, WebSocketMessageType type,
        CancellationToken cancellationToken = default) => SendCoreAsync(bytes, type, timeout, cancellationToken);

    internal Task SendWithTimeoutAsync(ReadOnlyMemory<byte> bytes, WebSocketMessageType type,
        TimeSpan messageTimeout, CancellationToken cancellationToken) => SendCoreAsync(bytes, type, messageTimeout, cancellationToken);

    private async Task SendCoreAsync(ReadOnlyMemory<byte> bytes, WebSocketMessageType type,
        TimeSpan messageTimeout, CancellationToken cancellationToken)
    {
        if (type is not (WebSocketMessageType.Binary or WebSocketMessageType.Text))
            throw new ArgumentOutOfRangeException(nameof(type));
        if (bytes.Length > limits.MaximumMessageBytes)
            throw new InvalidDataException("Compiler message exceeds the byte limit.");
        await sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(messageTimeout);
            try { await socket.SendAsync(bytes, type, true, deadline.Token).ConfigureAwait(false); }
            catch (Exception error) { throw Fail(error, cancellationToken, deadline.IsCancellationRequested); }
        }
        finally { sendGate.Release(); }
    }

    public Task<CompilerTransportMessage> ReceiveAsync(CancellationToken cancellationToken = default) =>
        ReceiveCoreAsync(false, cancellationToken);

    // Session pumps may remain idle. Start the assembly deadline after the first receive completes;
    // an incomplete WebSocket header cannot be distinguished from idle by this API. Ownership
    // cancellation/disposal still interrupts that first receive.
    internal Task<CompilerTransportMessage> ReceiveNextAsync(CancellationToken cancellationToken = default, TimeSpan? messageTimeout = null) =>
        ReceiveCoreAsync(true, cancellationToken, messageTimeout);

    private async Task<CompilerTransportMessage> ReceiveCoreAsync(bool allowIdle, CancellationToken cancellationToken, TimeSpan? messageTimeout = null)
    {
        await receiveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (!allowIdle) deadline.CancelAfter(timeout);
            using var message = new MemoryStream();
            var buffer = new byte[Math.Min(8192, limits.MaximumMessageBytes)];
            WebSocketMessageType? type = null;
            try
            {
                for (var operation = 0; operation < limits.MaximumReceiveOperations; operation++)
                {
                    // One extra byte detects overflow at the exact limit without accepting it.
                    var available = (int)Math.Min(buffer.Length, (long)limits.MaximumMessageBytes - message.Length + 1);
                    var result = await socket.ReceiveAsync(buffer.AsMemory(0, available), deadline.Token).ConfigureAwait(false);
                    if (allowIdle && operation == 0) deadline.CancelAfter(messageTimeout ?? timeout);
                    if (result.MessageType == WebSocketMessageType.Close)
                        throw new IOException("Compiler WebSocket closed before a message was available.");
                    if (type is not null && type != result.MessageType)
                        throw new InvalidDataException("Compiler message changed type during reassembly.");
                    type = result.MessageType;
                    if (message.Length + result.Count > limits.MaximumMessageBytes)
                        throw new InvalidDataException("Compiler message exceeds the byte limit.");
                    message.Write(buffer, 0, result.Count);
                    if (result.EndOfMessage) return new(result.MessageType, message.ToArray());
                }
                throw new InvalidDataException("Compiler message exceeds the receive-operation limit.");
            }
            catch (Exception error) { throw Fail(error, cancellationToken, deadline.IsCancellationRequested); }
        }
        finally { receiveGate.Release(); }
    }

    private Exception Fail(Exception error, CancellationToken cancellationToken, bool deadlineExpired)
    {
        Dispose();
        if (cancellationToken.IsCancellationRequested) return new OperationCanceledException(cancellationToken);
        return error switch
        {
            OperationCanceledException when deadlineExpired => new TimeoutException("Compiler WebSocket operation timed out."),
            InvalidDataException => error,
            _ => new IOException("Compiler WebSocket connection ended during an operation.")
        };
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        socket.Abort();
        socket.Dispose();
        http?.Dispose();
        // Gates stay usable until queued calls have observed disposal and released them.
    }
}
