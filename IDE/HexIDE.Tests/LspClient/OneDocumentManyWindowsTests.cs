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
/// Two code windows over one name, on one real client, as a server sees them (hexide-io/HexIDE#721).
///
/// <para>
/// One file shared by two projects of a group opens two windows that both name it by its file. The server has one
/// document for that name, so what it must see is one document: one open, versions that only rise whichever window
/// an edit came from, edits from a window still open after the other closed, and one close at the end.
/// </para>
/// </summary>
public class OneDocumentManyWindowsTests : IAsyncDisposable
{
    private const string Uri = "file:///c:/proj/Shared.bas";
    private const string Original = "Sub Main()\r\nEnd Sub\r\n";

    private readonly List<IAsyncDisposable> _disposables = [];
    private readonly List<IDisposable> _sessions = [];

    private sealed record Received(string Method, int Version, string Text);

    /// <summary>Records every open, change and close, in the order they arrive.</summary>
    private sealed class RecordingServer
    {
        private readonly object gate = new();
        private readonly List<Received> received = [];

        public IReadOnlyList<Received> Messages
        {
            get { lock (gate) return [.. received]; }
        }

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) =>
            JsonDocument.Parse("""{"capabilities":{"textDocumentSync":{"openClose":true,"change":1}}}""").RootElement.Clone();

        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }

        [JsonRpcMethod("textDocument/didOpen", UseSingleObjectParameterDeserialization = true)]
        public void DidOpen(JsonElement p)
        {
            var item = p.GetProperty("textDocument");
            Add(new Received("didOpen", item.GetProperty("version").GetInt32(), item.GetProperty("text").GetString()!));
        }

        [JsonRpcMethod("textDocument/didChange", UseSingleObjectParameterDeserialization = true)]
        public void DidChange(JsonElement p) =>
            Add(new Received("didChange", p.GetProperty("textDocument").GetProperty("version").GetInt32(),
                p.GetProperty("contentChanges")[0].GetProperty("text").GetString()!));

        [JsonRpcMethod("textDocument/didClose", UseSingleObjectParameterDeserialization = true)]
        public void DidClose(JsonElement _) => Add(new Received("didClose", 0, ""));

        private void Add(Received message)
        {
            lock (gate) received.Add(message);
        }
    }

    private async Task<(VBLspClient Client, RecordingServer Server)> Connected()
    {
        var server = new RecordingServer();
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

    private LspDocumentSession Window(VBLspClient client, TextDocument document)
    {
        var session = new LspDocumentSession(client, document, Uri, postToUiThread: work => work());
        _sessions.Add(session);
        return session;
    }

    /// <summary>
    /// Waits, up to ten seconds, for the server to have received <paramref name="count"/> messages, so that each
    /// step's message is on the wire before the next step sends its own.
    /// </summary>
    private static async Task UntilReceived(RecordingServer server, int count)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (server.Messages.Count < count && DateTime.UtcNow < deadline)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        server.Messages.Count.Should().BeGreaterThanOrEqualTo(count);
    }

    [AvaloniaFact]
    public async Task TwoWindowsOverOneNameAreOneDocumentToTheServer()
    {
        var (client, server) = await Connected();
        var first = new TextDocument(Original);
        var second = new TextDocument(Original);
        var a = Window(client, first);
        var b = Window(client, second);

        a.Start();
        await UntilReceived(server, 1);
        b.Start();

        first.Insert(0, "'");
        _ = a.FlushAsync(TestContext.Current.CancellationToken);
        await UntilReceived(server, 2);

        second.Insert(0, "''");
        _ = b.FlushAsync(TestContext.Current.CancellationToken);
        await UntilReceived(server, 3);

        a.Dispose();
        second.Insert(0, "'");
        _ = b.FlushAsync(TestContext.Current.CancellationToken);
        await UntilReceived(server, 4);

        b.Dispose();
        await UntilReceived(server, 5);

        server.Messages.Select(m => m.Method).Should().Equal(
            ["didOpen", "didChange", "didChange", "didChange", "didClose"],
            "the server sees one document: opened once, changed from either window, closed when the last window closes");
        var versions = server.Messages.Where(m => m.Method != "didClose").Select(m => m.Version).ToList();
        versions.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems("the document's versions only rise, whichever window an edit came from");
        server.Messages[3].Text.Should().Be("'''" + Original, "the window still open goes on editing the document after the other closed");
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
