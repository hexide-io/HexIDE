using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace HexIDE.TwinBasic;

/// <summary>Explicit initialization inputs; the adapter neither reads IDE settings nor loads licence keys.</summary>
public sealed record CompilerRootInitialization(IReadOnlyList<string> PackagePaths, JsonElement LanguageData);

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
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly CompilerWebSocketTransport transport;
    internal CompilerWebSocketTransport Transport => transport;
    public CompilerRootConnectionInfo Connection { get; }

    private CompilerRootSession(CompilerWebSocketTransport transport, CompilerRootConnectionInfo connection)
    {
        this.transport = transport;
        Connection = connection;
    }

    public static async Task<CompilerRootSession> ConnectAsync(Uri endpoint, CompilerRootInitialization initialization,
        CompilerRootHandshakeLimits? limits = null, CancellationToken cancellationToken = default)
    {
        var settings = Prepare(initialization, limits);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(settings.Timeout);
        CompilerWebSocketTransport? transport = null;
        try
        {
            transport = await CompilerWebSocketTransport.ConnectAsync(endpoint,
                new(MaximumMessageBytes: settings.Limits.MaximumMessageBytes, MessageTimeout: settings.Timeout),
                deadline.Token).ConfigureAwait(false);
            return await InitializeCoreAsync(transport, settings, deadline.Token).ConfigureAwait(false);
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
        CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = Prepare(initialization, limits);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(settings.Timeout);
            try { return await InitializeCoreAsync(transport, settings, deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { throw new TimeoutException("Compiler root initialization timed out."); }
            catch (OperationCanceledException) { throw new OperationCanceledException(cancellationToken); }
        }
        catch { transport.Dispose(); throw; }
    }

    private sealed record Prepared(CompilerRootHandshakeLimits Limits, TimeSpan Timeout, byte[] InitializePayload);

    private static Prepared Prepare(CompilerRootInitialization initialization, CompilerRootHandshakeLimits? limits)
    {
        ArgumentNullException.ThrowIfNull(initialization);
        ArgumentNullException.ThrowIfNull(initialization.PackagePaths);
        limits ??= new();
        var timeout = limits.Timeout ?? TimeSpan.FromSeconds(30);
        if (limits.MaximumMessageBytes < 32 || limits.MaximumReceivedBytes < 32 || limits.MaximumNotifications < 0 ||
            timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(limits));
        if (initialization.LanguageData.ValueKind != JsonValueKind.Object || !initialization.LanguageData.EnumerateObject().Any())
            throw new ArgumentException("Compiler initialization requires a nonempty language-data object.", nameof(initialization));

        // Size before allocation; '?' separates entries in the observed package-list representation.
        long packageBytes = 0;
        var paths = new List<string>();
        foreach (var path in initialization.PackagePaths)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Contains('?') || path.Contains('\0'))
                throw new ArgumentException("Package entries must be nonempty and contain no delimiter or NUL.", nameof(initialization));
            packageBytes += StrictUtf8.GetByteCount(path) + 1L;
            if (packageBytes > limits.MaximumMessageBytes) throw Invalid("Compiler package list exceeds the byte limit.");
            paths.Add(path);
        }
        var language = initialization.LanguageData.GetRawText();
        var total = 12L + packageBytes + StrictUtf8.GetByteCount(language);
        if (total > limits.MaximumMessageBytes - 5) throw Invalid("Compiler initialization exceeds the byte limit.");
        var payload = new byte[(int)total];
        // flags = 0, reserved = 0, byte length of first string, package list, language JSON.
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8), (int)packageBytes);
        var offset = 12;
        foreach (var path in paths)
        {
            offset += StrictUtf8.GetBytes(path, payload.AsSpan(offset));
            payload[offset++] = (byte)'?';
        }
        StrictUtf8.GetBytes(language, payload.AsSpan(offset));
        return new(limits, timeout, payload);
    }

    private static async Task<CompilerRootSession> InitializeCoreAsync(CompilerWebSocketTransport transport,
        Prepared settings, CancellationToken cancellationToken)
    {
        var codec = new CompilerWireCodec(new(MaximumMessageBytes: settings.Limits.MaximumMessageBytes));
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
                var reply = codec.DecodeControl(message.Bytes);
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

        // DPI 96; tracing off, one LLVM worker, 10000 ms threshold, cache retention off, empty licence.
        byte[] preinitialize = [96, 0, 0, 0, .. "0,1,10000,0,"u8];
        await transport.SendAsync(codec.EncodeControlRequest(22, 0, preinitialize), WebSocketMessageType.Binary, cancellationToken).ConfigureAwait(false);
        var preReply = await Reply(0).ConfigureAwait(false);
        if (preReply.Payload.Length != 4) throw Invalid("Invalid compiler process-id reply.");
        var processId = BinaryPrimitives.ReadInt32LittleEndian(preReply.Payload.AsSpan());
        if (processId <= 0) throw Invalid("Invalid compiler process id.");

        await transport.SendAsync(codec.EncodeControlRequest(0, 1, settings.InitializePayload), WebSocketMessageType.Binary, cancellationToken).ConfigureAwait(false);
        var initializeReply = await Reply(1).ConfigureAwait(false);
        if (initializeReply.Payload.Length <= 20) throw Invalid("Incomplete compiler channel metadata.");
        var ports = new int[5];
        for (var index = 0; index < ports.Length; index++)
        {
            ports[index] = BinaryPrimitives.ReadInt32LittleEndian(initializeReply.Payload.AsSpan(index * 4, 4));
            if (ports[index] is < 1 or > 65535) throw Invalid("Invalid compiler channel port.");
        }
        string serverId;
        try { serverId = StrictUtf8.GetString(initializeReply.Payload.AsSpan()[20..]); }
        catch (DecoderFallbackException) { throw Invalid("Invalid UTF-8 compiler server identity."); }
        if (string.IsNullOrWhiteSpace(serverId) || serverId.Contains('\0')) throw Invalid("Invalid compiler server identity.");
        return new(transport, new(processId, ports[0], ports[1], ports[2], ports[3], ports[4], serverId, notifications.ToImmutable()));
    }

    private static InvalidDataException Invalid(string message) => new(message);
    public void Dispose() => transport.Dispose();
}
