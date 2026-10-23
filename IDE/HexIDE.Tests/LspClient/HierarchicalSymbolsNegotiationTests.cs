using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// That the client says it takes a tree of symbols, and that a server which waits to be told then sends one
/// (hexide-io/HexIDE#463).
///
/// <para>
/// <b>Which shape <c>textDocument/documentSymbol</c> answers in is negotiated.</b> A client that declares
/// <c>hierarchicalDocumentSymbolSupport</c> may be sent the <c>DocumentSymbol[]</c> tree; one that does not
/// gets the flat <c>SymbolInformation[]</c> from a server that honours the declaration. The client read both
/// shapes all along, but never declared the first, so every outline built from such a server lost its
/// nesting.
/// </para>
///
/// <para>
/// <b>Why nothing noticed.</b> The servers HexIDE meets most often return the tree whether or not the client
/// asked for it. A server that returns the tree unconditionally and a client that never declares it cancel
/// out, and agree with each other rather than with the specification. clangd waits to be told, which is
/// why it is the measurement here.
/// </para>
/// </summary>
public class HierarchicalSymbolsNegotiationTests : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _disposables = [];
    private LspClientRegistry? _registry;

    /// <summary>Captures the <c>initialize</c> parameters the client sends.</summary>
    private sealed class HandshakeRecorder
    {
        private readonly TaskCompletionSource<JsonElement> _initialize =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<JsonElement> InitializeParams => _initialize.Task;

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement p)
        {
            _initialize.TrySetResult(p.Clone());
            return JsonDocument.Parse("""{"capabilities":{"documentSymbolProvider":true}}""").RootElement.Clone();
        }

        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }
    }

    [Fact]
    public async Task TheClientTellsTheServerItTakesATreeOfSymbols()
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

        var client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), "markdown");
        _disposables.Add(client);
        await client.StartAsync(TestContext.Current.CancellationToken);
        var initialize = await server.InitializeParams.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        initialize.GetProperty("capabilities")
            .GetProperty("textDocument")
            .GetProperty("documentSymbol")
            .GetProperty("hierarchicalDocumentSymbolSupport").GetBoolean()
            .Should().BeTrue("a server that honours the declaration sends the flat form to a client that never made it");
    }

    // A namespace holding a struct and a function, and no #include, for the reason DeclarationIsNotDefinitionTests
    // gives. Measured against clangd: flat, this is five symbols at the top level and no children; as a tree, it is
    // the namespace alone, holding the other two.
    private const string CppSource =
        "namespace shapes {\n" +
        "struct Point { int x; int y; };\n" +
        "int area(int w, int h) { return w * h; }\n" +
        "}\n";

    /// <summary>A URI from a platform-native path; see <c>DeclarationIsNotDefinitionTests.DocumentUri</c>.</summary>
    private static string DocumentUri() =>
        new Uri(Path.Combine(Path.GetTempPath(), "hexide-clangd", "shapes.cpp")).AbsoluteUri;

    private LspClientRegistry CppRegistry()
    {
        var serverInfo = new LspServerInfo(
            ForeignServer.Cpp.Find()!, ForeignServer.Cpp.ServerArguments, Path.GetTempPath());

        var loggerFactory = LoggerFactory.Create(b => { });
        var registration = new LanguageServerRegistration(
            Id: "foreign.cpp",
            DisplayName: "Foreign C/C++ server",
            Extensions: ForeignServer.Cpp.Extensions,
            LanguageId: ForeignServer.Cpp.LanguageId,
            CreateClient: () => new VBLspClient(
                new StdioProcessLspTransport(serverInfo, loggerFactory.CreateLogger<StdioProcessLspTransport>()),
                loggerFactory.CreateLogger<VBLspClient>(),
                ForeignServer.Cpp.LanguageId));

        _registry = new LspClientRegistry([registration], loggerFactory.CreateLogger<LspClientRegistry>());
        return _registry;
    }

    [ForeignServerFact("cpp")]
    public async Task AServerThatWaitsToBeToldSendsTheOutlineAsATree()
    {
        var sut = CppRegistry();
        var uri = DocumentUri();
        await sut.OpenDocumentAsync(uri, CppSource);

        var symbols = await sut.RequestDocumentSymbolsAsync(uri, TestContext.Current.CancellationToken);

        symbols.Select(s => s.Name).Should().Equal(["shapes"],
            "as a tree, the namespace is the only symbol at the top level; flat, all five would be");
        symbols[0].Children.Should().NotBeNullOrEmpty("the struct and the function are inside the namespace");
        symbols[0].Children!.Select(s => s.Name).Should().Equal(["Point", "area"]);
    }

    public async ValueTask DisposeAsync()
    {
        if (_registry is not null)
        {
            try { await _registry.StopAsync(); } catch { /* best effort, as the sibling suites do */ }
        }
        foreach (var d in _disposables)
        {
            try { await d.DisposeAsync(); } catch { /* teardown is best effort */ }
        }
        GC.SuppressFinalize(this);
    }
}
