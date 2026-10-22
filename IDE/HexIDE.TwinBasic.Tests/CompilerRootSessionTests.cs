using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace HexIDE.TwinBasic.Tests;

public sealed class CompilerRootSessionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static CompilerRootInitialization Options(string json = "{\"hello\":\"空\"}") =>
        new(["packages", "!user包"], JsonDocument.Parse(json).RootElement.Clone());

    [Fact]
    public async Task Handshake_matches_independent_wire_vectors_and_preserves_notifications()
    {
        using var pair = await Pair.Create();
        var connect = CompilerRootSession.InitializeAsync(pair.Transport, Options(), cancellationToken: Token);
        byte[] expectedPreinitialize = [22, 0, 0, 0, 0, 96, 0, 0, 0, .. "0,1,10000,0,"u8];
        Assert.Equal(expectedPreinitialize, await pair.Request());
        await pair.Reply(-1, 987, [0xFF, 0x80]);
        await pair.Reply(0, 0, Integer(123));
        byte[] packageText = Encoding.UTF8.GetBytes("packages?!user包?");
        byte[] expected = [0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            (byte)packageText.Length, 0, 0, 0, .. packageText, .. Encoding.UTF8.GetBytes("{\"hello\":\"空\"}")];
        Assert.Equal(expected, await pair.Request());
        await pair.Reply(1, 0, Metadata());
        using var session = await connect;
        Assert.Equal(123, session.Connection.ProcessId);
        Assert.Equal(1001, session.Connection.FileSystemPort);
        Assert.Equal(1002, session.Connection.DebuggerPort);
        Assert.Equal(1003, session.Connection.LanguagePort);
        Assert.Equal(1004, session.Connection.FormImagePort);
        Assert.Equal(1005, session.Connection.LiveBackupPort);
        Assert.Equal("instance", session.Connection.ServerId);
        var notification = Assert.Single(session.Connection.InitializationNotifications);
        Assert.Equal(987, notification.Code);
        Assert.Equal(new byte[] { 0xFF, 0x80 }, notification.Payload);
        session.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pair.Transport.ReceiveAsync(Token));
    }

    [Theory]
    [InlineData(1, 0, 123, 4)] // unexpected id
    [InlineData(0, -1, 123, 4)] // compiler rejection
    [InlineData(0, 1, 123, 4)] // unknown status is not success
    [InlineData(0, 0, 0, 4)]
    [InlineData(0, 0, -1, 4)]
    [InlineData(0, 0, 123, 3)]
    [InlineData(0, 0, 123, 5)]
    public async Task Invalid_preinitialization_fails_without_sending_initialize(int id, int status, int pid, int length)
    {
        using var pair = await Pair.Create();
        var connect = CompilerRootSession.InitializeAsync(pair.Transport, Options(), cancellationToken: Token);
        await pair.Request();
        var payload = new byte[length];
        Integer(pid).AsSpan(0, Math.Min(length, 4)).CopyTo(payload);
        await pair.Reply(id, status, payload);
        await Assert.ThrowsAsync<InvalidDataException>(() => connect);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pair.Transport.ReceiveAsync(Token));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("no-identity")]
    [InlineData("zero-port")]
    [InlineData("negative-port")]
    [InlineData("large-port")]
    [InlineData("bad-utf8")]
    [InlineData("nul-identity")]
    [InlineData("blank-identity")]
    [InlineData("duplicate-preinit")]
    public async Task Invalid_initialization_metadata_aborts_connection(string fault)
    {
        using var pair = await Pair.Create();
        var connect = CompilerRootSession.InitializeAsync(pair.Transport, Options(), cancellationToken: Token);
        await pair.Request();
        await pair.Reply(0, 0, Integer(123));
        await pair.Request();
        var payload = Metadata();
        switch (fault)
        {
            case "short": payload = payload[..19]; break;
            case "no-identity": payload = payload[..20]; break;
            case "zero-port": Integer(0).CopyTo(payload, 0); break;
            case "negative-port": Integer(-1).CopyTo(payload, 4); break;
            case "large-port": Integer(65536).CopyTo(payload, 16); break;
            case "bad-utf8": payload[20] = 0xFF; break;
            case "nul-identity": payload[20] = 0; break;
            case "blank-identity": payload.AsSpan(20).Fill(32); break;
        }
        await pair.Reply(fault == "duplicate-preinit" ? 0 : 1, 0, payload);
        await Assert.ThrowsAsync<InvalidDataException>(() => connect);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pair.Transport.ReceiveAsync(Token));
    }

    [Fact]
    public async Task Notification_count_and_total_byte_limits_are_independent()
    {
        foreach (var limits in new[] { new CompilerRootHandshakeLimits(MaximumNotifications: 0),
                     new CompilerRootHandshakeLimits(MaximumReceivedBytes: 32) })
        {
            using var pair = await Pair.Create();
            var connect = CompilerRootSession.InitializeAsync(pair.Transport, Options(), limits, Token);
            await pair.Request();
            await pair.Reply(-1, 123, new byte[25]);
            await Assert.ThrowsAsync<InvalidDataException>(() => connect);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => pair.Transport.ReceiveAsync(Token));
        }
    }

    [Fact]
    public async Task Aggregate_budget_includes_both_replies()
    {
        using var pair = await Pair.Create();
        var connect = CompilerRootSession.InitializeAsync(pair.Transport, Options(), new(MaximumReceivedBytes: 40), Token);
        await pair.Request();
        await pair.Reply(0, 0, Integer(123)); // 12 bytes
        await pair.Request();
        await pair.Reply(1, 0, Metadata()); // 36 bytes; each fits but aggregate does not
        await Assert.ThrowsAsync<InvalidDataException>(() => connect);
    }

    [Fact]
    public async Task Text_control_reply_is_rejected()
    {
        using var pair = await Pair.Create();
        var connect = CompilerRootSession.InitializeAsync(pair.Transport, Options(), cancellationToken: Token);
        await pair.Request();
        await pair.Peer.SendAsync("not binary"u8.ToArray().AsMemory(), WebSocketMessageType.Text, true, Token);
        await Assert.ThrowsAsync<InvalidDataException>(() => connect);
    }

    [Fact]
    public async Task Caller_cancellation_closes_owned_transport_and_preserves_token()
    {
        using var pair = await Pair.Create();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var connect = CompilerRootSession.InitializeAsync(pair.Transport, Options(), cancellationToken: cancel.Token);
        await pair.Request();
        cancel.Cancel();
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => connect);
        Assert.Equal(cancel.Token, error.CancellationToken);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pair.Transport.ReceiveAsync(Token));
    }

    [Fact]
    public async Task Handshake_deadline_includes_time_between_notifications()
    {
        using var pair = await Pair.Create();
        var connect = CompilerRootSession.InitializeAsync(pair.Transport, Options(), new(Timeout: TimeSpan.FromMilliseconds(250)), Token);
        await pair.Request();
        await pair.Reply(-1, 123, []);
        await Assert.ThrowsAsync<TimeoutException>(() => connect);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pair.Transport.ReceiveAsync(Token));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task Missing_language_data_is_rejected_before_connecting(string json)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => CompilerRootSession.ConnectAsync(
            new("ws://127.0.0.1:1/key/root"), Options(json), cancellationToken: Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("one?two")]
    [InlineData("one\0two")]
    public async Task Invalid_package_entry_is_rejected_before_connecting(string package)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => CompilerRootSession.ConnectAsync(
            new("ws://127.0.0.1:1/key/root"), Options() with { PackagePaths = [package] }, cancellationToken: Token));
    }

    [Fact]
    public async Task Oversize_initialization_is_rejected_before_connecting()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => CompilerRootSession.ConnectAsync(
            new("ws://127.0.0.1:1/key/root"), Options(), new(MaximumMessageBytes: 32), Token));
    }

    private static byte[] Integer(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return bytes;
    }

    private static byte[] Metadata() => [.. Integer(1001), .. Integer(1002), .. Integer(1003),
        .. Integer(1004), .. Integer(1005), .. "instance"u8];

    private sealed class Pair : IDisposable
    {
        private readonly TcpClient client;
        private readonly TcpClient server;
        public CompilerWebSocketTransport Transport { get; }
        public WebSocket Peer { get; }
        private Pair(TcpClient client, TcpClient server)
        {
            this.client = client;
            this.server = server;
            Transport = new(WebSocket.CreateFromStream(client.GetStream(), false, null, Timeout.InfiniteTimeSpan));
            Peer = WebSocket.CreateFromStream(server.GetStream(), true, null, Timeout.InfiniteTimeSpan);
        }
        public static async Task<Pair> Create()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var client = new TcpClient();
            await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, Token);
            return new(client, await listener.AcceptTcpClientAsync(Token));
        }
        public async Task<byte[]> Request()
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var buffer = new byte[4096];
            var result = await Peer.ReceiveAsync(buffer.AsMemory(), timeout.Token);
            Assert.True(result.EndOfMessage);
            Assert.Equal(WebSocketMessageType.Binary, result.MessageType);
            return buffer[..result.Count];
        }
        public async Task Reply(int id, int status, byte[] payload) =>
            await Peer.SendAsync(((byte[])[.. Integer(id), .. Integer(status), .. payload]).AsMemory(), WebSocketMessageType.Binary, true, Token);
        public void Dispose()
        {
            Transport.Dispose();
            Peer.Dispose();
            client.Dispose();
            server.Dispose();
        }
    }
}
