using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>The client accepts character folding ranges (hexide-io/HexIDE#1022).</summary>
public class LineFoldingNegotiationTests : IAsyncDisposable
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
            return JsonDocument.Parse("""{"capabilities":{"foldingRangeProvider":true}}""").RootElement.Clone();
        }

        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }
    }

    [Fact]
    public async Task TheClientDoesNotDeclareLineOnlyFolding()
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

        initialize.GetProperty("capabilities").GetProperty("textDocument")
            .GetProperty("foldingRange").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // A function whose closing brace has a line of its own, and no #include, for the reason
    // DeclarationIsNotDefinitionTests gives. Measured against clangd: its fold ends on line 3, the brace's line, for a
    // client that does not declare lineFoldingOnly, and on line 2 for one that does.
    private const string CppSource =
        "int area(int w, int h) {\n" +
        "    int a = w * h;\n" +
        "    return a;\n" +
        "}\n";

    /// <summary>A URI from a platform-native path; see <c>DeclarationIsNotDefinitionTests.DocumentUri</c>.</summary>
    private static string DocumentUri() =>
        new Uri(Path.Combine(Path.GetTempPath(), "hexide-clangd", "area.cpp")).AbsoluteUri;

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
    public async Task AServerEndsAFoldOnTheClosingBraceLine()
    {
        var sut = CppRegistry();
        var uri = DocumentUri();
        await sut.OpenDocumentAsync(uri, CppSource);

        var folds = await sut.RequestFoldingRangesAsync(uri, TestContext.Current.CancellationToken);

        folds.Should().ContainSingle("the function body is the one block in the file");
        folds[0].StartLine.Should().Be(0);
        folds[0].EndLine.Should().Be(3, "without lineFoldingOnly clangd ends on the brace line");
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
