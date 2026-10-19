using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;

namespace HexIDE.TwinBasic.Tests;

public sealed class CompilerChannelSessionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly Uri RootEndpoint = new("ws://127.0.0.1:9000/key%2Fopaque/root");

    [Fact]
    public async Task Attachment_uses_advertised_ports_and_preserves_escaped_authentication_path()
    {
        using var harness = await Harness.Create();
        var endpoints = new List<Uri>();
        await using var session = await harness.Attach(connector: (uri, _, _) =>
        {
            endpoints.Add(uri);
            return Task.FromResult(harness.Pairs[(CompilerChannel)endpoints.Count].Transport);
        });
        Assert.Equal(new[] { "ws://127.0.0.1:1001/key%2Fopaque/fs", "ws://127.0.0.1:1003/key%2Fopaque/language",
            "ws://127.0.0.1:1002/key%2Fopaque/debugger" }, endpoints.Select(uri => uri.AbsoluteUri));
        Assert.Equal(42, session.Connection.ProcessId);
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task Multiplexing_preserves_channel_type_bytes_and_per_channel_order()
    {
        using var harness = await Harness.Create();
        await using var session = await harness.Attach();
        foreach (var channel in Enum.GetValues<CompilerChannel>())
        {
            await harness.Pairs[channel].Send([1], WebSocketMessageType.Binary);
            await harness.Pairs[channel].Send([2], WebSocketMessageType.Binary);
            await session.SendAsync(channel, "{}"u8.ToArray(), WebSocketMessageType.Text, Token);
            var sent = await harness.Pairs[channel].Read();
            Assert.Equal(WebSocketMessageType.Text, sent.Type);
            Assert.Equal("{}"u8.ToArray(), sent.Bytes);
        }
        var last = new Dictionary<CompilerChannel, byte>();
        for (var i = 0; i < 8; i++)
        {
            var message = await session.ReceiveAsync(Token).WaitAsync(TimeSpan.FromSeconds(5), Token);
            Assert.Equal(WebSocketMessageType.Binary, message.Type);
            Assert.Equal(last.GetValueOrDefault(message.Channel) + 1, message.Bytes.Span[0]);
            last[message.Channel] = message.Bytes.Span[0];
        }
        Assert.Equal(4, last.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Failed_attachment_disposes_root_and_every_acquired_socket(int failAt)
    {
        using var harness = await Harness.Create();
        var calls = 0;
        await Assert.ThrowsAsync<IOException>(() => harness.Attach(connector: (_, _, _) =>
        {
            if (calls++ == failAt) throw new IOException("Connection refused.");
            return Task.FromResult(harness.Pairs[(CompilerChannel)calls].Transport);
        }));
        for (var i = 0; i <= failAt; i++)
            await Assert.ThrowsAsync<ObjectDisposedException>(() => harness.Pairs[(CompilerChannel)i].Transport.ReceiveAsync(Token));
    }

    [Fact]
    public async Task Cancelled_attachment_closes_already_acquired_sockets()
    {
        using var harness = await Harness.Create();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Attach(connector: (_, _, _) =>
        {
            cancel.Cancel();
            return Task.FromResult(harness.Pairs[CompilerChannel.FileSystem].Transport);
        }, cancellationToken: cancel.Token));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => harness.Pairs[CompilerChannel.Root].Transport.ReceiveAsync(Token));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => harness.Pairs[CompilerChannel.FileSystem].Transport.ReceiveAsync(Token));
    }

    [Fact]
    public async Task Attachment_deadline_covers_a_stalled_connector_and_closes_root()
    {
        using var harness = await Harness.Create();
        await Assert.ThrowsAsync<TimeoutException>(() => harness.Attach(new(ConnectionTimeout: TimeSpan.FromMilliseconds(100)),
            connector: async (_, _, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return harness.Pairs[CompilerChannel.FileSystem].Transport;
            }));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => harness.Pairs[CompilerChannel.Root].Transport.ReceiveAsync(Token));
    }

    [Fact]
    public async Task Session_message_limit_also_applies_to_a_root_created_with_larger_limits()
    {
        using var harness = await Harness.Create();
        await using var session = await harness.Attach(new(MaximumMessageBytes: 32));
        await harness.Pairs[CompilerChannel.Root].Send(new byte[33], WebSocketMessageType.Binary);
        Assert.False((await session.Completion.WaitAsync(TimeSpan.FromSeconds(5), Token)).WasDisposed);
        await Assert.ThrowsAsync<IOException>(() => session.ReceiveAsync(Token));
    }

    [Theory]
    [InlineData((int)CompilerChannel.Root)]
    [InlineData((int)CompilerChannel.FileSystem)]
    [InlineData((int)CompilerChannel.Language)]
    [InlineData((int)CompilerChannel.Debugger)]
    public async Task Any_peer_close_terminates_all_channels_and_pending_receive(int channelIndex)
    {
        var channel = (CompilerChannel)channelIndex;
        using var harness = await Harness.Create();
        await using var session = await harness.Attach();
        var pending = session.ReceiveAsync(Token);
        await harness.Pairs[channel].Peer.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "private peer detail", Token);
        var end = await session.Completion.WaitAsync(TimeSpan.FromSeconds(5), Token);
        Assert.False(end.WasDisposed);
        Assert.Contains(channel.ToString(), end.Error);
        Assert.DoesNotContain("private peer detail", end.Error);
        await Assert.ThrowsAsync<IOException>(() => pending);
        foreach (var pair in harness.Pairs.Values)
            await Assert.ThrowsAsync<ObjectDisposedException>(() => pair.Transport.ReceiveAsync(Token));
    }

    [Fact]
    public async Task Disposal_joins_readers_and_is_idempotent()
    {
        using var harness = await Harness.Create();
        var session = await harness.Attach();
        var pending = session.ReceiveAsync(Token);
        await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), Token);
        await session.DisposeAsync();
        Assert.True((await session.Completion).WasDisposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pending);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.SendAsync(CompilerChannel.Root, new byte[1], WebSocketMessageType.Binary, Token));
    }

    [Fact]
    public async Task Cancelling_consumer_does_not_abort_socket_readers()
    {
        using var harness = await Harness.Create();
        await using var session = await harness.Attach();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var pending = session.ReceiveAsync(cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await harness.Pairs[CompilerChannel.Language].Send([7], WebSocketMessageType.Binary);
        Assert.Equal(7, (await session.ReceiveAsync(Token)).Bytes.Span[0]);
        Assert.False(session.Completion.IsCompleted);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Count_or_byte_queue_overflow_is_terminal(bool count)
    {
        using var harness = await Harness.Create();
        await using var session = await harness.Attach(count ? new(MaximumQueuedMessages: 1) : new(MaximumQueuedBytes: 1));
        await harness.Pairs[CompilerChannel.Root].Send(count ? [1] : [1, 2], WebSocketMessageType.Binary);
        if (count) await harness.Pairs[CompilerChannel.Root].Send([2], WebSocketMessageType.Binary);
        var end = await session.Completion.WaitAsync(TimeSpan.FromSeconds(5), Token);
        Assert.False(end.WasDisposed);
        Assert.Contains("limit", end.Error);
        await Assert.ThrowsAsync<IOException>(() => session.ReceiveAsync(Token));
    }

    [Fact]
    public async Task Consuming_messages_releases_queue_byte_budget()
    {
        using var harness = await Harness.Create();
        await using var session = await harness.Attach(new(MaximumQueuedBytes: 2));
        for (var i = 0; i < 3; i++)
        {
            await harness.Pairs[CompilerChannel.Language].Send([1, 2], WebSocketMessageType.Binary);
            Assert.Equal(2, (await session.ReceiveAsync(Token)).Bytes.Length);
        }
        Assert.False(session.Completion.IsCompleted);
    }

    [Theory]
    [InlineData((int)CompilerChannel.Root)]
    [InlineData((int)CompilerChannel.Language)]
    public async Task Idle_is_allowed_but_incomplete_messages_have_a_deadline(int channelIndex)
    {
        var channel = (CompilerChannel)channelIndex;
        using var harness = await Harness.Create();
        await using var session = await harness.Attach(new(MessageTimeout: TimeSpan.FromMilliseconds(100)));
        await Task.Delay(300, Token);
        Assert.False(session.Completion.IsCompleted);
        await harness.Pairs[channel].Peer.SendAsync(new byte[1].AsMemory(), WebSocketMessageType.Binary, false, Token);
        var end = await session.Completion.WaitAsync(TimeSpan.FromSeconds(5), Token);
        Assert.Contains("timed out", end.Error);
    }

    [Fact]
    public async Task Precancelled_send_does_not_end_session()
    {
        using var harness = await Harness.Create();
        await using var session = await harness.Attach();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.SendAsync(CompilerChannel.Root, new byte[1], WebSocketMessageType.Binary, cancel.Token));
        Assert.False(session.Completion.IsCompleted);
    }

    private sealed class Harness : IDisposable
    {
        public Dictionary<CompilerChannel, Pair> Pairs { get; } = [];
        private CompilerRootSession root = null!;
        public static async Task<Harness> Create()
        {
            var harness = new Harness();
            foreach (var channel in Enum.GetValues<CompilerChannel>()) harness.Pairs.Add(channel, await Pair.Create());
            using var language = JsonDocument.Parse("{\"language\":\"test\"}");
            var pair = harness.Pairs[CompilerChannel.Root];
            var initialize = CompilerRootSession.InitializeAsync(pair.Transport, new([], language.RootElement), cancellationToken: Token);
            await pair.Read();
            await pair.Send([.. Integer(0), .. Integer(0), .. Integer(42)], WebSocketMessageType.Binary);
            await pair.Read();
            await pair.Send([.. Integer(1), .. Integer(0), .. Integer(1001), .. Integer(1002), .. Integer(1003),
                .. Integer(1004), .. Integer(1005), .. "test"u8], WebSocketMessageType.Binary);
            harness.root = await initialize;
            return harness;
        }
        public Task<CompilerChannelSession> Attach(CompilerChannelSessionLimits? limits = null,
            Func<Uri, CompilerTransportLimits?, CancellationToken, Task<CompilerWebSocketTransport>>? connector = null,
            CancellationToken? cancellationToken = null)
        {
            var next = 0;
            connector ??= (_, _, _) => Task.FromResult(Pairs[(CompilerChannel)(++next)].Transport);
            return CompilerChannelSession.AttachAsync(RootEndpoint, root, limits ?? new(), connector, cancellationToken ?? Token);
        }
        public void Dispose()
        {
            root?.Dispose();
            foreach (var pair in Pairs.Values) pair.Dispose();
        }
    }

    private static byte[] Integer(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return bytes;
    }

    private sealed class Pair(TcpClient client, TcpClient server) : IDisposable
    {
        public CompilerWebSocketTransport Transport { get; } = new(WebSocket.CreateFromStream(client.GetStream(), false, null, Timeout.InfiniteTimeSpan));
        public WebSocket Peer { get; } = WebSocket.CreateFromStream(server.GetStream(), true, null, Timeout.InfiniteTimeSpan);
        public static async Task<Pair> Create()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var client = new TcpClient();
            await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, Token);
            return new(client, await listener.AcceptTcpClientAsync(Token));
        }
        public async Task Send(byte[] bytes, WebSocketMessageType type) => await Peer.SendAsync(bytes.AsMemory(), type, true, Token);
        public async Task<(WebSocketMessageType Type, byte[] Bytes)> Read()
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            var buffer = new byte[4096];
            var message = await Peer.ReceiveAsync(buffer.AsMemory(), deadline.Token);
            Assert.True(message.EndOfMessage);
            return (message.MessageType, buffer[..message.Count]);
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
