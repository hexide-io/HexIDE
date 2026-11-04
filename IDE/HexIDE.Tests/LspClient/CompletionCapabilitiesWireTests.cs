using System.Text.Json;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

public class CompletionCapabilitiesWireTests
{
    private sealed class HandshakeRecorder
    {
        private readonly TaskCompletionSource<JsonElement> _initialize =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<JsonElement> InitializeParams => _initialize.Task;

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement p)
        {
            _initialize.TrySetResult(p.Clone());
            return JsonDocument.Parse("""{"capabilities":{}}""").RootElement.Clone();
        }

        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }
    }

    [Fact]
    public async Task InitializeDeclaresOnlySupportedCompletionCapabilities()
    {
        var server = new HandshakeRecorder();
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();
        var serverRpc = new JsonRpc(
            new HeaderDelimitedMessageHandler(serverSide, serverSide, new SystemTextJsonFormatter()), server);
        serverRpc.StartListening();

        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>())));

        var logger = Substitute.For<ILogger<VBLspClient>>();
        var client = new VBLspClient(transport, logger, "markdown");
        await using var ownedClient = client;
        using var ownedServer = serverRpc;
        await client.StartAsync(TestContext.Current.CancellationToken);
        var initialize = await server.InitializeParams.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var completion = initialize.GetProperty("capabilities").GetProperty("textDocument").GetProperty("completion");
        completion.GetProperty("completionItemKind").GetProperty("valueSet")
            .EnumerateArray().Select(value => value.GetInt32()).Should().Equal(Enumerable.Range(1, 25));
        completion.GetProperty("insertTextMode").GetInt32().Should().Be(1);
        completion.EnumerateObject().Select(property => property.Name).Should()
            .BeEquivalentTo("completionItemKind", "insertTextMode", "completionItem");
        var item = completion.GetProperty("completionItem");
        item.GetProperty("preselectSupport").GetBoolean().Should().BeTrue();
        item.GetProperty("documentationFormat").EnumerateArray().Select(value => value.GetString())
            .Should().Equal("plaintext");
    }
    [Fact]
    public async Task InitializeDeclaresDiagnosticPresentationCapabilities()
    {
        var server = new HandshakeRecorder();
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();
        var serverRpc = new JsonRpc(
            new HeaderDelimitedMessageHandler(serverSide, serverSide, new SystemTextJsonFormatter()), server);
        serverRpc.StartListening();

        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>())));

        var logger = Substitute.For<ILogger<VBLspClient>>();
        var client = new VBLspClient(transport, logger, "markdown");
        await using var ownedClient = client;
        using var ownedServer = serverRpc;
        await client.StartAsync(TestContext.Current.CancellationToken);
        var initialize = await server.InitializeParams.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var diagnostics = initialize.GetProperty("capabilities").GetProperty("textDocument").GetProperty("publishDiagnostics");
        diagnostics.GetProperty("relatedInformation").GetBoolean().Should().BeTrue();
        diagnostics.GetProperty("tagSupport").GetProperty("valueSet").EnumerateArray()
            .Select(v => v.GetInt32()).Should().Equal(1, 2);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitializeDeclaresTheHoverRenderersFormats(bool compareWithRenderer)
    {
        var server = new HandshakeRecorder();
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();
        var serverRpc = new JsonRpc(
            new HeaderDelimitedMessageHandler(serverSide, serverSide, new SystemTextJsonFormatter()), server);
        serverRpc.StartListening();

        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>())));

        var logger = Substitute.For<ILogger<VBLspClient>>();
        var client = new VBLspClient(transport, logger, "markdown");
        await using var ownedClient = client;
        using var ownedServer = serverRpc;
        await client.StartAsync(TestContext.Current.CancellationToken);
        var initialize = await server.InitializeParams.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var formats = initialize.GetProperty("capabilities").GetProperty("textDocument")
            .GetProperty("hover").GetProperty("contentFormat").EnumerateArray().Select(value => value.GetString()).ToArray();
        if (compareWithRenderer) formats.Should().Equal(HoverContentRenderer.Formats);
        else formats.Should().Equal("markdown", "plaintext");
    }
}
