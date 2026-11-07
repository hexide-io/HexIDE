using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

public class CompletionReplyShapeTests : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _disposables = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var d in _disposables)
        {
            try { await d.DisposeAsync(); } catch { /* teardown is best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    // ── Reply shapes, read off a real connection rather than through the parser ───────

    /// <summary>A server advertising completion and answering with whatever JSON it was given.</summary>
    private sealed class StubServer(string replyJson)
    {
        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) =>
            JsonDocument.Parse("""{"capabilities":{"completionProvider":{}}}""").RootElement.Clone();

        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }

        public JsonElement? Parameters { get; private set; }

        [JsonRpcMethod("textDocument/completion", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Completion(JsonElement parameters)
        {
            Parameters = parameters.Clone();
            return JsonDocument.Parse(replyJson).RootElement.Clone();
        }
    }

    private async Task<CompletionItem[]> Read(string replyJson)
    {
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();
        var server = new StubServer(replyJson);
        var serverRpc = new JsonRpc(
            new HeaderDelimitedMessageHandler(serverSide, serverSide, new SystemTextJsonFormatter()),
            server);
        serverRpc.StartListening();

        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>())));

        var client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), DocumentLanguage.Vb6);
        _disposables.Add(client);
        await client.StartAsync(TestContext.Current.CancellationToken);

        var answered = false;
        var result = await client.RequestCompletionAsync("file:///module.bas", new Position(2, 3),
            TestContext.Current.CancellationToken, () => answered = true);
        answered.Should().BeTrue();
        server.Parameters!.Value.GetProperty("textDocument").GetProperty("uri").GetString()
            .Should().Be("file:///module.bas");
        server.Parameters.Value.GetProperty("position").GetProperty("line").GetInt32().Should().Be(2);
        return result;
    }

    [Theory]
    [InlineData("[{\"label\":\"Calc\",\"kind\":3,\"detail\":\"Function\",\"insertText\":\"Calc()\"}]")]
    [InlineData("{\"isIncomplete\":true,\"items\":[{\"label\":\"Calc\",\"kind\":3,\"detail\":\"Function\",\"insertText\":\"Calc()\"}]}")]
    public async Task BothCompletionReplyShapesKeepTheItems(string json)
    {
        var item = (await Read(json)).Should().ContainSingle().Subject;
        item.Label.Should().Be("Calc");
        item.Detail.Should().Be("Function");
        item.InsertText.Should().Be("Calc()");
        item.Kind.Should().Be(CompletionItemKind.Function);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"isIncomplete\":false,\"items\":[]}")]
    public async Task EmptyAnswersHaveNoItems(string json) => (await Read(json)).Should().BeEmpty();
}
