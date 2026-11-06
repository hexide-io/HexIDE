using System.Text.Json;
using System.Threading.Channels;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

public class DocumentCountNotificationTests
{
    private const string Uri = "file:///projects/tool.py";
    private const string SecondUri = "file:///projects/other.py";

    private sealed class RecordingServer(int syncKind)
    {
        public readonly Channel<JsonElement> Opened = Channel.CreateUnbounded<JsonElement>();
        public readonly TaskCompletionSource<JsonElement> Closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) => JsonDocument.Parse(
            $$$$"""{"capabilities":{"textDocumentSync":{"openClose":true,"change":{{{{syncKind}}}}}}}""").RootElement.Clone();

        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }

        [JsonRpcMethod("textDocument/didOpen", UseSingleObjectParameterDeserialization = true)]
        public void DidOpen(JsonElement p) => Opened.Writer.TryWrite(p.Clone());

        [JsonRpcMethod("textDocument/didClose", UseSingleObjectParameterDeserialization = true)]
        public void DidClose(JsonElement p) => Closed.TrySetResult(p.Clone());

        [JsonRpcMethod("shutdown")]
        public object? Shutdown() => null;

        [JsonRpcMethod("exit")]
        public void Exit() { }
    }

    private sealed class Connection : IAsyncDisposable
    {
        public RecordingServer Server { get; }
        public LspClientRegistry Registry { get; }
        private readonly JsonRpc _serverRpc;

        public Connection(int syncKind)
        {
            Server = new RecordingServer(syncKind);
            var (clientSide, serverSide) = FullDuplexStream.CreatePair();
            _serverRpc = new JsonRpc(new HeaderDelimitedMessageHandler(
                serverSide, serverSide, new SystemTextJsonFormatter()), Server);
            _serverRpc.StartListening();
            var transport = Substitute.For<ILspTransport>();
            transport.IsAlive.Returns(true);
            transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
                .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(new HeaderDelimitedMessageHandler(
                    clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>())));
            Registry = new LspClientRegistry(
                [new("python", "Python", [".py"], "python", () =>
                    new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), "python"))],
                Substitute.For<ILogger<LspClientRegistry>>());
        }

        public async ValueTask DisposeAsync()
        {
            await Registry.DisposeAsync();
            _serverRpc.Dispose();
        }
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public async Task OpenRefreshesTheCardSnapshotAfterTracking(int syncKind, bool alreadyRunning)
    {
        await using var connection = new Connection(syncKind);
        var registry = connection.Registry;
        if (alreadyRunning)
        {
            await registry.OpenDocumentAsync(SecondUri, "pass", false, TestContext.Current.CancellationToken);
            await connection.Server.Opened.Reader.ReadAsync(TestContext.Current.CancellationToken);
        }

        // Like the tool window, retain a snapshot and refresh only when the registry tells us to.
        var snapshot = registry.Connections.Single();
        registry.ConnectionsChanged += (_, _) => snapshot = registry.Connections.Single();
        await registry.OpenDocumentAsync(Uri, "pass", false, TestContext.Current.CancellationToken);
        var opened = await connection.Server.Opened.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        opened.GetProperty("textDocument").GetProperty("uri").GetString().Should().Be(Uri);
        registry.Connections.Single().OpenDocumentCount.Should().Be(alreadyRunning ? 2 : 1);
        snapshot.OpenDocumentCount.Should().Be(alreadyRunning ? 2 : 1);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public async Task CloseRefreshesTheCardSnapshotAfterTracking(int syncKind, bool forRename)
    {
        await using var connection = new Connection(syncKind);
        var registry = connection.Registry;
        await registry.OpenDocumentAsync(Uri, "pass", false, TestContext.Current.CancellationToken);
        var snapshot = registry.Connections.Single();
        registry.ConnectionsChanged += (_, _) => snapshot = registry.Connections.Single();

        if (forRename) await registry.CloseDocumentForRenameAsync(Uri, TestContext.Current.CancellationToken);
        else await registry.CloseDocumentAsync(Uri, TestContext.Current.CancellationToken);

        var closed = await connection.Server.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        closed.GetProperty("textDocument").GetProperty("uri").GetString().Should().Be(Uri);
        registry.Connections.Single().OpenDocumentCount.Should().Be(0);
        snapshot.OpenDocumentCount.Should().Be(0);
    }
}
