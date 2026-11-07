using System.Text.Json;
using AvaloniaEdit.Document;
using HexIDE.Forms.ViewModels;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// An edit made while a language server is still starting, from a real document session through the real
/// registry to real clients, against servers whose start the test holds open (hexide-io/HexIDE#719).
///
/// <para>
/// <b>All three layers real, because each one lost the edit in its own way.</b> The session dropped a change
/// while nothing was running and recorded it nowhere. The registry opened every claimant only after all of
/// them had started, with the text captured when the open began. The client let an open overwrite a newer
/// tracked change and sent it again after its own reconnect replay had already sent the newer one. A test of
/// any one layer through substitutes for the other two passes with the bug in place.
/// </para>
/// <para>
/// What is asserted is what a server ends up holding, and the order it was told things in: one open, before any
/// change, and versions that only rise. The server here keeps a document as one that orders by version does,
/// discarding a change that is not later than what it holds.
/// </para>
/// </summary>
public class EditDuringServerStartTests : IAsyncDisposable
{
    private const string Uri = "file:///c:/proj/Module1.bas";
    private const string Original = "Sub Main()\r\nEnd Sub\r\n";
    private const string Edited = "'Sub Main()\r\nEnd Sub\r\n";

    /// <summary>
    /// Long enough past the session's 300 ms debounce that the edit's change has been handed on (or, before the
    /// fix, dropped) before the held server is let go. If the machine were slow enough to miss it, the change
    /// would go out after the server was up, and the test would pass without exercising the start-up window;
    /// it cannot fail because of the wait.
    /// </summary>
    private static readonly TimeSpan PastTheDebounce = TimeSpan.FromSeconds(1);

    private readonly List<IAsyncDisposable> _disposables = [];
    private readonly List<IDisposable> _sessions = [];

    private sealed record Received(string Method, int Version, string Text);

    /// <summary>
    /// Records every open and change, and holds the document as a server that orders by version does. Its
    /// <c>initialize</c> answers only once <see cref="Release"/> is called, so a test can act while it starts.
    /// </summary>
    private sealed class HeldStartServer(bool held)
    {
        private readonly object gate = new();
        private readonly List<Received> received = [];
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource initializing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int heldVersion;

        public Task Initializing => initializing.Task;
        public string HeldText { get; private set; } = "";

        public IReadOnlyList<Received> Messages
        {
            get { lock (gate) return [.. received]; }
        }

        public void Release() => release.TrySetResult();

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public async Task<JsonElement> InitializeAsync(JsonElement _)
        {
            initializing.TrySetResult();
            if (held) await release.Task;
            return JsonDocument.Parse("""{"capabilities":{"textDocumentSync":{"openClose":true,"change":1}}}""")
                .RootElement.Clone();
        }

        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }

        [JsonRpcMethod("textDocument/didOpen", UseSingleObjectParameterDeserialization = true)]
        public void DidOpen(JsonElement p)
        {
            var item = p.GetProperty("textDocument");
            lock (gate)
            {
                var version = item.GetProperty("version").GetInt32();
                var text = item.GetProperty("text").GetString()!;
                received.Add(new Received("didOpen", version, text));
                // An open replaces the document whatever its version, as the foreign server measured in #719 does.
                heldVersion = version;
                HeldText = text;
            }
        }

        [JsonRpcMethod("textDocument/didChange", UseSingleObjectParameterDeserialization = true)]
        public void DidChange(JsonElement p)
        {
            lock (gate)
            {
                var version = p.GetProperty("textDocument").GetProperty("version").GetInt32();
                var text = p.GetProperty("contentChanges")[0].GetProperty("text").GetString()!;
                received.Add(new Received("didChange", version, text));
                if (version > heldVersion)
                {
                    heldVersion = version;
                    HeldText = text;
                }
            }
        }

