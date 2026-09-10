using System.Text;
using System.Text.Json;
using HexIDE.Conversations;
using HexIDE.Lsp;
using Nerdbank.Streams;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.Conversations;

/// <summary>
/// The tap, driven through a real JSON-RPC connection.
/// </summary>
/// <remarks>
/// <b>Half of this class asserts that the connection still WORKS, and that is the point.</b> A formatter
/// wrapper fails in a particular way: it forwards five of the six interfaces the pipeline expects, requests
/// and responses keep flowing, and inbound notifications quietly stop being delivered. No exception, no
/// log, a healthy-looking connection, and <c>publishDiagnostics</c> gone. Measured, not imagined.
///
/// <para>
/// So a test that only checked "frames were captured" would pass against a wrapper that had broken the very
/// thing the capture exists to observe. Every capture assertion here is paired with a delivery assertion.
/// </para>
/// </remarks>
public class CapturingFormatterTests : IAsyncDisposable
{
    private readonly ConversationLog _log = new();
    private readonly List<IDisposable> _disposables = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var d in _disposables) { try { d.Dispose(); } catch { /* teardown */ } }
        await _log.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>A server that answers, and can be told to send a notification of its own.</summary>
    private sealed class Server
    {
        public TaskCompletionSource<string> Greeted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) =>
            JsonDocument.Parse("""{"capabilities":{"hoverProvider":true}}""").RootElement.Clone();

        [JsonRpcMethod("greet")]
        public void Greet(string who) => Greeted.TrySetResult(who);
    }

    /// <summary>The client half, whose formatter is the one under test.</summary>
    private (JsonRpc Client, JsonRpc Peer, Server PeerTarget) Connect(string connectionId = "vb6")
    {
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();

        var peerTarget = new Server();
        var peer = new JsonRpc(
            new HeaderDelimitedMessageHandler(serverSide, serverSide, new SystemTextJsonFormatter()), peerTarget);
        peer.StartListening();

        var tapped = new CapturingFormatter(new SystemTextJsonFormatter(), _log, connectionId);
        var client = new JsonRpc(new HeaderDelimitedMessageHandler(clientSide, clientSide, tapped));

        _disposables.Add(peer);
        _disposables.Add(client);
        return (client, peer, peerTarget);
    }

    // ── That it still works ──────────────────────────────────────────────────

    [Fact]
    public async Task AnInboundNotificationIsStillDelivered()
    {
        // THE assertion. This is the one a missing interface forward breaks, and it breaks it silently:
        // requests and responses carry on working, so every other test in this file would still pass.
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();

        var clientTarget = new Server();
        var tapped = new CapturingFormatter(new SystemTextJsonFormatter(), _log, "vb6");
        var client = new JsonRpc(new HeaderDelimitedMessageHandler(clientSide, clientSide, tapped), clientTarget);
        client.StartListening();
        _disposables.Add(client);

        var peer = new JsonRpc(
            new HeaderDelimitedMessageHandler(serverSide, serverSide, new SystemTextJsonFormatter()));
        peer.StartListening();
        _disposables.Add(peer);

        await peer.NotifyAsync("greet", "world");

        var arrived = await Task.WhenAny(clientTarget.Greeted.Task, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        arrived.Should().Be(clientTarget.Greeted.Task,
            "a wrapper that fails to forward IJsonRpcInstanceContainer leaves requests working and drops "
          + "inbound notifications with no error at all — which is publishDiagnostics disappearing");
        (await clientTarget.Greeted.Task).Should().Be("world");
    }

    [Fact]
    public async Task ARequestStillGetsItsAnswer()
    {
        var (client, _, _) = Connect();
        client.StartListening();

        var result = await client.InvokeWithParameterObjectAsync<JsonElement>(
            "initialize", new { processId = 1 }, TestContext.Current.CancellationToken);

        result.GetProperty("capabilities").GetProperty("hoverProvider").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task AnOutboundNotificationStillArrives()
    {
        var (client, _, peerTarget) = Connect();
        client.StartListening();

        await client.NotifyAsync("greet", "there");

        var arrived = await Task.WhenAny(peerTarget.Greeted.Task, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        arrived.Should().Be(peerTarget.Greeted.Task);
    }

    // ── That it captures ─────────────────────────────────────────────────────

    [Fact]
    public async Task BothDirectionsAreRecorded()
    {
        var (client, _, _) = Connect();
        client.StartListening();

        await client.InvokeWithParameterObjectAsync<JsonElement>(
            "initialize", new { processId = 1 }, TestContext.Current.CancellationToken);
        await _log.DrainAsync();

        var entries = _log.Snapshot("vb6");
        entries.Should().ContainSingle("the request and its reply are one exchange");
        entries[0].Method.Should().Be("initialize");
        entries[0].Kind.Should().Be(ConversationEntryKind.Request);
        entries[0].Outcome.Should().Be(ConversationOutcome.Answered,
            "the inbound reply completed the outbound request, so both directions were seen");
        entries[0].SizeBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SizeIsRecordedWithoutRetainingAnything()
    {
        // The whole reason capture can be on by default. An unarmed connection knows how big a reply was
        // without holding a byte of it.
        var (client, _, _) = Connect();
        client.StartListening();

        await client.InvokeWithParameterObjectAsync<JsonElement>(
            "initialize", new { processId = 1 }, TestContext.Current.CancellationToken);
        await _log.DrainAsync();

        var entry = _log.Snapshot("vb6").Single();
        entry.SizeBytes.Should().BeGreaterThan(0);
        _log.Body("vb6", entry.Sequence).Should().BeNull("nothing was armed, so nothing was kept");
    }

    [Fact]
    public async Task AnArmedConnectionKeepsTheBytesThatCrossedTheWire()
    {
        _log.Arm("vb6", true);
        var (client, _, _) = Connect();
        client.StartListening();

        await client.NotifyAsync("greet", "byte-exact");
        await _log.DrainAsync();

        var entry = _log.Snapshot("vb6").Single(e => e.Method == "greet");
        var body = _log.Body("vb6", entry.Sequence);

        body.Should().NotBeNull();
        var text = Encoding.UTF8.GetString(body!.Head);
        text.Should().StartWith("{").And.Contain("\"method\":\"greet\"").And.Contain("byte-exact",
            "the capture is the wire body, not a re-serialization of a decoded object");
        body.TrueLength.Should().Be(body.Head.Length, "this frame was small enough to keep whole");
    }

    [Fact]
    public async Task ANotificationIsNotMistakenForARequest()
    {
        // A notification is a request with nobody waiting. Recording it as a request would leave an entry
        // permanently outstanding, and a timeline full of things that apparently never came back.
        _log.Arm("vb6", true);
        var (client, _, _) = Connect();
        client.StartListening();

        await client.NotifyAsync("greet", "x");
        await _log.DrainAsync();

        _log.Snapshot("vb6").Single(e => e.Method == "greet")
            .Kind.Should().Be(ConversationEntryKind.Notification);
    }

    [Fact]
    public async Task TwoConnectionsAreKeptApart()
    {
        var (first, _, _) = Connect("vb6");
        var (second, _, _) = Connect("latex");
        first.StartListening();
        second.StartListening();

        await first.NotifyAsync("greet", "a");
        await second.NotifyAsync("greet", "b");
        await _log.DrainAsync();

        _log.Snapshot("vb6").Should().ContainSingle();
        _log.Snapshot("latex").Should().ContainSingle();
        _log.Snapshot().Should().HaveCount(2, "one timeline, and both connections are in it");
    }
}
