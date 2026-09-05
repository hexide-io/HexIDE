using System.Text.Json;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// What a real server receives when a document is saved, driven through a real connection.
///
/// <para>
/// <b>Not through a substitute, and the reason is specific rather than stylistic.</b> A mocked
/// <c>ILspClient</c> synthesises <c>SaveDocumentAsync</c> and returns a completed task, so
/// <c>Received(1)</c> is green with the notification never leaving the process — including when the type
/// is missing from the serializer context, which throws into a debug-level catch and produces exactly the
/// silence this whole change exists to remove. Every question this file asks is about what crossed the
/// wire: whether anything did, and what shape it had.
/// </para>
/// </summary>
public class SaveOnTheWireTests : IAsyncDisposable
{
    private const string Uri = "file:///c:/proj/README.md";

    private readonly List<IAsyncDisposable> _disposables = [];

    /// <summary>A server that advertises whatever it is told to, and records the saves it receives.</summary>
    private sealed class SaveRecordingServer(string capabilitiesJson)
    {
        private readonly TaskCompletionSource<JsonElement> _firstSave =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<JsonElement> FirstSave => _firstSave.Task;
        public int SaveCount { get; private set; }

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) =>
            JsonDocument.Parse($$"""{"capabilities":{{capabilitiesJson}}}""").RootElement.Clone();

        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }

        [JsonRpcMethod("textDocument/didOpen", UseSingleObjectParameterDeserialization = true)]
        public void DidOpen(JsonElement _) { }

        [JsonRpcMethod("textDocument/didChange", UseSingleObjectParameterDeserialization = true)]
        public void DidChange(JsonElement _) { }

        [JsonRpcMethod("textDocument/didSave", UseSingleObjectParameterDeserialization = true)]
        public void DidSave(JsonElement p)
        {
            SaveCount++;
            _firstSave.TrySetResult(p.Clone());
        }
    }

    private async Task<(VBLspClient Client, SaveRecordingServer Server)> ConnectedTo(
        string capabilitiesJson, bool start = true)
    {
        var server = new SaveRecordingServer(capabilitiesJson);
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
        // The registry starts its own clients, and the transport hands out one handler over one stream
        // pair — so starting here as well would have the second attempt connect over a stream the first
        // already owns.
        if (start) await client.StartAsync();
        return (client, server);
    }

    /// <summary>Gives a notification that should NOT arrive a fair chance to arrive before we deny it.</summary>
    private static async Task SettleAsync() => await Task.Delay(250);

    // ── Servers that asked ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("""{"textDocumentSync":{"openClose":true,"change":1,"save":true}}""")]
    [InlineData("""{"textDocumentSync":{"openClose":true,"change":1,"save":{}}}""")]
    [InlineData("""{"textDocumentSync":{"openClose":true,"change":1,"save":{"includeText":false}}}""")]
    [InlineData("""{"textDocumentSync":1}""")]
    public async Task AServerThatAskedIsToldWithoutTheText(string capabilities)
    {
        var (client, server) = await ConnectedTo(capabilities);
        await client.OpenDocumentAsync(Uri, "# hello");

        await client.SaveDocumentAsync(Uri);

        var save = await server.FirstSave.WaitAsync(TimeSpan.FromSeconds(10));
        save.GetProperty("textDocument").GetProperty("uri").GetString().Should().Be(Uri);
        save.TryGetProperty("text", out _).Should().BeFalse(
            "a null in place of an absent field sends a server down its has-text branch with nothing in "
          + "its hand — and this assertion is the only one that can tell the two apart");
    }

    [Fact]
    public async Task AServerThatAskedForTheTextGetsWhatWasSaved()
    {
        var (client, server) = await ConnectedTo(
            """{"textDocumentSync":{"openClose":true,"change":1,"save":{"includeText":true}}}""");
        await client.OpenDocumentAsync(Uri, "# hello");

        await client.SaveDocumentAsync(Uri);

        var save = await server.FirstSave.WaitAsync(TimeSpan.FromSeconds(10));
        save.GetProperty("text").GetString().Should().Be("# hello");
    }

    [Fact]
    public async Task TheTextSentIsTheLatestTheServerWasGiven()
    {
        var (client, server) = await ConnectedTo(
            """{"textDocumentSync":{"openClose":true,"change":1,"save":{"includeText":true}}}""");
        await client.OpenDocumentAsync(Uri, "# first");
        await client.ChangeDocumentAsync(Uri, 2, "# second");

        await client.SaveDocumentAsync(Uri);

        var save = await server.FirstSave.WaitAsync(TimeSpan.FromSeconds(10));
        save.GetProperty("text").GetString().Should().Be(
            "# second", "announcing a save of text the server was never given describes a file it cannot see");
    }

    // ── Servers that did not ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("""{"textDocumentSync":{"openClose":true,"change":1}}""")]
    [InlineData("""{"textDocumentSync":{"openClose":true,"change":1,"save":false}}""")]
    [InlineData("""{"textDocumentSync":0}""")]
    [InlineData("""{}""")]
    public async Task AServerThatDidNotAskHearsNothing(string capabilities)
    {
        var (client, server) = await ConnectedTo(capabilities);
        await client.OpenDocumentAsync(Uri, "# hello");

        await client.SaveDocumentAsync(Uri);
        await SettleAsync();

        server.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task NothingIsSentBeforeTheHandshakeCompletes()
    {
        // A save can land during startup — the IDE restores a session and the user hits Ctrl+S. Until the
        // server has answered, there is no negotiated answer to honour, so the only correct thing to send
        // is nothing.
        var server = new SaveRecordingServer("""{"textDocumentSync":{"save":{"includeText":true}}}""");
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

        await client.SaveDocumentAsync(Uri);   // never started
        await SettleAsync();

        server.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task SavingADocumentTheServerDoesNotHoldSendsNoText()
    {
        // The URI is still announced — the server may know the file from disk even if this connection
        // never opened it — but there is no tracked text to attach, and inventing one is worse than
        // omitting it.
        var (client, server) = await ConnectedTo(
            """{"textDocumentSync":{"openClose":true,"change":1,"save":{"includeText":true}}}""");

        await client.SaveDocumentAsync("file:///c:/proj/never-opened.md");

        var save = await server.FirstSave.WaitAsync(TimeSpan.FromSeconds(10));
        save.TryGetProperty("text", out _).Should().BeFalse();
    }

    // ── Two servers, one document ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OfTwoServersHoldingOneDocumentOnlyTheOneThatAskedIsTold()
    {
        // The reason the gate lives on the connection and not on the router. Two servers may legitimately
        // disagree about whether they want saves — as they may about what a file is called — and a router
        // that decided centrally would have to pick a winner and be wrong for the other.
        var asked = await ConnectedTo(
            """{"textDocumentSync":{"openClose":true,"change":1,"save":{"includeText":true}}}""",
            start: false);
        var didNot = await ConnectedTo(
            """{"textDocumentSync":{"openClose":true,"change":1}}""", start: false);

        var registry = new LspClientRegistry(
            [
                new LanguageServerRegistration("asked", "asked", [".md"], "markdown", () => asked.Client),
                new LanguageServerRegistration("quiet", "quiet", [".md"], "markdown", () => didNot.Client),
            ],
            Substitute.For<ILogger<LspClientRegistry>>());
        _disposables.Add(registry);

        await registry.OpenDocumentAsync(Uri, "# hello");
        await registry.SaveDocumentAsync(Uri);
        await SettleAsync();

        asked.Server.SaveCount.Should().Be(1);
        didNot.Server.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task SavingADocumentStartsNoServer()
    {
        // Servers start on the first document of a language they claim. A save for something nothing has
        // opened is not that — launching a process on it would make Ctrl+S in an unrelated file spawn a
        // server for a document it will immediately be told nothing more about.
        var started = false;
        var registry = new LspClientRegistry(
            [new LanguageServerRegistration("md", "md", [".md"], "markdown", () =>
            {
                started = true;
                return Substitute.For<ILspClient>();
            })],
            Substitute.For<ILogger<LspClientRegistry>>());
        _disposables.Add(registry);

        await registry.SaveDocumentAsync(Uri);

        started.Should().BeFalse();
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
