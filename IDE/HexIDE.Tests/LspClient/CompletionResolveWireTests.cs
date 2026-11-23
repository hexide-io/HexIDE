using System.Text.Json;
using System.Text.Json.Serialization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

public sealed class CompletionResolveWireTests
{
    private const string Uri = "file:///module.bas";
    private const string Data = """{ "token": "\u03a9", "values": [1.00, 2] }""";

    private sealed class Server(string label, string providerOptions, bool failResolve = false, string? data = Data) : IDisposable
    {
        private readonly (Stream Client, Stream Server) _streams = FullDuplexStream.CreatePair();
        private JsonRpc? _rpc;
        public JsonElement? ResolveParameters { get; private set; }

        public VBLspClient CreateClient()
        {
            var formatter = new SystemTextJsonFormatter();
            formatter.JsonSerializerOptions.Converters.Add(new RawElementConverter());
            _rpc = new JsonRpc(new LspMessageHandler(_streams.Server, _streams.Server, formatter), this);
            _rpc.StartListening();
            var transport = Substitute.For<ILspTransport>();
            transport.IsAlive.Returns(true);
            transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult<IJsonRpcMessageHandler?>(
                    new LspMessageHandler(_streams.Client, _streams.Client, call.Arg<IJsonRpcMessageFormatter>())));
            return new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), DocumentLanguage.Vb6);
        }

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) =>
            Parse($$$"""{"capabilities":{"completionProvider":{{{providerOptions}}}}}""");

        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }

        [JsonRpcMethod("textDocument/completion", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Complete(JsonElement _)
        {
            var dataMember = data is null ? "" : $""","data":{data}""";
            return Parse($$"""[{"label":"{{label}}","kind":3,"detail":"initial","insertText":"original"{{dataMember}}}]""");
        }

        [JsonRpcMethod("completionItem/resolve", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Resolve(JsonElement parameters)
        {
            ResolveParameters = parameters.Clone();
            if (failResolve) throw new InvalidOperationException("Resolve failed");
            return Parse($$"""{"label":"different","kind":1,"detail":"resolved","documentation":"docs for {{label}}","insertText":"different"}""");
        }

        public void Dispose()
        {
            _rpc?.Dispose();
            _streams.Client.Dispose();
            _streams.Server.Dispose();
        }

        private static JsonElement Parse(string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
    }

    private sealed class RawElementConverter : JsonConverter<JsonElement>
    {
        public override JsonElement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return document.RootElement.Clone();
        }

        public override void Write(Utf8JsonWriter writer, JsonElement value, JsonSerializerOptions options) =>
            writer.WriteRawValue(value.GetRawText());
    }

    private static LspClientRegistry Registry(params Server[] servers) =>
        new(servers.Select((server, index) => new LanguageServerRegistration(
            $"server-{index}", $"server-{index}", [".bas"], DocumentLanguage.Vb6, server.CreateClient)).ToArray(),
            Substitute.For<ILogger<LspClientRegistry>>());

    [Fact]
    public async Task EachMergedItemResolvesOnItsOwnerWithOriginalData()
    {
        var ct = TestContext.Current.CancellationToken;
        using var first = new Server("First", """{"resolveProvider":true}""");
        using var second = new Server("Second", """{"resolveProvider":true}""");
        await using var registry = Registry(first, second);
        await registry.OpenDocumentAsync(Uri, "code", ct);
        var items = await registry.RequestCompletionAsync(Uri, new(0, 0), ct);
        items.Select(item => item.Label).Should().Equal("First", "Second");

        ILspClient client = registry;
        var resolved = await client.ResolveCompletionItemAsync(items[1], ct);
        first.ResolveParameters.Should().BeNull();
        second.ResolveParameters.Should().NotBeNull();
        var parameters = second.ResolveParameters!.Value;
        parameters.GetProperty("label").GetString().Should().Be("Second");
        parameters.GetProperty("data").GetRawText().Should().Be(Data);
        resolved.Should().Be(items[1] with { Detail = "resolved", Documentation = new("plaintext", "docs for Second") });

        (await client.ResolveCompletionItemAsync(items[0], ct))!.Documentation!.Value.Should().Be("docs for First");
        first.ResolveParameters!.Value.GetProperty("label").GetString().Should().Be("First");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"resolveProvider":false}""")]
    public async Task OnlyAnAdvertisingServerReceivesAResolveRequest(string options)
    {
        var ct = TestContext.Current.CancellationToken;
        using var silent = new Server("Silent", options);
        using var resolving = new Server("Resolving", """{"resolveProvider":true}""", data: null);
        await using var registry = Registry(silent, resolving);
        await registry.OpenDocumentAsync(Uri, "code", ct);
        var items = await registry.RequestCompletionAsync(Uri, new(0, 0), ct);
        ILspClient client = registry;
        (await client.ResolveCompletionItemAsync(items[0], ct)).Should().BeNull();
        silent.ResolveParameters.Should().BeNull();
        (await client.ResolveCompletionItemAsync(items[1], ct)).Should().NotBeNull();
        resolving.ResolveParameters.Should().NotBeNull();
        resolving.ResolveParameters!.Value.TryGetProperty("data", out _).Should().BeFalse();
    }

    [Fact]
    public async Task AResolveErrorLeavesTheOriginalItemAvailable()
    {
        var ct = TestContext.Current.CancellationToken;
        using var server = new Server("Original", """{"resolveProvider":true}""", failResolve: true);
        await using var registry = Registry(server);
        await registry.OpenDocumentAsync(Uri, "code", ct);
        var item = (await registry.RequestCompletionAsync(Uri, new(0, 0), ct)).Should().ContainSingle().Subject;
        ILspClient client = registry;
        (await client.ResolveCompletionItemAsync(item, ct)).Should().BeNull();
        server.ResolveParameters.Should().NotBeNull();
        server.ResolveParameters!.Value.GetProperty("label").GetString().Should().Be("Original");
        item.Detail.Should().Be("initial");
        item.InsertText.Should().Be("original");
        item.Documentation.Should().BeNull();
    }
}
