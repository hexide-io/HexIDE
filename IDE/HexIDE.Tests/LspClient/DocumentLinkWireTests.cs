using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

public sealed class DocumentLinkWireTests
{
    private sealed class Server(bool offers)
    {
        internal JsonElement Capabilities;
        internal JsonElement Request;
        internal int Requests;
        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement parameters)
        {
            Capabilities = parameters.GetProperty("capabilities").Clone();
            return JsonSerializer.SerializeToElement(new Dictionary<string, object>
            {
                ["capabilities"] = offers ? new Dictionary<string, object> { ["documentLinkProvider"] = new Dictionary<string, object>() }
                    : new Dictionary<string, object>()
            });
        }
        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }
        [JsonRpcMethod("textDocument/documentLink", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Links(JsonElement parameters)
        {
            Requests++;
            Request = parameters.Clone();
            return JsonDocument.Parse("""
                [{"range":{"start":{"line":1,"character":2},"end":{"line":1,"character":7}},
                  "target":"file:///other.h#L2","tooltip":"Open header"},
                 {"range":{"start":{"line":2,"character":0},"end":{"line":2,"character":1}}}]
                """).RootElement.Clone();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeclaresTooltipAndRequestsOnlyWhenOffered(bool offers)
    {
        var token = TestContext.Current.CancellationToken;
        var (local, peer) = FullDuplexStream.CreatePair();
        var server = new Server(offers);
        using var rpc = new JsonRpc(new LspMessageHandler(peer, peer, new SystemTextJsonFormatter()), server);
        rpc.StartListening();
        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.LeaveServerRunning.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IJsonRpcMessageHandler?>(
                new LspMessageHandler(local, local, call.Arg<IJsonRpcMessageFormatter>())));
        await using var client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), "c");
        await client.StartAsync(token);
        server.Capabilities.GetProperty("textDocument").GetProperty("documentLink")
            .GetProperty("tooltipSupport").GetBoolean().Should().BeTrue();
        var links = await client.RequestDocumentLinksAsync("file:///main.c", token);
        server.Requests.Should().Be(offers ? 1 : 0);
        if (!offers) { links.Should().BeNull(); return; }
        server.Request.GetProperty("textDocument").GetProperty("uri").GetString().Should().Be("file:///main.c");
        links.Should().NotBeNull();
        links!.Should().HaveCount(2);
        links[0].Range.Should().Be(new HexIDE.Lsp.Messages.Range(new(1, 2), new(1, 7)));
        links[0].Target.Should().Be("file:///other.h#L2");
        links[0].Tooltip.Should().Be("Open header");
        links[1].Target.Should().BeNull();
    }
}
