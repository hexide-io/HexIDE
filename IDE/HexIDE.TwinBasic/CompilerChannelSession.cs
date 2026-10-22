using System.Net.WebSockets;
using System.Threading.Channels;

namespace HexIDE.TwinBasic;

internal enum CompilerChannel { Root, FileSystem, Language, Debugger }

internal sealed record CompilerChannelSessionLimits(int MaximumMessageBytes = 16 * 1024 * 1024,
    int MaximumQueuedMessages = 256, int MaximumQueuedBytes = 32 * 1024 * 1024,
    TimeSpan? ConnectionTimeout = null, TimeSpan? MessageTimeout = null);

internal sealed record CompilerChannelMessage(CompilerChannel Channel, WebSocketMessageType Type, ReadOnlyMemory<byte> Bytes);
internal sealed record CompilerSessionTermination(bool WasDisposed, string? Error);

/// <summary>
/// Owns all four compiler sockets. Messages remain opaque and ordered within each channel;
/// this is not a request dispatcher, language client or debugger adapter. Failure is terminal.
/// </summary>
internal sealed class CompilerChannelSession : IDisposable, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly CompilerRootSession root;
    private readonly Dictionary<CompilerChannel, CompilerWebSocketTransport> transports;
    private readonly CompilerChannelSessionLimits limits;
    private readonly Channel<CompilerChannelMessage> messages;
    private readonly TaskCompletionSource<CompilerSessionTermination> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task[] pumps = [];
    private long queuedBytes;
    private bool ended;
    private IOException? failure;

    /// <summary>Initialization notifications precede the ongoing messages returned by ReceiveAsync.</summary>
    public CompilerRootConnectionInfo Connection => root.Connection;
    /// <summary>Terminal state, without faulting an unobserved background task. Async disposal joins the pumps.</summary>
    public Task<CompilerSessionTermination> Completion => completion.Task;

    private CompilerChannelSession(CompilerRootSession root,
        Dictionary<CompilerChannel, CompilerWebSocketTransport> transports, CompilerChannelSessionLimits limits)
    {
        this.root = root;
        this.transports = transports;
        this.limits = limits;
        messages = Channel.CreateBounded<CompilerChannelMessage>(new BoundedChannelOptions(limits.MaximumQueuedMessages)
        {
            FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false
        });
    }

    // A project must already be loaded and acknowledged by RequestAllProjectsLoaded.
    // Keep this component internal until production project preparation exists: attaching to
    // a merely initialized BETA1011 root can crash the native compiler.
    internal static Task<CompilerChannelSession> AttachToPreparedProjectAsync(Uri rootEndpoint,
        CompilerRootSession preparedRoot, CompilerChannelSessionLimits? limits = null,
        CancellationToken cancellationToken = default) =>
        AttachAsync(rootEndpoint, preparedRoot, limits ?? new(), CompilerWebSocketTransport.ConnectAsync, cancellationToken);

    // Ownership of root transfers even when attachment fails. The injectable connector lets tests
    // exercise partial acquisition without starting vendor processes or binding fixed ports.
    internal static async Task<CompilerChannelSession> AttachAsync(Uri rootEndpoint, CompilerRootSession root,
        CompilerChannelSessionLimits limits,
        Func<Uri, CompilerTransportLimits?, CancellationToken, Task<CompilerWebSocketTransport>> connect,
        CancellationToken cancellationToken)
    {
        var sockets = new Dictionary<CompilerChannel, CompilerWebSocketTransport> { [CompilerChannel.Root] = root.Transport };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            Validate(rootEndpoint, limits);
            deadline.CancelAfter(limits.ConnectionTimeout ?? TimeSpan.FromSeconds(30));
            var info = root.Connection;
            foreach (var (channel, name, port) in new[]
            {
                (CompilerChannel.FileSystem, "fs", info.FileSystemPort),
                (CompilerChannel.Language, "language", info.LanguagePort),
                (CompilerChannel.Debugger, "debugger", info.DebuggerPort)
            })
            {
                deadline.Token.ThrowIfCancellationRequested();
                var endpoint = new UriBuilder(rootEndpoint)
                {
                    Port = port, Path = rootEndpoint.AbsolutePath[..^4] + name
                }.Uri;
                sockets.Add(channel, await connect(endpoint,
                    new(MaximumMessageBytes: limits.MaximumMessageBytes,
                        MessageTimeout: limits.ConnectionTimeout ?? TimeSpan.FromSeconds(30)),
                    deadline.Token).ConfigureAwait(false));
            }
            deadline.Token.ThrowIfCancellationRequested();
            var session = new CompilerChannelSession(root, sockets, limits);
            session.pumps = sockets.Select(pair => session.PumpAsync(pair.Key, pair.Value)).ToArray();
            return session;
        }
        catch (Exception error)
        {
            foreach (var socket in sockets.Values) socket.Dispose();
            root.Dispose();
            if (error is OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
                throw new TimeoutException("Compiler channel attachment timed out.");
            }
            throw;
        }
    }

    private static void Validate(Uri endpoint, CompilerChannelSessionLimits limits)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || !endpoint.AbsolutePath.EndsWith("/root", StringComparison.Ordinal))
            throw new ArgumentException("An explicit compiler root endpoint is required.", nameof(endpoint));
        if (limits.MaximumMessageBytes < 32 || limits.MaximumQueuedMessages < 1 || limits.MaximumQueuedBytes < 1 ||
            !ValidTimeout(limits.ConnectionTimeout) || !ValidTimeout(limits.MessageTimeout))
            throw new ArgumentOutOfRangeException(nameof(limits));
    }

    private static bool ValidTimeout(TimeSpan? value) => value is null ||
        (value > TimeSpan.Zero && value.Value.TotalMilliseconds <= uint.MaxValue - 1);

    private async Task PumpAsync(CompilerChannel channel, CompilerWebSocketTransport transport)
    {
        try
        {
            while (true)
            {
                var message = await transport.ReceiveNextAsync(messageTimeout:
                    limits.MessageTimeout ?? TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                if (message.Bytes.Length > limits.MaximumMessageBytes)
                    throw new InvalidDataException("Compiler message exceeds the session byte limit.");
                lock (gate)
                {
                    if (ended) return;
                    if (message.Bytes.Length > limits.MaximumQueuedBytes - queuedBytes ||
                        !messages.Writer.TryWrite(new(channel, message.Type, message.Bytes)))
                        throw new InvalidDataException("Compiler session incoming queue exceeded its limit.");
                    queuedBytes += message.Bytes.Length;
                }
            }
        }
        catch (Exception error)
        {
            // Peer payloads and authenticated endpoints never become diagnostics here.
            var reason = error is InvalidDataException ? "message or queue limit exceeded" :
                error is TimeoutException ? "message assembly timed out" : "connection ended";
            End(new IOException($"Compiler {channel} channel failed: {reason}."));
        }
    }

    public async Task<CompilerChannelMessage> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                ThrowIfEnded();
                if (messages.Reader.TryRead(out var message))
                {
                    queuedBytes -= message.Bytes.Length;
                    return message;
                }
            }
            await messages.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Keep the send buffer unchanged until completion. Cancellation after dispatch is terminal.</summary>
    public async Task SendAsync(CompilerChannel channel, ReadOnlyMemory<byte> bytes, WebSocketMessageType type,
        CancellationToken cancellationToken = default)
    {
        if (!transports.TryGetValue(channel, out var transport)) throw new ArgumentOutOfRangeException(nameof(channel));
        if (type is not (WebSocketMessageType.Binary or WebSocketMessageType.Text)) throw new ArgumentOutOfRangeException(nameof(type));
        if (bytes.Length > limits.MaximumMessageBytes) throw new InvalidDataException("Compiler message exceeds the byte limit.");
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate) ThrowIfEnded();
        try { await transport.SendWithTimeoutAsync(bytes, type,
            limits.MessageTimeout ?? TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false); }
        catch
        {
            End(new IOException($"Compiler {channel} channel send failed."));
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw new IOException($"Compiler {channel} channel send failed.");
        }
    }

    private void ThrowIfEnded()
    {
        if (failure is not null) throw new IOException(failure.Message);
        ObjectDisposedException.ThrowIf(ended, this);
    }

    private void End(IOException? error)
    {
        lock (gate)
        {
            if (ended) return;
            ended = true;
            failure = error;
            while (messages.Reader.TryRead(out _)) { }
            queuedBytes = 0;
            messages.Writer.TryComplete();
        }
        foreach (var transport in transports.Values) transport.Dispose();
        root.Dispose();
        completion.TrySetResult(new(error is null, error?.Message));
    }

    public void Dispose() => End(null);
    public async ValueTask DisposeAsync()
    {
        Dispose();
        await Task.WhenAll(pumps).ConfigureAwait(false);
        await completion.Task.ConfigureAwait(false);
    }
}
