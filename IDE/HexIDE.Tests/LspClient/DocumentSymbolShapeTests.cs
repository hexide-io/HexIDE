using System.Text;
using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// What arrives when a server answers <c>textDocument/documentSymbol</c> in a shape our own server never
/// uses.
///
/// <para>
/// <b>The request has two legal replies and they share no field.</b> <c>DocumentSymbol[]</c> is a tree with
/// <c>range</c>, <c>selectionRange</c> and <c>children</c>; <c>SymbolInformation[]</c> is flat and carries a
/// <c>location</c> instead. Servers choose freely, and the choice is not negotiable through any capability —
/// so a client supporting one shape is broken against half the ecosystem, silently, because deserializing
/// the wrong shape produces objects rather than an error.
/// </para>
///
/// <para>
/// Every case here was unreachable against the bundled VB6 server, which returns a flat
/// <c>DocumentSymbol[]</c> of procedures with every field populated. That is exactly why they went unnoticed:
/// the only server this client had ever met agreed with it.
/// </para>
/// </summary>
public class DocumentSymbolShapeTests : IAsyncDisposable
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

    private async Task<DocumentSymbol[]> SymbolsFromServerAnswering(string symbolsJson)
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

        return await client.RequestDocumentSymbolsAsync(
            "vb6://module/Module1", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AHierarchicalReplyKeepsTheSymbolsNestedInsideIt()
    {
        // The shape that made #160's "members appear as nested symbols" land empty. A server reporting one
        // module symbol holding its procedures is entirely conformant, and reading only the top level of
        // that answer yields a procedure list containing the module's name and nothing else.
        var symbols = await SymbolsFromServerAnswering("""
            [{"name":"Module1","kind":2,
              "range":{"start":{"line":0,"character":0},"end":{"line":20,"character":0}},
              "selectionRange":{"start":{"line":0,"character":0},"end":{"line":0,"character":7}},
              "children":[
                {"name":"Alpha","kind":12,
                 "range":{"start":{"line":2,"character":0},"end":{"line":4,"character":0}},
                 "selectionRange":{"start":{"line":2,"character":4},"end":{"line":2,"character":9}}},
                {"name":"Beta","kind":6,
                 "range":{"start":{"line":6,"character":0},"end":{"line":8,"character":0}},
                 "selectionRange":{"start":{"line":6,"character":4},"end":{"line":6,"character":8}}}]}]
            """);

        symbols.Should().ContainSingle();
        symbols[0].Kind.Should().Be(SymbolKind.Module);
        symbols[0].Children.Should().NotBeNull().And.HaveCount(2);

        symbols.SelectMany(s => s.Flatten()).Select(s => s.Name)
            .Should().Equal(["Module1", "Alpha", "Beta"],
                "the procedures are what a dropdown is for, and they are one level down");
    }

    [Fact]
    public async Task AFlatSymbolInformationReplyIsUnderstoodRatherThanCrashed()
    {
        // The NRE. SymbolInformation carries `location` and NO ranges, so deserializing it straight into a
        // DocumentSymbol leaves both range fields null — and the first consumer to read one throws far from
        // the cause, in a UI event handler.
        var symbols = await SymbolsFromServerAnswering("""
            [{"name":"Gamma","kind":12,
              "location":{"uri":"file:///c:/proj/Module1.bas",
                          "range":{"start":{"line":3,"character":0},"end":{"line":5,"character":0}}},
              "containerName":"Module1"}]
            """);

        symbols.Should().ContainSingle();
        symbols[0].Name.Should().Be("Gamma");
        symbols[0].Range.Start.Line.Should().Be(3);

        // The one range the server gave becomes both. Claiming to know a narrower selection would be
        // inventing one, and every consumer reads SelectionRange to place the caret.
        symbols[0].SelectionRange.Should().NotBeNull();
        symbols[0].SelectionRange.Start.Line.Should().Be(3);
    }

    [Fact]
    public async Task AKindOutsideTheFiveVb6UsesArrivesAsItself()
    {
        // With a five-member enum every one of these fell into the default arm of every switch and was
        // relabelled a method. A wrong label, not a missing one, and nothing would have reported it.
        var symbols = await SymbolsFromServerAnswering("""
            [{"name":"aModule","kind":2,
              "range":{"start":{"line":0,"character":0},"end":{"line":1,"character":0}},
              "selectionRange":{"start":{"line":0,"character":0},"end":{"line":0,"character":1}}},
             {"name":"aVariable","kind":13,
              "range":{"start":{"line":2,"character":0},"end":{"line":2,"character":9}},
              "selectionRange":{"start":{"line":2,"character":0},"end":{"line":2,"character":9}}},
             {"name":"anEvent","kind":24,
              "range":{"start":{"line":4,"character":0},"end":{"line":4,"character":9}},
              "selectionRange":{"start":{"line":4,"character":0},"end":{"line":4,"character":9}}}]
            """);

        symbols.Select(s => s.Kind).Should().Equal(
            [SymbolKind.Module, SymbolKind.Variable, SymbolKind.Event]);
    }

    [Fact]
    public async Task AMissingSelectionRangeFallsBackToTheFullRange()
    {
        // Malformed — the specification requires selectionRange on a DocumentSymbol. The point of this seam
        // is that a server we did not write cannot make a consumer here throw, so a defensible substitute
        // beats a null nothing checks for.
        var symbols = await SymbolsFromServerAnswering("""
            [{"name":"Delta","kind":12,
              "range":{"start":{"line":7,"character":0},"end":{"line":9,"character":0}}}]
            """);

        symbols.Should().ContainSingle();
        symbols[0].SelectionRange.Start.Line.Should().Be(7);
    }

    [Fact]
    public async Task ASymbolWithNoRangeAtAllIsDroppedRatherThanCarried()
    {
        // Every consumer of a symbol here exists to navigate to it. One with no position cannot be
        // navigated to, so carrying it means offering a name that does nothing when chosen.
        var symbols = await SymbolsFromServerAnswering("""
            [{"name":"Positionless","kind":12},
             {"name":"Real","kind":12,
              "range":{"start":{"line":1,"character":0},"end":{"line":2,"character":0}},
              "selectionRange":{"start":{"line":1,"character":0},"end":{"line":1,"character":4}}}]
            """);

        symbols.Select(s => s.Name).Should().Equal(["Real"]);
    }

    /// <summary>Nested <c>documentSymbol</c> JSON, <paramref name="depth"/> symbols deep.</summary>
    private static string NestedSymbols(int depth)
    {
        var json = new StringBuilder();
        for (var i = 0; i < depth; i++)
        {
            json.Append("[{\"name\":\"S").Append(i).Append("\",\"kind\":12,")
                .Append("\"range\":{\"start\":{\"line\":0,\"character\":0},\"end\":{\"line\":1,\"character\":0}},")
                .Append("\"selectionRange\":{\"start\":{\"line\":0,\"character\":0},\"end\":{\"line\":0,\"character\":1}},")
                .Append("\"children\":");
        }
        json.Append("[]");
        for (var i = 0; i < depth; i++) json.Append("}]");
        return json.ToString();
    }

    [Fact]
    public async Task RealNestingRoundTripsWhole()
    {
        // Twenty-five levels is far past anything VB6 or VBA produces and comfortably inside what the
        // reader accepts, so this is the case that proves nesting is READ rather than merely survived.
        var symbols = await SymbolsFromServerAnswering(NestedSymbols(25));

        symbols.SelectMany(s => s.Flatten()).Should().HaveCount(25);
    }

    [Fact]
    public async Task PathologicalNestingIsRefusedRatherThanCrashing()
    {
        // An unbounded walk of attacker-shaped nesting is a stack overflow — not an exception, uncatchable,
        // and it takes the IDE with it. This asserts the outcome is a refusal instead.
        //
        // MEASURED, and worth recording because it is not where you would look: the bound is
        // System.Text.Json's own MaxDepth of 64, applied while the RESPONSE is parsed, long before any code
        // here walks it. Each symbol level costs two JSON levels (object, then children array), so the
        // effective ceiling is 31 symbols of nesting. The depth cap inside the reader is a second line of
        // defence that the first one currently prevents from ever being reached — kept because "currently"
        // is doing real work in that sentence, and Flatten's own cap is reachable by any caller holding a
        // tree this did not build.
        var symbols = await SymbolsFromServerAnswering(NestedSymbols(400));

        symbols.Should().BeEmpty(
            "the reply is rejected whole rather than partially walked — the parse never completes");
    }

    /// <summary>A server that advertises document symbols and answers with whatever JSON it was given.</summary>
    private sealed class StubServer(string symbolsJson)
    {
        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) =>
            JsonDocument.Parse("""{"capabilities":{"documentSymbolProvider":true}}""").RootElement.Clone();

        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }

        [JsonRpcMethod("textDocument/documentSymbol", UseSingleObjectParameterDeserialization = true)]
        public JsonElement DocumentSymbol(JsonElement _) =>
            JsonDocument.Parse(symbolsJson, new JsonDocumentOptions { MaxDepth = 4096 }).RootElement.Clone();
    }
}
