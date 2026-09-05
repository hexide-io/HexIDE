using System.Collections.Concurrent;
using System.Text.Json;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// What a server is told about its documents after the connection comes back.
///
/// <para>
/// The replay is easy to overlook — it runs only after a disconnect, which never happens in ordinary
/// testing — and it had drifted from the ordinary open in two ways at once: the language identifier was
/// hardcoded to <c>vb6</c>, and the capability gate was missing. Both were invisible against our own
/// server, which is a VB6 server that wants open/close, so nothing could disagree.
/// </para>
///
/// <para>
/// These tests drive a real reconnect over a reconnectable transport rather than calling the replay
/// directly, because "does the replay do the right thing" and "is the replay reached, with the state it
/// needs" are different claims and only the second catches an ordering mistake.
/// </para>
/// </summary>
public class ReconnectReplayTests : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _disposables = [];
    private readonly List<IDisposable> _rpcs = [];

    /// <summary>Records every didOpen it is sent, across however many connections it serves.</summary>
    private sealed class OpenRecordingServer(string capabilitiesJson)
    {
        public ConcurrentQueue<(string Uri, string LanguageId, int Version, string Text)> Opens { get; } = new();

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) =>
            JsonDocument.Parse($$"""{"capabilities":{{capabilitiesJson}}}""").RootElement.Clone();

        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }

        [JsonRpcMethod("textDocument/didOpen", UseSingleObjectParameterDeserialization = true)]
        public void DidOpen(JsonElement p)
        {
            var d = p.GetProperty("textDocument");
            Opens.Enqueue((
                d.GetProperty("uri").GetString()!,
                d.GetProperty("languageId").GetString()!,
                d.GetProperty("version").GetInt32(),
                d.GetProperty("text").GetString()!));
        }
    }

    /// <summary>
    /// A transport that can be dropped and will hand out a fresh stream pair when reconnected — which is
    /// what makes a reconnect reachable at all. A substitute returning one handler cannot be reconnected:
    /// the second attempt runs over a stream the first already owns.
    /// </summary>
    private sealed class ReconnectableFakeTransport(OpenRecordingServer server, List<IDisposable> rpcs)
        : ILspTransport
    {
        public bool IsAlive { get; private set; } = true;
        public bool CanReconnect => true;
        public event EventHandler? Closed;

        private Stream? _clientSide;

        public Task<IJsonRpcMessageHandler?> ConnectAsync(
            IJsonRpcMessageFormatter formatter, CancellationToken cancellationToken = default)
        {
            var (clientSide, serverSide) = FullDuplexStream.CreatePair();
            _clientSide = clientSide;
            var serverRpc = new JsonRpc(
                new HeaderDelimitedMessageHandler(serverSide, serverSide, new SystemTextJsonFormatter()),
                server);
            rpcs.Add(serverRpc);
            serverRpc.StartListening();
            IsAlive = true;
            return Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(clientSide, clientSide, formatter));
        }

        /// <summary>Drops the connection the way a server exiting would.</summary>
        public void Drop()
        {
            IsAlive = false;
            _clientSide?.Dispose();
            Closed?.Invoke(this, EventArgs.Empty);
        }

        public ValueTask DisposeAsync()
        {
            _clientSide?.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private (VBLspClient Client, OpenRecordingServer Server, ReconnectableFakeTransport Transport)
        Connected(string languageId, string capabilitiesJson)
    {
        var server = new OpenRecordingServer(capabilitiesJson);
        var transport = new ReconnectableFakeTransport(server, _rpcs);
        var client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), languageId);
        _disposables.Add(client);
        return (client, server, transport);
    }

    /// <summary>Waits for the replayed open to arrive; the reconnect loop backs off before retrying.</summary>
    private static async Task<bool> WaitForOpensAsync(OpenRecordingServer server, int count)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (server.Opens.Count < count && DateTime.UtcNow < deadline)
            await Task.Delay(100);
        return server.Opens.Count >= count;
    }

    [Fact]
    public async Task AReplayedDocumentKeepsTheLanguageItsServerWasGiven()
    {
        // #272. Every document was replayed as "vb6" regardless of which server it belonged to, so after
        // any reconnect a Markdown server held a document set it could not analyse. The failure is silent
        // and permanent: features that worked, stopped, and never came back until the tab was reopened.
        var (client, server, transport) = Connected(
            "markdown", """{"textDocumentSync":{"openClose":true,"change":1}}""");
        await client.StartAsync();
        await client.OpenDocumentAsync("file:///c:/p/README.md", "# hello");
        (await WaitForOpensAsync(server, 1)).Should().BeTrue("the first open must arrive before we drop");

        transport.Drop();

        (await WaitForOpensAsync(server, 2)).Should().BeTrue("the document should be replayed on reconnect");
        server.Opens.Select(o => o.LanguageId).Should().AllBe(
            "markdown", "a replay is a re-open, and must say the same thing the open said");
    }

    [Fact]
    public async Task AServerThatDoesNotWantOpenCloseIsNotSentAReplayEither()
    {
        // The second half of the same drift. The replay skipped the capability gate the ordinary open
        // applies, so a server that declined open/close received them anyway — but only after a
        // reconnect, which is the hardest circumstance in which to notice.
        var (client, server, transport) = Connected(
            "markdown", """{"textDocumentSync":{"openClose":false,"change":1}}""");
        await client.StartAsync();
        await client.OpenDocumentAsync("file:///c:/p/README.md", "# hello");

        transport.Drop();
        await Task.Delay(3000);   // long enough for the backoff to reconnect and replay

        server.Opens.Should().BeEmpty("it declined open/close, and a reconnect does not change its mind");
    }

    [Fact]
    public async Task AReplayCarriesTheTextTheDocumentActuallyHas()
    {
        // The replay exists so a fresh server regains the document set. Replaying the text as it was at
        // open, rather than as it is now, would hand it a stale copy to publish diagnostics from.
        var (client, server, transport) = Connected(
            "markdown", """{"textDocumentSync":{"openClose":true,"change":1}}""");
        await client.StartAsync();
        await client.OpenDocumentAsync("file:///c:/p/README.md", "# first");
        (await WaitForOpensAsync(server, 1)).Should().BeTrue();
        await client.ChangeDocumentAsync("file:///c:/p/README.md", 2, "# second");

        transport.Drop();

        (await WaitForOpensAsync(server, 2)).Should().BeTrue();
        server.Opens.Last().Text.Should().Be("# second");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var d in _disposables)
        {
            try { await d.DisposeAsync(); } catch { /* teardown is best effort */ }
        }
        foreach (var r in _rpcs)
        {
            try { r.Dispose(); } catch { /* teardown is best effort */ }
        }
        GC.SuppressFinalize(this);
    }
}
