using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

public class SelectionRangeTests
{
    private sealed class Server(string provider, string answer)
    {
        public JsonElement InitializeParams { get; private set; }
        public JsonElement Request { get; private set; }
        public int Calls { get; private set; }

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement p)
        {
            InitializeParams = p.Clone();
            return JsonDocument.Parse("{\"capabilities\":{\"selectionRangeProvider\":" + provider + "}}").RootElement.Clone();
        }

        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }

        [JsonRpcMethod("textDocument/selectionRange", UseSingleObjectParameterDeserialization = true)]
        public JsonElement SelectionRange(JsonElement p)
        {
            Calls++;
            Request = p.Clone();
            if (answer == "error") throw new InvalidOperationException("Selection ranges unavailable");
            if (answer == "null") return JsonDocument.Parse("null").RootElement.Clone();
            return JsonDocument.Parse("""
                [{"range":{"start":{"line":1,"character":2},"end":{"line":1,"character":5}},
                  "parent":{"range":{"start":{"line":0,"character":0},"end":{"line":3,"character":0}}}},
                 {"range":{"start":{"line":2,"character":1},"end":{"line":2,"character":4}}}]
                """).RootElement.Clone();
        }
    }

    [Theory]
    [InlineData("true", "ranges")]
    [InlineData("true", "null")]
    [InlineData("true", "error")]
    [InlineData("{}", "ranges")]
    [InlineData("false", "ranges")]
    [InlineData("null", "ranges")]
    public async Task NegotiatesAndRequestsOnlyAdvertisedRanges(string provider, string answer)
    {
        var server = new Server(provider, answer);
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();
        using var serverRpc = new JsonRpc(
            new HeaderDelimitedMessageHandler(serverSide, serverSide, new SystemTextJsonFormatter()), server);
        serverRpc.StartListening();
        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>())));
        await using var client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), "vb6");
        await client.StartAsync(TestContext.Current.CancellationToken);

        var capability = server.InitializeParams.GetProperty("capabilities").GetProperty("textDocument")
            .GetProperty("selectionRange");
        capability.ValueKind.Should().Be(JsonValueKind.Object);
        capability.TryGetProperty("dynamicRegistration", out _).Should().BeFalse();
        var ranges = await client.RequestSelectionRangesAsync("file:///module.bas",
            [new Position(1, 3), new Position(2, 2)], TestContext.Current.CancellationToken);
        var enabled = provider is "true" or "{}";
        ((ILspClient)client).SupportsSelectionRanges("file:///module.bas").Should().Be(enabled);
        server.Calls.Should().Be(enabled ? 1 : 0);
        if (!enabled)
        {
            ranges.Should().BeEmpty();
            return;
        }
        server.Request.GetProperty("textDocument").GetProperty("uri").GetString().Should().Be("file:///module.bas");
        var positions = server.Request.GetProperty("positions");
        positions.GetArrayLength().Should().Be(2);
        positions[0].GetProperty("line").GetInt32().Should().Be(1);
        positions[0].GetProperty("character").GetInt32().Should().Be(3);
        positions[1].GetProperty("line").GetInt32().Should().Be(2);
        positions[1].GetProperty("character").GetInt32().Should().Be(2);
        if (answer != "ranges")
        {
            ranges.Should().BeEmpty();
            return;
        }
        ranges.Should().HaveCount(2);
        ranges[0].Range.Start.Should().Be(new Position(1, 2));
        ranges[0].Parent!.Range.End.Should().Be(new Position(3, 0));
        ranges[0].Parent!.Parent.Should().BeNull();
        ranges[1].Parent.Should().BeNull();
    }
}
