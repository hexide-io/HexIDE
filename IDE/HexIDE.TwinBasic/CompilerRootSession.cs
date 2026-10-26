using System.Collections.Immutable;
using System.Net.WebSockets;

namespace HexIDE.TwinBasic;

public sealed record CompilerRootHandshakeLimits(int MaximumMessageBytes = 16 * 1024 * 1024,
    int MaximumReceivedBytes = 32 * 1024 * 1024, int MaximumNotifications = 128, TimeSpan? Timeout = null);

/// <summary>ServerId is an opaque instance identity, not a compiler version or compatibility declaration.</summary>
public sealed record CompilerRootConnectionInfo(int ProcessId, int FileSystemPort, int DebuggerPort,
    int LanguagePort, int FormImagePort, int LiveBackupPort, string ServerId,
    ImmutableArray<CompilerControlMessage> InitializationNotifications);

/// <summary>
/// Owns a root socket initialized with the observed BETA1011 Community Edition sequence.
/// The caller supplies an endpoint for a fresh compiler. This class does not launch a process,
/// load a project, attach other channels or interpret reverse requests. Dispose closes only its socket.
/// </summary>
public sealed class CompilerRootSession : IDisposable
{
    internal ICompilerSessionCodec Codec { get; }
    private readonly CompilerWebSocketTransport transport;
    internal CompilerWebSocketTransport Transport => transport;
    private int projectPreparationClaimed;
    internal bool TryClaimProjectPreparation() => Interlocked.CompareExchange(ref projectPreparationClaimed, 1, 0) == 0;
    private int nextRequestId = 2; // Initialization takes ids 0 and 1.
    internal int TakeRequestId() => Interlocked.Increment(ref nextRequestId) - 1;

    /// <summary>
    /// Asks the compiler to end, which it does without answering. Wait for that with
    /// <see cref="CompilerProcess.WaitForExitAsync"/>; a compiler that never ends is still ended when its process is disposed.
    /// </summary>
    public Task RequestShutdownAsync(CancellationToken cancellationToken = default) =>
        transport.SendAsync(Codec.EncodeProjectRequest(TakeRequestId(), new CompilerShutdown(), 64),
            WebSocketMessageType.Binary, cancellationToken);
    public CompilerRootConnectionInfo Connection { get; }

    private CompilerRootSession(CompilerWebSocketTransport transport, CompilerRootConnectionInfo connection, ICompilerSessionCodec codec)
    {
        this.transport = transport;
        Connection = connection;
        Codec = codec;
    }

    public static async Task<CompilerRootSession> ConnectAsync(Uri endpoint, CompilerRootInitialization initialization,
        CompilerRootHandshakeLimits? limits = null, CancellationToken cancellationToken = default, ICompilerSessionCodec? codec = null)
    {
        codec ??= new CompilerWireCodec();
        var settings = Prepare(initialization, limits, codec);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(settings.Timeout);
        CompilerWebSocketTransport? transport = null;
        try
        {
            transport = await CompilerWebSocketTransport.ConnectAsync(endpoint,
                new(MaximumMessageBytes: settings.Limits.MaximumMessageBytes, MessageTimeout: settings.Timeout),
                deadline.Token).ConfigureAwait(false);
            return await InitializeCoreAsync(transport, settings, codec, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            transport?.Dispose();
            throw new TimeoutException("Compiler root initialization timed out.");
        }
        catch (OperationCanceledException)
        {
            transport?.Dispose();
            throw new OperationCanceledException(cancellationToken);
        }
        catch
        {
            transport?.Dispose();
            throw;
        }
    }

    // Own the supplied transport, including on validation failure. Used by offline socket tests.
    internal static async Task<CompilerRootSession> InitializeAsync(CompilerWebSocketTransport transport,
        CompilerRootInitialization initialization, CompilerRootHandshakeLimits? limits = null,
        CancellationToken cancellationToken = default, ICompilerSessionCodec? codec = null)
    {
        try
        {
            codec ??= new CompilerWireCodec();
            var settings = Prepare(initialization, limits, codec);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(settings.Timeout);
            try { return await InitializeCoreAsync(transport, settings, codec, deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { throw new TimeoutException("Compiler root initialization timed out."); }
            catch (OperationCanceledException) { throw new OperationCanceledException(cancellationToken); }
        }
        catch { transport.Dispose(); throw; }
    }

    private sealed record Prepared(CompilerRootHandshakeLimits Limits, TimeSpan Timeout, CompilerInitializationRequest InitializePayload);

    private static Prepared Prepare(CompilerRootInitialization initialization, CompilerRootHandshakeLimits? limits, ICompilerSessionCodec codec)
    {
        ArgumentNullException.ThrowIfNull(initialization);
        ArgumentNullException.ThrowIfNull(initialization.PackagePaths);
        limits ??= new();
        var timeout = limits.Timeout ?? TimeSpan.FromSeconds(30);
        if (limits.MaximumMessageBytes < 32 || limits.MaximumReceivedBytes < 32 || limits.MaximumNotifications < 0 ||
            timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(limits));
        return new(limits, timeout, codec.PrepareInitialization(initialization, limits.MaximumMessageBytes));
    }

    private static async Task<CompilerRootSession> InitializeCoreAsync(CompilerWebSocketTransport transport,
        Prepared settings, ICompilerSessionCodec codec, CancellationToken cancellationToken)
    {
        var notifications = ImmutableArray.CreateBuilder<CompilerControlMessage>();
        long receivedBytes = 0;
        async Task<CompilerControlMessage> Reply(int id)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var message = await transport.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                receivedBytes += message.Bytes.Length;
                if (receivedBytes > settings.Limits.MaximumReceivedBytes) throw Invalid("Compiler handshake exceeds the receive byte budget.");
                if (message.Type != WebSocketMessageType.Binary) throw Invalid("Compiler control replies must be binary.");
                var reply = codec.DecodeControl(message.Bytes, settings.Limits.MaximumMessageBytes);
                if (reply.IsNotification)
                {
                    if (notifications.Count == settings.Limits.MaximumNotifications) throw Invalid("Compiler handshake exceeds the notification budget.");
                    notifications.Add(reply);
                    continue;
                }
                if (reply.RequestId != id) throw Invalid("Unexpected compiler initialization reply id.");
                if (reply.Code != 0) throw Invalid($"Compiler initialization failed with status {reply.Code}.");
                return reply;
            }
        }

        await transport.SendAsync(codec.EncodePreinitialize(), WebSocketMessageType.Binary, cancellationToken).ConfigureAwait(false);
        var preReply = await Reply(0).ConfigureAwait(false);
        var process = codec.ReadProcessInfo(preReply);

        await transport.SendAsync(codec.EncodeInitialize(settings.InitializePayload), WebSocketMessageType.Binary, cancellationToken).ConfigureAwait(false);
        var initializeReply = await Reply(1).ConfigureAwait(false);
        var metadata = codec.ReadChannelMetadata(initializeReply);
        return new(transport, new(process.ProcessId, metadata.FileSystemPort, metadata.DebuggerPort,
            metadata.LanguagePort, metadata.FormImagePort, metadata.LiveBackupPort, metadata.ServerId,
            notifications.ToImmutable()), codec);
    }

    private static InvalidDataException Invalid(string message) => new(message);
    public void Dispose() => transport.Dispose();
}
