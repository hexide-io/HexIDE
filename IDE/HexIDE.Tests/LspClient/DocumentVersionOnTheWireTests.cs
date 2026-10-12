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
/// The versions a server receives for one document, from its open through its first change, with the open
/// sent by the client and the changes by a real document session.
///
/// <para>
/// <b>Both halves real, because the defect was in the seam between them</b> (hexide-io/HexIDE#470). The
/// client hardcoded the open's version and the session counted its changes from 0, so the first change
/// repeated the open's 1. Every existing test saw half of that: the session's own tests compared one change
/// with the next through a substitute, and the wire tests called <c>ChangeDocumentAsync</c> with a version
/// chosen by hand. Neither ever recorded an open and a change from the same document.
/// </para>
/// <para>
/// The last test asks what that cost rather than what was sent. A server that orders by version discards a
/// change that is not later than what it holds, and one measured in #470 does, so the assertion that matters
/// is on the text the server ends up holding.
/// </para>
/// </summary>
public class DocumentVersionOnTheWireTests : IAsyncDisposable
{
    private const string Uri = "file:///c:/proj/Module1.bas";
    private const string SyncCapabilities = """{"textDocumentSync":{"openClose":true,"change":1}}""";

    private readonly List<IAsyncDisposable> _disposables = [];
    private readonly List<IDisposable> _sessions = [];

    private sealed record Received(string Method, int Version, string Text);

    /// <summary>
    /// Records every open and change. Holds a document as a server that orders by version does: a change
    /// whose version is not later than the one held is discarded.
    /// </summary>
    private sealed class VersionRecordingServer
    {
        private readonly object gate = new();
        private readonly List<Received> received = [];
        private readonly TaskCompletionSource opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int heldVersion;

        public Task Opened => opened.Task;
        public Task Changed => changed.Task;
        public string HeldText { get; private set; } = "";

        public IReadOnlyList<Received> Messages
        {
            get { lock (gate) return [.. received]; }
        }

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) =>
            JsonDocument.Parse($$"""{"capabilities":{{SyncCapabilities}}}""").RootElement.Clone();

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
                heldVersion = version;
                HeldText = text;
            }
            opened.TrySetResult();
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
            changed.TrySetResult();
        }

        [JsonRpcMethod("textDocument/didClose", UseSingleObjectParameterDeserialization = true)]
        public void DidClose(JsonElement _) { }
    }

    /// <summary>A started client connected to a fresh server.</summary>
    private async Task<(VBLspClient Client, VersionRecordingServer Server)> Connected()
    {
        var server = new VersionRecordingServer();
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
        await client.StartAsync(TestContext.Current.CancellationToken);
        return (client, server);
    }

    /// <summary>
    /// A started session for one document, returned once its open has reached the server, as an edit or a
    /// save comes after the open for anyone typing into a window.
    /// </summary>
    /// <remarks>
    /// <b>Synchronous, and a caller must not await before its last use of the document.</b> A
    /// <see cref="TextDocument"/> accepts calls only from the thread that made it, and an await in a plain
    /// test can resume on any pool thread. The running IDE keeps this on the UI thread by construction.
    /// </remarks>
    private (LspDocumentSession Session, TextDocument Document) Opened(
        VBLspClient client, VersionRecordingServer server, string text)
    {
        var document = new TextDocument(text);
        var session = new LspDocumentSession(client, document, Uri, postToUiThread: work => work());
        _sessions.Add(session);
        session.Start();

        SpinWait.SpinUntil(() => server.Opened.IsCompleted, TimeSpan.FromSeconds(10))
            .Should().BeTrue("the open has to reach the server before anything is sent after it");
        return (session, document);
    }

    private static async Task FirstChange(VersionRecordingServer server) =>
        await server.Changed.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

    [Fact]
    public async Task TheDocumentIsOpenedAtTheOpeningVersion()
    {
        var (client, server) = await Connected();
        Opened(client, server, "Sub Main()\r\nEnd Sub\r\n");

        server.Messages.Should().ContainSingle()
            .Which.Should().Be(new Received("didOpen", LspDocumentVersion.Opening, "Sub Main()\r\nEnd Sub\r\n"));
    }

    [Fact]
    public async Task ASingleEditIsSentAtAVersionAfterTheOpens()
    {
        // One edit, then the debounce's quiet: the shape #470 named. A second keystroke inside the window
        // would raise the count before the one send goes out, which is why ordinary typing hid it.
        var (client, server) = await Connected();
        var (_, document) = Opened(client, server, "Sub Main()\r\nEnd Sub\r\n");

        document.Insert(0, "'");
        await FirstChange(server);

        var (open, change) = (server.Messages[0], server.Messages[1]);
        change.Method.Should().Be("didChange");
        change.Version.Should().BeGreaterThan(open.Version, "a change carries the version after the change, and the open's is already taken");
        change.Text.Should().Be("'Sub Main()\r\nEnd Sub\r\n");
    }

    [Fact]
    public async Task ASaveBeforeAnyEditFlushesAtAVersionAfterTheOpens()
    {
        // The second path #470 named: saving flushes first, and a flush before any edit was the first change.
        var (client, server) = await Connected();
        var (session, _) = Opened(client, server, "Sub Main()\r\nEnd Sub\r\n");

        // The flush reads the buffer before its first await, so this is still the document's own thread.
        await session.NotifySavedAsync(TestContext.Current.CancellationToken);
        await FirstChange(server);

        server.Messages[1].Method.Should().Be("didChange");
        server.Messages[1].Version.Should().BeGreaterThan(server.Messages[0].Version);
    }

    [Fact]
    public async Task AServerThatOrdersByVersionKeepsTheFirstEdit()
    {
        // What the repeated version cost. The server's copy is the one its diagnostics, folds and symbols
        // describe, so a discarded edit is analysis of text the window no longer shows, until the next edit.
        var (client, server) = await Connected();
        var (_, document) = Opened(client, server, "Sub Main()\r\nEnd Sub\r\n");

        document.Text = "Sub Main()\r\n    Debug.Print 1\r\nEnd Sub\r\n";
        await FirstChange(server);

        server.HeldText.Should().Be(
            "Sub Main()\r\n    Debug.Print 1\r\nEnd Sub\r\n",
            "a server that discards a change no later than what it holds must still be given the edit");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var s in _sessions) s.Dispose();
        foreach (var d in _disposables)
        {
            try { await d.DisposeAsync(); } catch { /* teardown is best effort */ }
        }
        GC.SuppressFinalize(this);
    }
}