        [JsonRpcMethod("textDocument/didClose", UseSingleObjectParameterDeserialization = true)]
        public void DidClose(JsonElement _) { }
    }

    /// <summary>A registration whose client, when the registry creates it, connects to <paramref name="server"/>.</summary>
    private LanguageServerRegistration Registration(string id, HeldStartServer server, int priority) =>
        new(id, id, DocumentLanguage.Vb6Extensions, DocumentLanguage.Vb6, () => Client(server), priority);

    private VBLspClient Client(HeldStartServer server)
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

        var client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), "vb6");
        _disposables.Add(client);
        return client;
    }

    private static LspClientRegistry Registry(params LanguageServerRegistration[] registrations) =>
        new(registrations, Substitute.For<ILogger<LspClientRegistry>>());

    /// <summary>A started session for <see cref="Uri"/>, its document made on this thread, as an editor's is on the UI thread.</summary>
    private (LspDocumentSession Session, TextDocument Document) Started(ILspClient client)
    {
        var document = new TextDocument(Original);
        var session = new LspDocumentSession(client, document, Uri, postToUiThread: work => work());
        _sessions.Add(session);
        session.Start();
        return (session, document);
    }

    /// <summary>
    /// Returns once <paramref name="server"/> has been asked to initialize, and is holding its answer.
    /// </summary>
    private static Task StartingNow(HeldStartServer server) =>
        server.Initializing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

    /// <summary>
    /// Waits, up to ten seconds, for what a server holds to settle into <paramref name="condition"/>. The caller
    /// asserts afterwards, so a failure names what the server holds rather than that time ran out.
    /// </summary>
    private static async Task Settle(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(20, TestContext.Current.CancellationToken);
    }

    /// <summary>One open, before anything else, and every message after it later than the one before.</summary>
    private static void ToldInOrder(IReadOnlyList<Received> messages, string server)
    {
        messages.Should().NotBeEmpty($"{server} is told about the document");
        messages[0].Method.Should().Be("didOpen", $"{server} is opened on the document before it is told of any change to it");
        messages.Count(m => m.Method == "didOpen").Should().Be(1, $"{server} is opened on the document once");
        for (var i = 1; i < messages.Count; i++)
            messages[i].Version.Should().BeGreaterThan(messages[i - 1].Version,
                $"{server}'s versions only rise, and message {i} ({messages[i].Method}) followed {messages[i - 1].Method} v{messages[i - 1].Version}");
    }

    [AvaloniaFact]
    public async Task AnEditWhileTheOnlyServerStartsReachesIt()
    {
        var server = new HeldStartServer(held: true);
        var registry = Registry(Registration("vb6", server, priority: 0));

        var (_, document) = Started(registry);
        await StartingNow(server);

        document.Insert(0, "'");
        await Task.Delay(PastTheDebounce, TestContext.Current.CancellationToken);
        server.Release();

        await Settle(() => server.HeldText == Edited);
        server.HeldText.Should().Be(Edited, "the server ends up holding the text as edited");
        ToldInOrder(server.Messages, "the server");
    }

    [AvaloniaFact]
    public async Task ASaveWhileTheOnlyServerStartsSendsTheTextItSaved()
    {
        // The flush a save does was gated the same way, and it cancels the debounce first: so the edit was
        // dropped twice over, by the flush and by the change it cancelled.
        var server = new HeldStartServer(held: true);
        var registry = Registry(Registration("vb6", server, priority: 0));

        var (session, document) = Started(registry);
        await StartingNow(server);

        document.Insert(0, "'");
        // Reads the buffer on the document's UI thread before sending the save.
        var saving = session.NotifySavedAsync(TestContext.Current.CancellationToken);
        await Task.Delay(PastTheDebounce, TestContext.Current.CancellationToken);
        server.Release();
        await saving.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        await Settle(() => server.HeldText == Edited);
        server.HeldText.Should().Be(Edited, "the server ends up holding the text that was saved");
        ToldInOrder(server.Messages, "the server");
    }

    [AvaloniaFact]
    public async Task AnEditWhileASecondServerStartsReachesBothInOrder()
    {
        // The first server is up, so the registry reports running and the change goes out, to the first server
        // before its open and to the second before its handshake. Both ended up holding the text from before the
        // edit: the first because its late open replaced the change, the second because the registry's open
        // overwrote what its replay had just sent, at a lower version.
        var first = new HeldStartServer(held: false);
        var second = new HeldStartServer(held: true);
        var registry = Registry(Registration("first", first, priority: 1), Registration("second", second, priority: 0));

        var (_, document) = Started(registry);
        await StartingNow(second);

        document.Insert(0, "'");
        await Task.Delay(PastTheDebounce, TestContext.Current.CancellationToken);
        second.Release();

        await Settle(() => first.HeldText == Edited && second.HeldText == Edited);
        first.HeldText.Should().Be(Edited, "the first server ends up holding the text as edited");
        second.HeldText.Should().Be(Edited, "the second server ends up holding the text as edited");
        ToldInOrder(first.Messages, "the first server");
        ToldInOrder(second.Messages, "the second server");
    }

    [Fact]
    public async Task AnOpenNeverGoesBehindAChangeTheClientHasRecorded()
    {
        // The client's own half, with nothing in front of it. The registry routes a change to every claimant
        // that has a connection, including one not yet opened on the document, and sends its opens later; so a
        // change can reach a client before the open it follows does, carrying newer text than that open.
        var server = new HeldStartServer(held: false);
        var client = Client(server);
        await client.StartAsync(TestContext.Current.CancellationToken);

        await client.ChangeDocumentAsync(Uri, 3, Edited, TestContext.Current.CancellationToken);
        await client.OpenDocumentAsync(Uri, Original, TestContext.Current.CancellationToken);

        await Settle(() => server.Messages.Count > 0);
        server.Messages.Should().Equal(
            [new Received("didOpen", 3, Edited)],
            "the open carries the change already recorded, at its version, and the change is not sent ahead of it");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions) session.Dispose();
        foreach (var disposable in _disposables)
        {
            try { await disposable.DisposeAsync(); } catch { /* teardown is best effort */ }
        }
        GC.SuppressFinalize(this);
    }
}
