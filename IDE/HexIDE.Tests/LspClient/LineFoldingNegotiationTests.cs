using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// That the client says it folds whole lines, and that a server which honours that ends a fold on the line before
/// its closing line (hexide-io/HexIDE#1363).
///
/// <para>
/// <b>The editor folds a server's range by whole lines.</b> <c>Vb6FoldingAdapter.FromServer</c> reads a fold's
/// lines and ignores its characters. A client that does not declare <c>lineFoldingOnly</c> is told a fold may end
/// part-way along a line, and clangd ends each one just before its closing brace. Read by whole lines, that fold
/// runs to the end of the brace's line, so collapsing it hid the brace. Told the client folds whole lines, clangd
/// ends the fold a line earlier.
/// </para>
/// </summary>
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
    public async Task TheClientTellsTheServerItFoldsWholeLines()
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
            .GetProperty("foldingRange")
            .GetProperty("lineFoldingOnly").GetBoolean()
            .Should().BeTrue("the editor folds a server's range by whole lines and ignores its characters");
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
    public async Task AServerThatHonoursItEndsAFoldBeforeTheClosingLine()
    {
        var sut = CppRegistry();
        var uri = DocumentUri();
        await sut.OpenDocumentAsync(uri, CppSource);

        var folds = await sut.RequestFoldingRangesAsync(uri, TestContext.Current.CancellationToken);

        folds.Should().ContainSingle("the function body is the one block in the file");
        folds[0].StartLine.Should().Be(0);
        folds[0].EndLine.Should().Be(2,
            "a fold read by whole lines that ended on line 3 would hide the closing brace, which has that line to itself");
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
