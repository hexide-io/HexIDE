using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;
using LspRange = HexIDE.Lsp.Messages.Range;   // `Range` collides with System.Range

namespace HexIDE.Tests.LspClient;

/// <summary>
/// <c>workspace/symbol</c> — the first request this client makes that is not about a document.
///
/// <para>
/// Two things are new here and both are tested rather than assumed: the reply has <b>two</b> legal shapes
/// that share a field name but not its contents, and the routing has no URI to key on, so it asks every
/// capable server instead of the one that claims a file.
/// </para>
/// </summary>
public class WorkspaceSymbolTests : IAsyncDisposable
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

    /// <summary>A server advertising workspace symbols and answering with whatever JSON it was given.</summary>
    private sealed class StubServer(string symbolsJson)
    {
        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) =>
            JsonDocument.Parse("""{"capabilities":{"workspaceSymbolProvider":true}}""").RootElement.Clone();

        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }

        [JsonRpcMethod("workspace/symbol", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Symbols(JsonElement _) =>
            JsonDocument.Parse(symbolsJson).RootElement.Clone();
    }

    private async Task<SymbolInformation[]> Read(string symbolsJson)
    {
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();
        var serverRpc = new JsonRpc(
            new HeaderDelimitedMessageHandler(serverSide, serverSide, new SystemTextJsonFormatter()),
            new StubServer(symbolsJson));
        serverRpc.StartListening();

        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>())));

        var client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), DocumentLanguage.Vb6);
        _disposables.Add(client);
        await client.StartAsync(TestContext.Current.CancellationToken);

        return await client.RequestWorkspaceSymbolsAsync("Calc", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheOlderSymbolInformationShapeIsRead()
    {
        var symbols = await Read("""
            [{"name":"CalcTotal","kind":12,
              "location":{"uri":"file:///c:/proj/Orders.bas",
                          "range":{"start":{"line":9,"character":4},"end":{"line":9,"character":13}}},
              "containerName":"Orders"}]
            """);

        symbols.Should().HaveCount(1);
        symbols[0].Name.Should().Be("CalcTotal");
        symbols[0].Kind.Should().Be(SymbolKind.Function);
        symbols[0].ContainerName.Should().Be("Orders");
        symbols[0].Location.Range.Start.Line.Should().Be(9);
    }

    [Fact]
    public async Task A317WorkspaceSymbolWithNoRangeIsKeptAtTheStartOfItsFile()
    {
        // 3.17 lets a server answer with only a uri and defer the range to workspaceSymbol/resolve, so it
        // can answer a broad query without computing positions for thousands of hits. We do not implement
        // resolve. Keeping the hit at position zero lands the user in the right FILE; dropping it would
        // hide a real symbol, and inventing a line would be a wrong answer rather than an imprecise one.
        var symbols = await Read("""
            [{"name":"CalcTotal","kind":12,"location":{"uri":"file:///c:/proj/Orders.bas"}}]
            """);

        symbols.Should().HaveCount(1);
        symbols[0].Location.Uri.Should().Be("file:///c:/proj/Orders.bas");
        symbols[0].Location.Range.Start.Line.Should().Be(0);
        symbols[0].Location.Range.Start.Character.Should().Be(0);
    }

    [Fact]
    public async Task ANullReplyIsNoSymbolsRatherThanAFailure()
    {
        // `SymbolInformation[] | WorkspaceSymbol[] | null` — null is a legal answer meaning "nothing".
        (await Read("null")).Should().BeEmpty();
    }

    [Fact]
    public async Task AnEntryWithNoUsableLocationIsSkippedRatherThanFaked()
    {
        // A name with nowhere to go cannot be navigated to, and a list entry that does nothing when
        // activated is worse than one that is not there.
        (await Read("""[{"name":"Orphan","kind":12},{"name":"Fine","kind":12,"location":{"uri":"file:///c:/a.bas"}}]"""))
            .Select(s => s.Name).Should().Equal("Fine");
    }

    // ── Routing, which has no document to route by ───────────────────────────────────

    private static ILspClient ServerAdvertising(string capabilities, params string[] names)
    {
        var client = Substitute.For<ILspClient>();
        client.IsRunning.Returns(true);
        client.AdvertisedCapabilities.Returns(JsonDocument.Parse(capabilities).RootElement.Clone());
        client.RequestWorkspaceSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([.. names.Select(n => new SymbolInformation(
                n, SymbolKind.Function,
                new Location($"file:///c:/proj/{n}.bas",
                    new LspRange(new Position(0, 0), new Position(0, 0)))))]);
        return client;
    }

    private static LspClientRegistry RegistryOf(params ILspClient[] clients) =>
        new([.. clients.Select((c, i) => new LanguageServerRegistration(
                Id: $"server{i}",
                DisplayName: $"Server {i}",
                Extensions: [".bas"],
                LanguageId: $"lang{i}",
                CreateClient: () => c))],
            Substitute.For<ILogger<LspClientRegistry>>());

    /// <summary>
    /// A registry whose servers are actually running, reached the way the IDE reaches them.
    /// </summary>
    /// <remarks>
    /// Opening a document is the ONLY place a server starts, and deliberately so — it is the first moment
    /// that language is known to be present. A workspace query does not establish that, so it must not
    /// start anything, and a test that skipped this step would be asserting against a registry of null
    /// clients and passing for the wrong reason.
    /// </remarks>
    private static async Task<LspClientRegistry> RunningRegistryOf(params ILspClient[] clients)
    {
        var registry = RegistryOf(clients);
        await registry.OpenDocumentAsync(
            "vb6://module/Module1", "Option Explicit", TestContext.Current.CancellationToken);
        return registry;
    }

    [Fact]
    public async Task EveryCapableServerIsAskedAndTheAnswersAreUnioned()
    {
        // A workspace holding VB6 and LaTeX has two servers that each know a disjoint part of it. Taking
        // the first answer would drop a whole language and look like the symbols were simply not there.
        var sut = await RunningRegistryOf(
            ServerAdvertising("""{"workspaceSymbolProvider":true}""", "CalcTotal"),
            ServerAdvertising("""{"workspaceSymbolProvider":true}""", "Bibliography"));

        var symbols = await sut.RequestWorkspaceSymbolsAsync("Calc", TestContext.Current.CancellationToken);

        symbols.Select(s => s.Name).Should().BeEquivalentTo(["CalcTotal", "Bibliography"]);
    }

    [Fact]
    public async Task AServerThatDoesNotOfferItIsNotAsked()
    {
        var silent = ServerAdvertising("""{"documentSymbolProvider":true}""", "NeverAsked");
        var sut = await RunningRegistryOf(silent, ServerAdvertising("""{"workspaceSymbolProvider":true}""", "CalcTotal"));

        var symbols = await sut.RequestWorkspaceSymbolsAsync("Calc", TestContext.Current.CancellationToken);

        symbols.Select(s => s.Name).Should().Equal("CalcTotal");
        await silent.DidNotReceive().RequestWorkspaceSymbolsAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheCapabilityIsAcceptedAsOptionsAsWellAsTrue()
    {
        // Most capabilities are `boolean | XxxOptions` in the protocol and a conformant server may send
        // either; modelling one narrowly is what caused a total silent blackout once already (#238).
        var sut = await RunningRegistryOf(
            ServerAdvertising("""{"workspaceSymbolProvider":{"resolveProvider":true}}""", "CalcTotal"));

        var symbols = await sut.RequestWorkspaceSymbolsAsync("Calc", TestContext.Current.CancellationToken);

        symbols.Should().ContainSingle();
    }

    [Fact]
    public async Task NothingRunningMeansNoResultsRatherThanStartingEveryServer()
    {
        // Lazy start is deliberate: a server starts when a document of its language is opened, because
        // that is the first moment the language is known to be present. A search box does not establish
        // that, so this must NOT wake every registered server — a workspace query would otherwise launch a
        // LaTeX process because someone typed in a VB6 project.
        //
        // The cost is real and belongs to the UI: with nothing open there are no results, and "no matches"
        // must not be shown for what is actually "nothing is running yet".
        var server = ServerAdvertising("""{"workspaceSymbolProvider":true}""", "CalcTotal");
        var sut = RegistryOf(server);

        var symbols = await sut.RequestWorkspaceSymbolsAsync("Calc", TestContext.Current.CancellationToken);

        symbols.Should().BeEmpty();
        await server.DidNotReceive().RequestWorkspaceSymbolsAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnExplicitRefusalIsHonoured()
    {
        var sut = await RunningRegistryOf(ServerAdvertising("""{"workspaceSymbolProvider":false}""", "NeverAsked"));

        (await sut.RequestWorkspaceSymbolsAsync("Calc", TestContext.Current.CancellationToken))
            .Should().BeEmpty();
    }
}
