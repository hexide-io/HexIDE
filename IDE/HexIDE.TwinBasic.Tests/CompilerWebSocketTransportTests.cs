using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace HexIDE.TwinBasic.Tests;

public sealed class CompilerWebSocketTransportTests
{
    [Theory]
    [InlineData(WebSocketMessageType.Binary)]
    [InlineData(WebSocketMessageType.Text)]
    public async Task Fragmented_message_preserves_bytes_type_and_next_message(WebSocketMessageType type)
    {
        using var pair = await Pair.Create(new(MaximumMessageBytes: 8));
        byte[] bytes = Encoding.UTF8.GetBytes("空😀!");
        await pair.Peer.SendAsync(bytes.AsMemory(0, 2), type, false, TestContext.Current.CancellationToken);
        await pair.Peer.SendAsync(bytes.AsMemory(2), type, true, TestContext.Current.CancellationToken);
        await pair.Peer.SendAsync(ReadOnlyMemory<byte>.Empty, type, true, TestContext.Current.CancellationToken);
        var received = await pair.Transport.ReceiveAsync(TestContext.Current.CancellationToken);
        Assert.Equal(type, received.Type);
        Assert.Equal(bytes, received.Bytes);
        Assert.Empty((await pair.Transport.ReceiveAsync(TestContext.Current.CancellationToken)).Bytes);
    }

