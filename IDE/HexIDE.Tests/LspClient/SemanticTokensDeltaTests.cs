using System.Text.Json;
using HexIDE.Conversations;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

public sealed class SemanticTokensDeltaTests : IAsyncDisposable
{
    private readonly List<VBLspClient> _clients = [];
    private readonly List<JsonRpc> _servers = [];
    private readonly ILogger<VBLspClient> _logger = Substitute.For<ILogger<VBLspClient>>();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class Server
    {
        public JsonElement InitializeFrame { get; private set; }
        public List<(string Method, JsonElement Params)> Requests { get; } = [];
        public Queue<string> Replies { get; } = new();
        public bool Delta { get; set; } = true;
        public int TransientErrors { get; set; }
        public bool Error { get; set; }

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement p)
        {
            InitializeFrame = p.Clone();
            return JsonDocument.Parse("""{"capabilities":{"textDocumentSync":1,"semanticTokensProvider":{"full":{"delta":DELTA},"legend":{"tokenTypes":["variable"],"tokenModifiers":[]}}}}""".Replace("DELTA", Delta ? "true" : "false", StringComparison.Ordinal)).RootElement.Clone();
        }

        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }
        [JsonRpcMethod("textDocument/didOpen")]
        public void Open(JsonElement _) { }
        [JsonRpcMethod("textDocument/didClose")]
        public void Close(JsonElement _) { }
        [JsonRpcMethod("textDocument/didChange")]
        public void Change(JsonElement _) { }

        [JsonRpcMethod("textDocument/semanticTokens/full", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Full(JsonElement p) => Reply("full", p);
        [JsonRpcMethod("textDocument/semanticTokens/full/delta", UseSingleObjectParameterDeserialization = true)]
        public JsonElement DeltaReply(JsonElement p) => Reply("delta", p);

        private JsonElement Reply(string method, JsonElement p)
        {
            Requests.Add((method, p.Clone()));
            if (method == "delta" && TransientErrors-- > 0)
                throw new LocalRpcException("Content Modified") { ErrorCode = -32801 };
            if (method == "delta" && Error) throw new LocalRpcException("tokens failed") { ErrorCode = -32603 };
            return JsonDocument.Parse(Replies.Dequeue()).RootElement.Clone();
        }
    }

    private async Task<VBLspClient> Connect(Server server, TimeProvider? clock = null)
    {
        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            var (clientSide, serverSide) = FullDuplexStream.CreatePair();
            var rpc = new JsonRpc(new LspMessageHandler(serverSide, serverSide, new SystemTextJsonFormatter()), server);
            rpc.StartListening();
            _servers.Add(rpc);
            return Task.FromResult<IJsonRpcMessageHandler?>(new LspMessageHandler(clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>()));
        });
        var client = new VBLspClient(transport, _logger, "cpp", timeProvider: clock);
        _clients.Add(client);
        await client.StartAsync(Token);
        await client.OpenDocumentAsync("untitled:tokens.c", "int a;", Token);
        return client;
    }

    [Fact]
    public async Task HandshakeDeclaresDeltaFullRequests()
    {
        var server = new Server();
        await Connect(server);
        server.InitializeFrame.GetProperty("capabilities").GetProperty("textDocument")
            .GetProperty("semanticTokens").GetProperty("requests").GetProperty("full")
            .GetProperty("delta").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task MultipleEditsUsePreviousArrayIndicesAndTheNewResultId()
    {
        var server = new Server();
        server.Replies.Enqueue("""{"resultId":"first","data":[0,1,2,3,4,5,6,7,8,9]}""");
        server.Replies.Enqueue("""{"resultId":"second","edits":[{"start":1,"deleteCount":2,"data":[21]},{"start":5,"deleteCount":2},{"start":10,"deleteCount":0,"data":[30,31]}]}""");
        server.Replies.Enqueue("""{"resultId":"third","edits":[]}""");
        var client = await Connect(server);
        (await client.RequestSemanticTokensAsync("untitled:tokens.c", Token))!.Data.Should().HaveCount(10);
        await client.ChangeDocumentAsync("untitled:tokens.c", 2, "int b;", Token);
        (await client.RequestSemanticTokensAsync("untitled:tokens.c", Token))!.Data.Should().Equal(0,21,3,4,7,8,9,30,31);
        (await client.RequestSemanticTokensAsync("untitled:tokens.c", Token))!.Data.Should().Equal(0,21,3,4,7,8,9,30,31);
        server.Requests.Select(r => r.Method).Should().Equal("full", "delta", "delta");
        server.Requests[1].Params.GetProperty("previousResultId").GetString().Should().Be("first");
        server.Requests[2].Params.GetProperty("previousResultId").GetString().Should().Be("second");
        server.Requests[1].Params.GetProperty("textDocument").GetProperty("uri").GetString().Should().Be("untitled:tokens.c");
    }

    [Fact]
    public async Task FullReplyToDeltaReplacesTheHeldResult()
    {
        var server = new Server();
        server.Replies.Enqueue("""{"resultId":"first","data":[1,2,3]}""");
        server.Replies.Enqueue("""{"resultId":"replacement","data":[4,5]}""");
        server.Replies.Enqueue("""{"edits":[]}""");
        var client = await Connect(server);
        await client.RequestSemanticTokensAsync("untitled:tokens.c", Token);
        (await client.RequestSemanticTokensAsync("untitled:tokens.c", Token))!.Data.Should().Equal(4,5);
        await client.RequestSemanticTokensAsync("untitled:tokens.c", Token);
        server.Requests[2].Params.GetProperty("previousResultId").GetString().Should().Be("replacement");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidEditOrErrorFallsBackToFull(bool error)
    {
        var server = new Server { Error = error };
        server.Replies.Enqueue("""{"resultId":"first","data":[1,2,3]}""");
        if (!error) server.Replies.Enqueue("""{"resultId":"bad","edits":[{"start":4,"deleteCount":0,"data":[9]}]}""");
        server.Replies.Enqueue("""{"resultId":"recovered","data":[4,5]}""");
        var client = await Connect(server);
        await client.RequestSemanticTokensAsync("untitled:tokens.c", Token);
        (await client.RequestSemanticTokensAsync("untitled:tokens.c", Token))!.Data.Should().Equal(4,5);
        if (!error) server.Replies.Enqueue("""{"edits":[{"start":9,"deleteCount":0}]}""");
        server.Replies.Enqueue("""{"resultId":"recovered-again","data":[6]}""");
        (await client.RequestSemanticTokensAsync("untitled:tokens.c", Token))!.Data.Should().Equal(6);
        server.Requests.Select(r => r.Method).Should().Equal("full", "delta", "full", "delta", "full");
        _logger.ReceivedCalls().Count(call => call.GetMethodInfo().Name == "Log"
            && call.GetArguments()[0] is LogLevel.Warning).Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReopenOrReconnectStartsWithFull(bool reconnect)
    {
        var server = new Server();
        server.Replies.Enqueue("""{"resultId":"first","data":[1,2,3]}""");
        server.Replies.Enqueue("""{"resultId":"second","data":[4,5]}""");
        var client = await Connect(server);
        await client.RequestSemanticTokensAsync("untitled:tokens.c", Token);
        if (reconnect) { await client.StopAsync(); await client.StartAsync(Token); }
        else { await client.CloseDocumentAsync("untitled:tokens.c", Token); await client.OpenDocumentAsync("untitled:tokens.c", "int a;", Token); }
        (await client.RequestSemanticTokensAsync("untitled:tokens.c", Token))!.Data.Should().Equal(4,5);
        server.Requests.Select(r => r.Method).Should().Equal("full", "full");
    }

    [Fact]
    public async Task DeltaContentModifiedIsRetried()
    {
        var server = new Server { TransientErrors = 1 };
        server.Replies.Enqueue("""{"resultId":"first","data":[1,2,3]}""");
        server.Replies.Enqueue("""{"resultId":"second","edits":[{"start":0,"deleteCount":1,"data":[9]}]}""");
        var client = await Connect(server);
        await client.RequestSemanticTokensAsync("untitled:tokens.c", Token);
        (await client.RequestSemanticTokensAsync("untitled:tokens.c", Token))!.Data.Should().Equal(9,2,3);
        server.Requests.Select(r => r.Method).Should().Equal("full", "delta", "delta");
    }

    [Fact]
    public async Task ServerWithoutDeltasAlwaysReceivesFull()
    {
        var server = new Server { Delta = false };
        server.Replies.Enqueue("""{"resultId":"first","data":[1]}""");
        server.Replies.Enqueue("""{"resultId":"second","data":[2]}""");
        var client = await Connect(server);
        await client.RequestSemanticTokensAsync("untitled:tokens.c", Token);
        (await client.RequestSemanticTokensAsync("untitled:tokens.c", Token))!.Data.Should().Equal(2);
        server.Requests.Select(r => r.Method).Should().Equal("full", "full");
    }

    [Fact]
    public async Task HeldResultsBelongToTheirDocument()
    {
        var server = new Server();
        server.Replies.Enqueue("""{"resultId":"a","data":[1]}""");
        server.Replies.Enqueue("""{"resultId":"b","data":[2]}""");
        server.Replies.Enqueue("""{"resultId":"a2","edits":[]}""");
        var client = await Connect(server);
        await client.OpenDocumentAsync("untitled:other.c", "int b;", Token);
        await client.RequestSemanticTokensAsync("untitled:tokens.c", Token);
        await client.RequestSemanticTokensAsync("untitled:other.c", Token);
        (await client.RequestSemanticTokensAsync("untitled:tokens.c", Token))!.Data.Should().Equal(1);
        server.Requests.Select(r => r.Method).Should().Equal("full", "full", "delta");
        server.Requests[2].Params.GetProperty("previousResultId").GetString().Should().Be("a");
    }

    [Fact]
    public async Task ReplyWithoutResultIdMakesTheNextRequestFull()
    {
        var server = new Server();
        server.Replies.Enqueue("""{"resultId":"first","data":[1]}""");
        server.Replies.Enqueue("""{"data":[2]}""");
        server.Replies.Enqueue("""{"resultId":"third","data":[3]}""");
        var client = await Connect(server);
        await client.RequestSemanticTokensAsync("untitled:tokens.c", Token);
        await client.RequestSemanticTokensAsync("untitled:tokens.c", Token);
        (await client.RequestSemanticTokensAsync("untitled:tokens.c", Token))!.Data.Should().Equal(3);
        server.Requests.Select(r => r.Method).Should().Equal("full", "delta", "full");
    }

    [Fact]
    public async Task EmptyArrayWithAnIdCanReceiveADelta()
    {
        var server = new Server();
        server.Replies.Enqueue("""{"resultId":"empty","data":[]}""");
        server.Replies.Enqueue("""{"resultId":"populated","edits":[{"start":0,"deleteCount":0,"data":[1]}]}""");
        var client = await Connect(server);
        (await client.RequestSemanticTokensAsync("untitled:tokens.c", Token)).Should().BeNull();
        (await client.RequestSemanticTokensAsync("untitled:tokens.c", Token))!.Data.Should().Equal(1);
        server.Requests.Select(r => r.Method).Should().Equal("full", "delta");
    }

    private sealed class RetryClock : TimeProvider
    {
        public TaskCompletionSource<Action> Delay { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            dueTime.Should().Be(TimeSpan.FromMilliseconds(250));
            var timer = new RetryTimer(callback, state);
            Delay.TrySetResult(timer.Advance);
            return timer;
        }

        private sealed class RetryTimer(TimerCallback callback, object? state) : ITimer
        {
            private int _disposed;
            public void Advance() { if (Volatile.Read(ref _disposed) == 0) callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
            public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    [Theory]
    [InlineData("change")]
    [InlineData("close")]
    [InlineData("reopen")]
    [InlineData("reconnect")]
    [InlineData("stop")]
    [InlineData("cancel")]
    public async Task ObsoleteDeltaRetryStops(string action)
    {
        var server = new Server { TransientErrors = int.MaxValue };
        server.Replies.Enqueue("""{"resultId":"first","data":[1]}""");
        var clock = new RetryClock();
        var client = await Connect(server, clock);
        await client.RequestSemanticTokensAsync("untitled:tokens.c", Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var request = client.RequestSemanticTokensAsync("untitled:tokens.c", cancellation.Token);
        var advance = await clock.Delay.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        switch (action)
        {
            case "change": await client.ChangeDocumentAsync("untitled:tokens.c", 2, "int b;", Token); break;
            case "close": await client.CloseDocumentAsync("untitled:tokens.c", Token); break;
            case "reopen":
                await client.CloseDocumentAsync("untitled:tokens.c", Token);
                await client.OpenDocumentAsync("untitled:tokens.c", "int a;", Token);
                break;
            case "reconnect": await client.StopAsync(); await client.StartAsync(Token); break;
            case "stop": await client.StopAsync(); break;
            case "cancel": await cancellation.CancelAsync(); break;
        }
        advance();
        (await request.WaitAsync(TimeSpan.FromSeconds(5), Token)).Should().BeNull();
        server.Requests.Select(r => r.Method).Should().Equal("full", "delta");
    }

    [ForeignServerFact("cpp")]
    public async Task ClangdDeltaEqualsFullOnAFreshConnection()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "hexide-tokens-" + Guid.NewGuid().ToString("N"))).FullName;
        using var logs = LoggerFactory.Create(_ => { });
        await using var capture = new ConversationLog();
        capture.Arm("delta", true);
        VBLspClient NewClient(string id) => new(new StdioProcessLspTransport(
            new LspServerInfo(ForeignServer.Cpp.Find()!, ForeignServer.Cpp.ServerArguments, dir), logs.CreateLogger<StdioProcessLspTransport>()),
            logs.CreateLogger<VBLspClient>(), "cpp", capture: capture, connectionId: id);
        await using var client = NewClient("delta");
        await using var fresh = NewClient("fresh");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, ".clangd"), "Index:\n  StandardLibrary: No\n", Token);
            var file = Path.Combine(dir, "tokens.c");
            const string initial = "int main() { int a = 1; return a; }\n";
            const string edited = "int main() { int longer = 1; return longer; }\n";
            await File.WriteAllTextAsync(file, initial, Token);
            var uri = new Uri(file).AbsoluteUri;
            await client.StartAsync(Token);
            await client.OpenDocumentAsync(uri, initial, Token);
            (await client.RequestSemanticTokensAsync(uri, Token)).Should().NotBeNull();
            await client.ChangeDocumentAsync(uri, 2, edited, Token);
            var delta = await client.RequestSemanticTokensAsync(uri, Token);
            delta.Should().NotBeNull();
            await capture.DrainAsync();
            capture.Snapshot("delta").Should().Contain(e => e.Direction == ConversationDirection.Sent && e.Method == "textDocument/semanticTokens/full/delta");
            await fresh.StartAsync(Token);
            await fresh.OpenDocumentAsync(uri, edited, Token);
            var full = await fresh.RequestSemanticTokensAsync(uri, Token);
            full.Should().NotBeNull();
            delta!.Data.Should().Equal(full!.Data);
        }
        finally { await client.StopAsync(); await fresh.StopAsync(); Directory.Delete(dir, true); }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients) await client.DisposeAsync();
        foreach (var server in _servers) server.Dispose();
    }
}
