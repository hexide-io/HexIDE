using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;

namespace HexIDE.TwinBasic.Tests;

// Tokens address decoded objects; no compiler encoding is used by session tests (#1217).
internal sealed class InMemoryCompilerSessionCodec : ICompilerSessionCodec
{
    internal sealed record ChannelData(WebSocketMessageType Type, byte[] Bytes) : CompilerChannelData;
    public CompilerChannelData DecodeChannel(WebSocketMessageType type, ReadOnlyMemory<byte> bytes) => new ChannelData(type, bytes.ToArray());
    public CompilerEncodedMessage EncodeChannel(CompilerChannelData message)
    {
        var data = (ChannelData)message;
        return new(data.Type, data.Bytes);
    }

    internal sealed record Language : CompilerLanguageData;
    private sealed record Initialization : CompilerInitializationRequest;
    internal sealed record Request(int Id, object Message);
    internal sealed record Preinitialize;
    internal sealed record Initialize;
    private readonly ConcurrentDictionary<byte, object> objects = new();
    private readonly ConcurrentDictionary<CompilerControlMessage, object> data = new();
    private int next;
    internal static CompilerRootInitialization Options => new([], new Language());
    internal byte[] Store(object value)
    {
        var key = checked((byte)Interlocked.Increment(ref next));
        objects[key] = value;
        return [key];
    }
    internal Request ReadRequest(ReadOnlySpan<byte> bytes) => (Request)objects[bytes[0]];
    internal byte[] Reply(int id, int status = 0, object? value = null, ImmutableArray<byte> payload = default)
    {
        var message = new CompilerControlMessage(id, status, payload.IsDefault ? [] : payload);
        if (value is not null) data[message] = value;
        return Store(message);
    }
    public CompilerInitializationRequest PrepareInitialization(CompilerRootInitialization initialization, int maximumMessageBytes) => new Initialization();
    public byte[] EncodePreinitialize() => Store(new Request(0, new Preinitialize()));
    public byte[] EncodeInitialize(CompilerInitializationRequest initialization) => Store(new Request(1, new Initialize()));
    public CompilerControlMessage DecodeControl(ReadOnlySpan<byte> message, int maximumMessageBytes) => (CompilerControlMessage)objects[message[0]];
    public CompilerProcessInfo ReadProcessInfo(CompilerControlMessage message) => (CompilerProcessInfo)data[message];
    public CompilerChannelMetadata ReadChannelMetadata(CompilerControlMessage message) => (CompilerChannelMetadata)data[message];
    public CompilerSettingsRequest ReadSettingsRequest(CompilerControlMessage message) => (CompilerSettingsRequest)data[message];
    public CompilerPackageRequest ReadPackageRequest(CompilerControlMessage message) => (CompilerPackageRequest)data[message];
    public long MaximumPackageContentBytes(CompilerPackageRequest package, int maximumMessageBytes) => maximumMessageBytes;
    public byte[] EncodeProjectRequest(int requestId, CompilerProjectRequest request, int maximumMessageBytes) => Store(new Request(requestId, request));
}

internal sealed class CompilerSessionTestPeer(TcpClient client, TcpClient server) : IDisposable
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    internal InMemoryCompilerSessionCodec Codec { get; } = new();
    internal CompilerWebSocketTransport Transport { get; } = new(WebSocket.CreateFromStream(client.GetStream(), false, null, Timeout.InfiniteTimeSpan));
    private readonly WebSocket socket = WebSocket.CreateFromStream(server.GetStream(), true, null, Timeout.InfiniteTimeSpan);
    internal CompilerRootSession Root { get; private set; } = null!;
    public string SourceDirectory { get; } = Path.Combine(Path.GetTempPath(), "hexide-preparation-test-" + Guid.NewGuid().ToString("N"));
    public string Source => Path.Combine(SourceDirectory, "source.twinproj");
    internal static async Task<CompilerSessionTestPeer> Create(bool initialize = true)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var client = new TcpClient();
        await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, Token);
        var peer = new CompilerSessionTestPeer(client, await listener.AcceptTcpClientAsync(Token));
        Directory.CreateDirectory(peer.SourceDirectory);
        await File.WriteAllBytesAsync(peer.Source, [1, 2, 3, 4], Token);
        if (initialize)
        {
            var pending = peer.Initialize();
            await peer.Request();
            await peer.Reply(0, value: new CompilerProcessInfo(42));
            await peer.Request();
            await peer.Reply(1, value: new CompilerChannelMetadata(1001, 1002, 1003, 1004, 1005, "test"));
            peer.Root = await pending;
        }
        return peer;
    }
    internal Task<CompilerRootSession> Initialize(CompilerRootHandshakeLimits? limits = null, CancellationToken? token = null) =>
        CompilerRootSession.InitializeAsync(Transport, InMemoryCompilerSessionCodec.Options, limits, token ?? Token, Codec);
    internal async Task<InMemoryCompilerSessionCodec.Request> Request()
    {
        var buffer = new byte[1];
        await socket.ReceiveAsync(buffer.AsMemory(), Token).AsTask().WaitAsync(TimeSpan.FromSeconds(5), Token);
        return Codec.ReadRequest(buffer);
    }
    internal async Task Reply(int id, int status = 0, byte[]? payload = null, object? value = null) =>
        await socket.SendAsync(Codec.Reply(id, status, value, payload is null ? [] : ImmutableArray.CreateRange(payload)).AsMemory(), WebSocketMessageType.Binary, true, Token);
    public void Dispose()
    {
        Root?.Dispose();
        Transport.Dispose();
        socket.Dispose();
        client.Dispose();
        server.Dispose();
        Directory.Delete(SourceDirectory, true);
    }
}
