using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;

namespace HexIDE.TwinBasic.Tests;

public sealed class CompilerChannelSessionTests : SocketTrackingTest
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task Failed_peer_creation_disposes_sockets(int acquisition)
    {
        var remaining = acquisition;
        AfterAcquire = () =>
        {
            if (--remaining == 0) throw new IOException("Injected peer creation failure");
        };
        await FluentActions.Awaiting(async () => { using var peer = await Harness.Create(); })
            .Should().ThrowExactlyAsync<IOException>().WithMessage("Injected peer creation failure");
        AssertNoOpenSockets();
    }

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
        endpoints.Select(uri => uri.AbsoluteUri).Should().Equal(new[] { "ws://127.0.0.1:1001/key%2Fopaque/fs", "ws://127.0.0.1:1003/key%2Fopaque/language",
            "ws://127.0.0.1:1002/key%2Fopaque/debugger" });
        session.Connection.ProcessId.Should().Be(42);
        session.Completion.IsCompleted.Should().BeFalse();
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
            await session.SendAsync(channel, new InMemoryCompilerSessionCodec.ChannelData(WebSocketMessageType.Text, "opaque"u8.ToArray()), Token);
            var sent = await harness.Pairs[channel].Read();
            sent.Type.Should().Be(WebSocketMessageType.Text);
            sent.Bytes.Should().Equal("opaque"u8.ToArray());
        }
        var last = new Dictionary<CompilerChannel, byte>();
        for (var i = 0; i < 8; i++)
        {
            var message = await session.ReceiveAsync(Token).WaitAsync(TimeSpan.FromSeconds(5), Token);
            var data = message.Data.Should().BeOfType<InMemoryCompilerSessionCodec.ChannelData>().Which;
            data.Type.Should().Be(WebSocketMessageType.Binary);
            data.Bytes[0].Should().Be((byte)(last.GetValueOrDefault(message.Channel) + 1));
            last[message.Channel] = data.Bytes[0];
        }
        last.Count.Should().Be(4);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Failed_attachment_disposes_root_and_every_acquired_socket(int failAt)
    {
        using var harness = await Harness.Create();
        var calls = 0;
        await FluentActions.Awaiting(() => harness.Attach(connector: (_, _, _) =>
        {
            if (calls++ == failAt) throw new IOException("Connection refused.");
            return Task.FromResult(harness.Pairs[(CompilerChannel)calls].Transport);
        })).Should().ThrowExactlyAsync<IOException>();
        for (var i = 0; i <= failAt; i++)
            await FluentActions.Awaiting(() => harness.Pairs[(CompilerChannel)i].Transport.ReceiveAsync(Token)).Should().ThrowExactlyAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Cancelled_attachment_closes_already_acquired_sockets()
    {
        using var harness = await Harness.Create();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        await FluentActions.Awaiting(() => harness.Attach(connector: (_, _, _) =>
        {
            cancel.Cancel();
            return Task.FromResult(harness.Pairs[CompilerChannel.FileSystem].Transport);
        }, cancellationToken: cancel.Token)).Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Awaiting(() => harness.Pairs[CompilerChannel.Root].Transport.ReceiveAsync(Token)).Should().ThrowExactlyAsync<ObjectDisposedException>();
        await FluentActions.Awaiting(() => harness.Pairs[CompilerChannel.FileSystem].Transport.ReceiveAsync(Token)).Should().ThrowExactlyAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Attachment_deadline_covers_a_stalled_connector_and_closes_root()
    {
        using var harness = await Harness.Create();
        await FluentActions.Awaiting(() => harness.Attach(new(ConnectionTimeout: TimeSpan.FromMilliseconds(100)),
            connector: async (_, _, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return harness.Pairs[CompilerChannel.FileSystem].Transport;
            })).Should().ThrowExactlyAsync<TimeoutException>();
        await FluentActions.Awaiting(() => harness.Pairs[CompilerChannel.Root].Transport.ReceiveAsync(Token)).Should().ThrowExactlyAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Session_message_limit_also_applies_to_a_root_created_with_larger_limits()
    {
        using var harness = await Harness.Create();
        await using var session = await harness.Attach(new(MaximumMessageBytes: 32));
        await harness.Pairs[CompilerChannel.Root].Send(new byte[33], WebSocketMessageType.Binary);
        (await session.Completion.WaitAsync(TimeSpan.FromSeconds(5), Token)).WasDisposed.Should().BeFalse();
        await FluentActions.Awaiting(() => session.ReceiveAsync(Token)).Should().ThrowExactlyAsync<IOException>();
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
        end.WasDisposed.Should().BeFalse();
        end.Error.Should().Contain(channel.ToString());
        end.Error.Should().NotContain("private peer detail");
        await FluentActions.Awaiting(() => pending).Should().ThrowExactlyAsync<IOException>();
        foreach (var pair in harness.Pairs.Values)
            await FluentActions.Awaiting(() => pair.Transport.ReceiveAsync(Token)).Should().ThrowExactlyAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Disposal_joins_readers_and_is_idempotent()
    {
        using var harness = await Harness.Create();
        var session = await harness.Attach();
        var pending = session.ReceiveAsync(Token);
        await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), Token);
        await session.DisposeAsync();
        (await session.Completion).WasDisposed.Should().BeTrue();
        await FluentActions.Awaiting(() => pending).Should().ThrowExactlyAsync<ObjectDisposedException>();
        await FluentActions.Awaiting(() => session.SendAsync(CompilerChannel.Root, new InMemoryCompilerSessionCodec.ChannelData(WebSocketMessageType.Binary, new byte[1]), Token)).Should().ThrowExactlyAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Cancelling_consumer_does_not_abort_socket_readers()
    {
        using var harness = await Harness.Create();
        await using var session = await harness.Attach();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var pending = session.ReceiveAsync(cancel.Token);
        cancel.Cancel();
        await FluentActions.Awaiting(() => pending).Should().ThrowAsync<OperationCanceledException>();
        await harness.Pairs[CompilerChannel.Language].Send([7], WebSocketMessageType.Binary);
        ((InMemoryCompilerSessionCodec.ChannelData)(await session.ReceiveAsync(Token)).Data).Bytes[0].Should().Be(7);
        session.Completion.IsCompleted.Should().BeFalse();
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
        end.WasDisposed.Should().BeFalse();
        end.Error.Should().Contain("limit");
        await FluentActions.Awaiting(() => session.ReceiveAsync(Token)).Should().ThrowExactlyAsync<IOException>();
    }

    [Fact]
    public async Task Consuming_messages_releases_queue_byte_budget()
    {
        using var harness = await Harness.Create();
        await using var session = await harness.Attach(new(MaximumQueuedBytes: 2));
        for (var i = 0; i < 3; i++)
        {
            await harness.Pairs[CompilerChannel.Language].Send([1, 2], WebSocketMessageType.Binary);
            ((InMemoryCompilerSessionCodec.ChannelData)(await session.ReceiveAsync(Token)).Data).Bytes.Length.Should().Be(2);
        }
        session.Completion.IsCompleted.Should().BeFalse();
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
        session.Completion.IsCompleted.Should().BeFalse();
        await harness.Pairs[channel].Peer.SendAsync(new byte[1].AsMemory(), WebSocketMessageType.Binary, false, Token);
        var end = await session.Completion.WaitAsync(TimeSpan.FromSeconds(5), Token);
        end.Error.Should().Contain("timed out");
    }

    [Fact]
    public async Task Precancelled_send_does_not_end_session()
    {
        using var harness = await Harness.Create();
        await using var session = await harness.Attach();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        cancel.Cancel();
        await FluentActions.Awaiting(() => session.SendAsync(CompilerChannel.Root, new InMemoryCompilerSessionCodec.ChannelData(WebSocketMessageType.Binary, new byte[1]), cancel.Token)).Should().ThrowAsync<OperationCanceledException>();
        session.Completion.IsCompleted.Should().BeFalse();
    }

    private sealed class Harness : IDisposable
    {
        public Dictionary<CompilerChannel, Pair> Pairs { get; } = [];
        private CompilerRootSession root = null!;
        public static async Task<Harness> Create()
        {
            using var resources = new TestSocketResources();
            var harness = resources.Own(new Harness());
            foreach (var channel in Enum.GetValues<CompilerChannel>()) harness.Pairs.Add(channel, await Pair.Create());
            var codec = new InMemoryCompilerSessionCodec();
            var pair = harness.Pairs[CompilerChannel.Root];
            var initialize = CompilerRootSession.InitializeAsync(pair.Transport, InMemoryCompilerSessionCodec.Options,
                cancellationToken: Token, codec: codec);
            await pair.Read();
            await pair.Send(codec.Reply(0, value: new CompilerProcessInfo(42)), WebSocketMessageType.Binary);
            await pair.Read();
            await pair.Send(codec.Reply(1, value: new CompilerChannelMetadata(1001, 1002, 1003, 1004, 1005, "test")), WebSocketMessageType.Binary);
            harness.root = await initialize;
            resources.Complete();
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

    private sealed class Pair(TcpClient client, TcpClient server) : IDisposable
    {
        public CompilerWebSocketTransport Transport { get; } = new(WebSocket.CreateFromStream(client.GetStream(), false, null, Timeout.InfiniteTimeSpan));
        public WebSocket Peer { get; } = WebSocket.CreateFromStream(server.GetStream(), true, null, Timeout.InfiniteTimeSpan);
        public static async Task<Pair> Create()
        {
            using var resources = new TestSocketResources();
            using var listener = SocketTrackingTest.Track(new TcpListener(IPAddress.Loopback, 0));
            listener.Start();
            var client = resources.Own(new TcpClient());
            await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, Token);
            var server = resources.Own(await listener.AcceptTcpClientAsync(Token));
            var pair = resources.Own(new Pair(client, server));
            resources.Complete();
            return pair;
        }
        public async Task Send(byte[] bytes, WebSocketMessageType type) => await Peer.SendAsync(bytes.AsMemory(), type, true, Token);
        public async Task<(WebSocketMessageType Type, byte[] Bytes)> Read()
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            var buffer = new byte[4096];
            var message = await Peer.ReceiveAsync(buffer.AsMemory(), deadline.Token);
            message.EndOfMessage.Should().BeTrue();
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
