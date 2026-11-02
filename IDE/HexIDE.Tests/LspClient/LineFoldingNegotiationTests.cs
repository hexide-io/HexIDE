using System.Text.Json;
using AvaloniaEdit.Document;
using HexIDE.Controls;
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

        public bool FailTokens { get; set; }
        public bool EmptyTokens { get; set; }
        public int TokenRequests { get; private set; }
        public string? TokenUri { get; private set; }

        [JsonRpcMethod("textDocument/semanticTokens/full", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Tokens(JsonElement p)
        {
            TokenRequests++;
            TokenUri = p.GetProperty("textDocument").GetProperty("uri").GetString();
            if (FailTokens) throw new InvalidOperationException("analysis still starting");
            return JsonDocument.Parse(EmptyTokens ? """{"data":[]}""" : """{"data":[0,0,3,0,0]}""").RootElement.Clone();
        }

        public Task<JsonElement> InitializeParams => _initialize.Task;

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement p)
        {
            _initialize.TrySetResult(p.Clone());
            return JsonDocument.Parse("""{"capabilities":{"foldingRangeProvider":true,"semanticTokensProvider":{"full":true,"legend":{"tokenTypes":["class"],"tokenModifiers":[]}}}}""").RootElement.Clone();
        }

        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }
    }

    [AvaloniaFact]
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

        var logger = Substitute.For<ILogger<VBLspClient>>();
        var client = new VBLspClient(transport, logger, "markdown");
        _disposables.Add(client);
        await client.StartAsync(TestContext.Current.CancellationToken);
        var initialize = await server.InitializeParams.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var semantic = initialize.GetProperty("capabilities").GetProperty("textDocument").GetProperty("semanticTokens");
        semantic.GetProperty("requests").GetProperty("full").GetBoolean().Should().BeTrue();
        semantic.GetProperty("requests").TryGetProperty("range", out _).Should().BeFalse();
        semantic.GetProperty("formats")[0].GetString().Should().Be("relative");
        semantic.GetProperty("tokenTypes").GetArrayLength().Should().Be(23);
        semantic.GetProperty("tokenModifiers").GetArrayLength().Should().Be(10);
        semantic.GetProperty("overlappingTokenSupport").GetBoolean().Should().BeFalse();
        semantic.GetProperty("multilineTokenSupport").GetBoolean().Should().BeFalse();

        initialize.GetProperty("capabilities").GetProperty("textDocument")
            .TryGetProperty("foldingRange", out _).Should().BeFalse();

        const string uri = "untitled:Module1.bas";
        var tokens = await client.RequestSemanticTokensAsync(uri, TestContext.Current.CancellationToken);
        tokens.Should().NotBeNull();
        tokens!.Data.Should().Equal(0, 0, 3, 0, 0);
        tokens.TokenTypes.Should().Equal("class");
        SemanticTokensColorizer.Decode(new TextDocument("Foo"), tokens).Should()
            .Equal(new SemanticTokensColorizer.Token(0, 3, "Type"));
        server.TokenUri.Should().Be(uri);
        server.TokenRequests.Should().Be(1);

        server.FailTokens = true;
        (await client.RequestSemanticTokensAsync(uri, TestContext.Current.CancellationToken)).Should().BeNull();
        (await client.RequestSemanticTokensAsync(uri, TestContext.Current.CancellationToken)).Should().BeNull();
        server.FailTokens = false;
        server.EmptyTokens = true;
        (await client.RequestSemanticTokensAsync(uri, TestContext.Current.CancellationToken)).Should().BeNull();
        server.EmptyTokens = false;
        (await client.RequestSemanticTokensAsync(uri, TestContext.Current.CancellationToken)).Should().NotBeNull();
        server.TokenRequests.Should().Be(5, "an error must not prevent the next change from requesting tokens");
        logger.ReceivedCalls().Count(call => call.GetMethodInfo().Name == "Log"
            && call.GetArguments()[2]?.ToString()?.Contains("keeping lexical colours", StringComparison.Ordinal) == true)
            .Should().Be(1, "errors and empty results are logged once per document");
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
