using System.Text.Json;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// Two servers may claim one extension and disagree about what it is called, and both are right.
///
/// <para>
/// This is the change section 3 of #255 exists for. Routing used to consult a global extension-to-language
/// table, which silently assumed every server would agree — so of two servers claiming <c>.py</c>, one
/// calling it <c>python</c> and the other <c>python3</c>, exactly one could be served correctly and the
/// other was told something it did not recognise about every file it saw.
/// </para>
///
/// <para>
/// The resolution is that each server has its own connection, and nothing in the protocol requires two
/// connections be told the same thing. Routing keys on the extension; the identifier is per-server.
/// </para>
/// </summary>
public class PerServerLanguageIdTests : IAsyncDisposable
{
    private const string PyDoc = "file:///c:/proj/main.py";

    private readonly List<IAsyncDisposable> _disposables = [];

    // ── Routing: both claimants are served ────────────────────────────────────────────────────────────

    private static ILspClient FakeClient()
    {
        var c = Substitute.For<ILspClient>();
        c.IsRunning.Returns(true);
        c.AdvertisedCapabilities.Returns(
            JsonDocument.Parse("""{"textDocumentSync":{"openClose":true,"change":1}}""").RootElement.Clone());
        return c;
    }

    [Fact]
    public async Task TwoServersClaimingOneExtensionUnderDifferentNamesBothReceiveTheDocument()
    {
        // Keyed on the extension, so disagreement about the NAME cannot exclude either of them. Under the
        // old global table one of these two would have been unreachable for every .py file.
        var python = FakeClient();
        var python3 = FakeClient();
        var sut = new LspClientRegistry(
            [
                new LanguageServerRegistration("a", "a", [".py"], "python", () => python),
                new LanguageServerRegistration("b", "b", [".py"], "python3", () => python3),
            ],
            Substitute.For<ILogger<LspClientRegistry>>());
        _disposables.Add(sut);

        await sut.OpenDocumentAsync(PyDoc, "x = 1");

        await python.Received(1).OpenDocumentAsync(PyDoc, "x = 1", Arg.Any<CancellationToken>());
        await python3.Received(1).OpenDocumentAsync(PyDoc, "x = 1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AServerIsNotOfferedAnExtensionItDoesNotClaim()
    {
        // Routing is only meaningful if it excludes. Without this the suite would pass with a registry that
        // handed every document to every server.
        var python = FakeClient();
        var markdown = FakeClient();
        var sut = new LspClientRegistry(
            [
                new LanguageServerRegistration("a", "a", [".py"], "python", () => python),
                new LanguageServerRegistration("b", "b", [".md"], "markdown", () => markdown),
            ],
            Substitute.For<ILogger<LspClientRegistry>>());
        _disposables.Add(sut);

        await sut.OpenDocumentAsync(PyDoc, "x = 1");

        await python.Received(1).OpenDocumentAsync(PyDoc, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await markdown.DidNotReceive().OpenDocumentAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClaimsAreComparedWithoutRegardToCase()
    {
        // A user writes ".MD" or opens "README.MD"; neither should decide whether their server runs.
        var server = FakeClient();
        var sut = new LspClientRegistry(
            [new LanguageServerRegistration("a", "a", [".Md"], "markdown", () => server)],
            Substitute.For<ILogger<LspClientRegistry>>());
        _disposables.Add(sut);

        await sut.OpenDocumentAsync("file:///c:/p/README.MD", "# hi");

        await server.Received(1).OpenDocumentAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── The wire: each server is told what IT declared ────────────────────────────────────────────────

    /// <summary>Records the <c>languageId</c> of the first didOpen it is sent.</summary>
    private sealed class DidOpenRecordingServer
    {
        private readonly TaskCompletionSource<string> _languageId =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string> LanguageId => _languageId.Task;

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) => JsonDocument.Parse(
            """{"capabilities":{"textDocumentSync":{"openClose":true,"change":1}}}""").RootElement.Clone();

        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }

        [JsonRpcMethod("textDocument/didOpen", UseSingleObjectParameterDeserialization = true)]
        public void DidOpen(JsonElement p) =>
            _languageId.TrySetResult(p.GetProperty("textDocument").GetProperty("languageId").GetString()!);
    }

    private VBLspClient ClientDeclaring(string languageId, DidOpenRecordingServer server)
    {
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();
        var serverRpc = new JsonRpc(
            new HeaderDelimitedMessageHandler(serverSide, serverSide, new SystemTextJsonFormatter()), server);
        serverRpc.StartListening();

        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>())));

        var client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), languageId);
        _disposables.Add(client);
        return client;
    }

    [Theory]
    [InlineData("python")]
    [InlineData("python3")]
    public async Task AServerIsToldTheLanguageIdItDeclaredRatherThanOneChosenForIt(string declared)
    {
        // Driven over a real JsonRpc pair rather than a substitute, because the value under test is what
        // goes ON THE WIRE — a mocked client would assert only that we passed our own argument along.
        var server = new DidOpenRecordingServer();
        var sut = ClientDeclaring(declared, server);
        await sut.StartAsync();

        await sut.OpenDocumentAsync(PyDoc, "x = 1");

        (await server.LanguageId.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(declared);
    }

    [Fact]
    public async Task TheIdeOwnDocumentsAreStillIdentifiedAsVb6()
    {
        // The regression that would be easiest to introduce here: the bundled server's own documents
        // carry no extension, so they route by scheme, and their language identifier must survive the move
        // from a global table to a per-server declaration.
        var server = new DidOpenRecordingServer();
        var sut = ClientDeclaring(DocumentLanguage.Vb6, server);
        await sut.StartAsync();

        await sut.OpenDocumentAsync("vb6://module/Module1", "Sub Main()\r\nEnd Sub\r\n");

        (await server.LanguageId.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be("vb6");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var d in _disposables)
        {
            try { await d.DisposeAsync(); } catch { /* teardown is best effort */ }
        }
        GC.SuppressFinalize(this);
    }
}
