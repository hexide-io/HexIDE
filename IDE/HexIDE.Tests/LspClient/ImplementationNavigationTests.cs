using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

public class ImplementationNavigationTests
{
    private sealed class Server(string capability, string advertised, string answer)
    {
        public JsonElement Initialization;
        public readonly List<(string Method, JsonElement Params)> Requests = [];

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement p)
        {
            Initialization = p.Clone();
            return JsonDocument.Parse("{\"capabilities\":{\"" + capability + "\":" + advertised + "}}").RootElement.Clone();
        }

        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }

        [JsonRpcMethod("textDocument/implementation", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Implementation(JsonElement p) => Reply("implementation", p);

        [JsonRpcMethod("textDocument/typeDefinition", UseSingleObjectParameterDeserialization = true)]
        public JsonElement TypeDefinition(JsonElement p) => Reply("typeDefinition", p);

        private JsonElement Reply(string method, JsonElement p)
        {
            Requests.Add((method, p.Clone()));
            return JsonDocument.Parse(answer).RootElement.Clone();
        }
    }

    [Theory]
    [InlineData("implementation", "true", "single")]
    [InlineData("implementation", "{}", "array")]
    [InlineData("implementation", "true", "links")]
    [InlineData("typeDefinition", "{}", "single")]
    [InlineData("typeDefinition", "true", "array")]
    [InlineData("typeDefinition", "{}", "links")]
    [InlineData("implementation", "false", "single")]
    [InlineData("typeDefinition", "null", "single")]
    public async Task NegotiatesGatesAndReadsTheServerAnswer(string method, string advertised, string shape)
    {
        const string location = """{"uri":"file:///target.cls","range":{"start":{"line":7,"character":3},"end":{"line":7,"character":8}}}""";
        const string link = """[{"targetUri":"file:///target.cls","targetRange":{"start":{"line":0,"character":0},"end":{"line":9,"character":0}},"targetSelectionRange":{"start":{"line":7,"character":3},"end":{"line":7,"character":8}}}]""";
        var server = new Server(method + "Provider", advertised, shape switch { "array" => "[" + location + "]", "links" => link, _ => location });
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();
        using var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(serverSide, serverSide, new SystemTextJsonFormatter()), server);
        rpc.StartListening();
        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(new HeaderDelimitedMessageHandler(clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>())));
        await using var client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), DocumentLanguage.Vb6);
        var ct = TestContext.Current.CancellationToken;
        await client.StartAsync(ct);
        foreach (var name in new[] { "implementation", "typeDefinition" })
            server.Initialization.GetProperty("capabilities").GetProperty("textDocument").GetProperty(name).GetProperty("linkSupport").GetBoolean().Should().BeTrue();
        var result = method == "implementation"
            ? await client.RequestImplementationAsync("file:///source.bas", new(2, 4), ct)
            : await client.RequestTypeDefinitionAsync("file:///source.bas", new(2, 4), ct);
        var enabled = advertised is "true" or "{}";
        client.SupportsCapability("file:///source.bas", method + "Provider").Should().Be(enabled);
        client.SupportsCapability("file:///source.bas", (method == "implementation" ? "typeDefinition" : "implementation") + "Provider").Should().BeFalse();
        if (!enabled)
        {
            result.Should().BeNull();
            server.Requests.Should().BeEmpty();
            return;
        }
        server.Requests.Should().ContainSingle();
        server.Requests[0].Method.Should().Be(method);
        server.Requests[0].Params.GetProperty("textDocument").GetProperty("uri").GetString().Should().Be("file:///source.bas");
        server.Requests[0].Params.GetProperty("position").GetProperty("line").GetInt32().Should().Be(2);
        server.Requests[0].Params.GetProperty("position").GetProperty("character").GetInt32().Should().Be(4);
        result.Should().ContainSingle();
        result![0].Uri.Should().Be("file:///target.cls");
        result[0].Range.Start.Should().Be(new Position(7, 3));
    }
}

public class ImplementationNavigationRoutingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RegistryRoutesOnlyToTheDocumentsClaimant(bool implementation)
    {
        var client = Substitute.For<ILspClient>();
        client.IsRunning.Returns(true);
        client.SupportsCapability(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        var target = new Location("untitled:P/Target.cls", new(new(2, 3), new(2, 8)));
        client.RequestImplementationAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>()).Returns([target]);
        client.RequestTypeDefinitionAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>()).Returns([target]);
        await using var registry = new LspClientRegistry([new LanguageServerRegistration("test", "Test", [".bas"], "vb6", () => client)], Substitute.For<ILogger<LspClientRegistry>>());
        var ct = TestContext.Current.CancellationToken;
        const string uri = "untitled:P/Source.bas";
        await registry.OpenDocumentAsync(uri, "", ct);
        registry.SupportsCapability(uri, "implementationProvider").Should().BeTrue();
        registry.SupportsCapability("untitled:P/Other.txt", "implementationProvider").Should().BeFalse();
        var result = implementation ? await registry.RequestImplementationAsync(uri, new(1, 2), ct)
            : await registry.RequestTypeDefinitionAsync(uri, new(1, 2), ct);
        result.Should().Equal(target);
        var unclaimed = implementation ? await registry.RequestImplementationAsync("untitled:P/Other.txt", new(1, 2), ct)
            : await registry.RequestTypeDefinitionAsync("untitled:P/Other.txt", new(1, 2), ct);
        unclaimed.Should().BeNull();
    }
}