    [Fact]
    public async Task Byte_limit_applies_across_fragments_and_fault_is_terminal()
    {
        using var pair = await Pair.Create(new(MaximumMessageBytes: 8));
        await pair.Peer.SendAsync(new byte[8].AsMemory(), WebSocketMessageType.Binary, false, TestContext.Current.CancellationToken);
        await pair.Peer.SendAsync(new byte[1].AsMemory(), WebSocketMessageType.Binary, true, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => pair.Transport.ReceiveAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pair.Transport.ReceiveAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Empty_fragments_cannot_bypass_receive_operation_limit()
    {
        using var pair = await Pair.Create(new(MaximumReceiveOperations: 2));
        for (var i = 0; i < 2; i++)
            await pair.Peer.SendAsync(ReadOnlyMemory<byte>.Empty, WebSocketMessageType.Binary, false, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => pair.Transport.ReceiveAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Deadline_applies_to_incomplete_messages()
    {
        using var pair = await Pair.Create(new(MessageTimeout: TimeSpan.FromMilliseconds(150)));
        await pair.Peer.SendAsync(new byte[1].AsMemory(), WebSocketMessageType.Binary, false, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<TimeoutException>(() => pair.Transport.ReceiveAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pair.Transport.ReceiveAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Cancellation_of_active_receive_ends_connection()
    {
        using var pair = await Pair.Create();
        using var cancellation = new CancellationTokenSource();
        var receive = pair.Transport.ReceiveAsync(cancellation.Token);
        cancellation.Cancel();
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => receive);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pair.Transport.ReceiveAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Cancelling_queued_receive_does_not_abort_active_receive()
    {
        using var pair = await Pair.Create();
        var active = pair.Transport.ReceiveAsync(TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        var queued = pair.Transport.ReceiveAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        await pair.Peer.SendAsync(new byte[] { 42 }.AsMemory(), WebSocketMessageType.Binary, true, TestContext.Current.CancellationToken);
        Assert.Equal(new byte[] { 42 }, (await active).Bytes);
    }

    [Fact]
    public async Task Disposal_settles_active_and_queued_receives_without_gate_race()
    {
        using var pair = await Pair.Create();
        var active = pair.Transport.ReceiveAsync(TestContext.Current.CancellationToken);
        var queued = pair.Transport.ReceiveAsync(TestContext.Current.CancellationToken);
        pair.Transport.Dispose();
        pair.Transport.Dispose();
        await Assert.ThrowsAsync<IOException>(() => active.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => queued.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Peer_close_discards_partial_message()
    {
        using var pair = await Pair.Create();
        await pair.Peer.SendAsync(new byte[1].AsMemory(), WebSocketMessageType.Binary, false, TestContext.Current.CancellationToken);
        await pair.Peer.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "private peer text", TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<IOException>(() => pair.Transport.ReceiveAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain("private peer text", error.ToString());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pair.Transport.ReceiveAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Concurrent_sends_are_complete_distinct_messages()
    {
        using var pair = await Pair.Create(new(MaximumMessageBytes: 2));
        var sends = Enumerable.Range(0, 20).Select(i => pair.Transport.SendAsync(new byte[] { (byte)i }, WebSocketMessageType.Binary, TestContext.Current.CancellationToken)).ToArray();
        var seen = new HashSet<byte>();
        for (var i = 0; i < sends.Length; i++)
        {
            var buffer = new byte[4];
            var result = await pair.Peer.ReceiveAsync(buffer.AsMemory(), TestContext.Current.CancellationToken);
            Assert.True(result.EndOfMessage);
            Assert.Equal(WebSocketMessageType.Binary, result.MessageType);
            Assert.Equal(1, result.Count);
            Assert.True(seen.Add(buffer[0]));
        }
        await Task.WhenAll(sends);
        await Assert.ThrowsAsync<InvalidDataException>(() => pair.Transport.SendAsync(new byte[3], WebSocketMessageType.Binary, TestContext.Current.CancellationToken));
        // Validation failures before I/O do not poison a usable connection.
        await pair.Transport.SendAsync(new byte[1], WebSocketMessageType.Text, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("ws://example.com/secret/root")]
    [InlineData("ws://localhost/secret/root")]
    [InlineData("wss://127.0.0.1/secret/root")]
    [InlineData("ws://192.0.2.1/secret/root")]
    [InlineData("ws://name:secret@127.0.0.1/root")]
    [InlineData("ws://127.0.0.1/root?secret")]
    [InlineData("ws://127.0.0.1/root#secret")]
    [InlineData("/secret/root")]
    public async Task Rejects_non_loopback_or_ambiguous_endpoints_without_echoing_secrets(string endpoint)
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            CompilerWebSocketTransport.ConnectAsync(new(endpoint, UriKind.RelativeOrAbsolute), cancellationToken: TestContext.Current.CancellationToken));
        Assert.DoesNotContain("secret", error.ToString());
    }

    [Fact]
    public async Task Failed_handshake_does_not_expose_authenticated_endpoint()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var connect = CompilerWebSocketTransport.ConnectAsync(new($"ws://127.0.0.1:{port}/private-passkey/root"), cancellationToken: TestContext.Current.CancellationToken);
        using var peer = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        await ReadRequest(peer);
        await peer.GetStream().WriteAsync("HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(),
            TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<IOException>(() => connect);
        Assert.DoesNotContain("private-passkey", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task Connect_performs_real_upgrade_and_transfers_control_bytes()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var connect = CompilerWebSocketTransport.ConnectAsync(new($"ws://127.0.0.1:{port}/test-key/root"),
            cancellationToken: TestContext.Current.CancellationToken);
        using var peer = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        var request = await ReadRequest(peer);
        Assert.StartsWith("GET /test-key/root HTTP/1.1\r\n", request);
        var key = request.Split("\r\n").Single(line => line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
            .Split(':', 2)[1].Trim();
        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        await peer.GetStream().WriteAsync(Encoding.ASCII.GetBytes(
            $"HTTP/1.1 101 Switching Protocols\r\nConnection: Upgrade\r\nUpgrade: websocket\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"),
            TestContext.Current.CancellationToken);
        using var transport = await connect;
        using var socket = WebSocket.CreateFromStream(peer.GetStream(), true, null, Timeout.InfiniteTimeSpan);
        await transport.SendAsync(new CompilerWireCodec().EncodeControlRequest(22, 0, []), WebSocketMessageType.Binary,
            TestContext.Current.CancellationToken);
        var bytes = new byte[16];
        var received = await socket.ReceiveAsync(bytes.AsMemory(), TestContext.Current.CancellationToken);
        Assert.Equal(new byte[] { 22, 0, 0, 0, 0 }, bytes[..received.Count]);
        Assert.True(received.EndOfMessage);
        await socket.SendAsync(new byte[8].AsMemory(), WebSocketMessageType.Binary, true, TestContext.Current.CancellationToken);
        Assert.Equal(0, new CompilerWireCodec().DecodeControl((await transport.ReceiveAsync(TestContext.Current.CancellationToken)).Bytes).RequestId);
    }

    [Fact]
    public async Task Redirect_is_rejected_without_contacting_destination()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        using var destination = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        destination.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var otherPort = ((IPEndPoint)destination.LocalEndpoint).Port;
        var connect = CompilerWebSocketTransport.ConnectAsync(new($"ws://127.0.0.1:{port}/secret/root"),
            cancellationToken: TestContext.Current.CancellationToken);
        using var peer = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        await ReadRequest(peer);
        await peer.GetStream().WriteAsync(Encoding.ASCII.GetBytes(
            $"HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:{otherPort}/secret/root\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"),
            TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<IOException>(() => connect);
        Assert.False(destination.Pending());
        Assert.DoesNotContain("secret", error.ToString());
    }

    [Fact]
    public async Task Connection_deadline_covers_unanswered_http_upgrade()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var connect = CompilerWebSocketTransport.ConnectAsync(new($"ws://127.0.0.1:{port}/secret/root"),
            new(MessageTimeout: TimeSpan.FromMilliseconds(250)), TestContext.Current.CancellationToken);
        using var peer = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<TimeoutException>(() => connect);
    }

    private static async Task<string> ReadRequest(TcpClient peer)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var request = new StringBuilder();
        var one = new byte[1];
        while (request.Length < 8192)
        {
            if (await peer.GetStream().ReadAsync(one, timeout.Token) == 0) throw new EndOfStreamException();
            request.Append((char)one[0]);
            if (request.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal)) return request.ToString();
        }
        throw new InvalidDataException("HTTP request exceeded test bound.");
    }

    [Theory]
    [InlineData(0, 1, 100)]
    [InlineData(1, 0, 100)]
    [InlineData(1, 1, 0)]
    [InlineData(1, 1, -1)]
    public async Task Invalid_limits_fail_before_connection(int bytes, int operations, int milliseconds)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => CompilerWebSocketTransport.ConnectAsync(
            new("ws://127.0.0.1:1/key/root"), new(bytes, operations, TimeSpan.FromMilliseconds(milliseconds)), TestContext.Current.CancellationToken));
    }

    private sealed class Pair : IDisposable
    {
        private readonly TcpClient client;
        private readonly TcpClient server;
        public CompilerWebSocketTransport Transport { get; }
        public WebSocket Peer { get; }

        private Pair(TcpClient client, TcpClient server, CompilerTransportLimits? limits)
        {
            this.client = client;
            this.server = server;
            Transport = new(WebSocket.CreateFromStream(client.GetStream(), false, null, Timeout.InfiniteTimeSpan), limits);
            Peer = WebSocket.CreateFromStream(server.GetStream(), true, null, Timeout.InfiniteTimeSpan);
        }

        public static async Task<Pair> Create(CompilerTransportLimits? limits = null)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var client = new TcpClient();
            await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, TestContext.Current.CancellationToken);
            return new(client, await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken), limits);
        }

        public void Dispose()
        {
            Transport.Dispose();
            Peer.Dispose();
            client.Dispose();
            server.Dispose();
        }
    }
}
