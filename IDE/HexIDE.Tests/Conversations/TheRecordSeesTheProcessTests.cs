using System.Buffers;
using System.Text;
using System.Text.Json;
using HexIDE.Conversations;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;
using StreamJsonRpc.Protocol;

namespace HexIDE.Tests.Conversations;

/// <summary>
/// The three things the record claimed to hold and did not.
/// </summary>
/// <remarks>
/// <b>All three were found by comparing the capture against the tool it replaces, not by a failing test.</b>
/// The change's own spec carries a SHALL — "the record SHALL include its start, its termination, its exit
/// code, and everything it wrote to standard error" — and its task list ticked that as done while standard
/// error went only to a debug log and nothing in the tree read an exit code. Worse, the stdio transport's
/// <c>Unobservable</c> returns null, which is the record's way of promising a reader that nothing is
/// missing: the honesty mechanism was certifying absent data as present.
///
/// <para>
/// The third is the sharpest. A frame this client could not decode produced no entry at all, because the
/// recording ran after the deserialize that threw — so the record was blind in exactly the case somebody
/// opens the window for, while the window happily rendered a "not valid JSON" marker no wire path could
/// reach. Every malformed-body test in the tree injected through <c>Record</c> directly, which is how it
/// stayed hidden.
/// </para>
/// </remarks>
public class TheRecordSeesTheProcessTests : IAsyncDisposable
{
    private readonly ConversationLog _log = new();

    public async ValueTask DisposeAsync()
    {
        await _log.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private CapturingFormatter Formatter() => new(new SystemTextJsonFormatter(), _log, "vb6");

    private async Task<IReadOnlyList<ConversationEnvelope>> EntriesAsync()
    {
        await _log.DrainAsync();
        return _log.Snapshot("vb6");
    }

    // ── An inbound frame this client cannot decode ───────────────────────────

    [Fact]
    public async Task AFrameThatCannotBeDecodedIsRecordedRatherThanLost()
    {
        // The bytes were already copied one line above the throw and were being dropped on the floor.
        _log.Arm("vb6", true);
        using var formatter = Formatter();

        var garbage = new ReadOnlySequence<byte>("{\"jsonrpc\":\"2.0\",\"id\":".Select(c => (byte)c).ToArray());

        var act = () => formatter.Deserialize(garbage);
        act.Should().Throw<Exception>("the caller must still be told — the record does not swallow it");

        var entries = await EntriesAsync();
        entries.Should().ContainSingle();
        entries[0].Kind.Should().Be(ConversationEntryKind.Note,
            "it is not a message: it has no method, no id, and no direction the protocol would recognise");
        entries[0].Detail.Should().Contain("undecodable frame");
    }

    [Fact]
    public async Task TheUndecodableBytesTravelWithTheEntry()
    {
        // A note saying "something arrived and I could not read it" is half an answer. The bytes are the
        // other half, and they are what gets pasted into a report to whoever wrote the server.
        _log.Arm("vb6", true);
        using var formatter = Formatter();

        const string wire = "{\"jsonrpc\":\"2.0\",\"id\":";
        var garbage = new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(wire));

        try { formatter.Deserialize(garbage); } catch (Exception) { /* expected */ }

        var entries = await EntriesAsync();
        var body = _log.Body("vb6", entries.Single().Sequence);

        body.Should().NotBeNull("the copy was taken before the deserialize that threw");
        Encoding.UTF8.GetString(body!.Head).Should().Be(wire);
    }

    [Fact]
    public async Task ADecodableFrameIsStillRecordedAsAMessage()
    {
        // The guard must not turn every inbound frame into a note.
        _log.Arm("vb6", true);
        using var formatter = Formatter();

        var wire = Encoding.UTF8.GetBytes(
            "{\"jsonrpc\":\"2.0\",\"method\":\"textDocument/didOpen\",\"params\":{}}");
        formatter.Deserialize(new ReadOnlySequence<byte>(wire));

        var entries = await EntriesAsync();
        entries.Should().ContainSingle();
        entries[0].Kind.Should().Be(ConversationEntryKind.Notification);
        entries[0].Method.Should().Be("textDocument/didOpen");
    }

    // ── An outbound message this client cannot serialize ─────────────────────

    [Fact]
    public async Task AMessageThatCannotBeSerializedIsRecordedAsNeverSent()
    {
        // The failure a byte proxy outside the process was immune to and a tap inside it is not: a type
        // missing from LspJsonContext throws here, the throw lands in a debug-level catch upstream, and
        // the server looks broken. Nothing crossed the wire, which is exactly the finding.
        using var formatter = Formatter();

        var unserializable = new JsonRpcRequest
        {
            RequestId = new RequestId(1),
            Method = "textDocument/hover",
            Arguments = new object[] { new Unserializable() },
        };

        var writer = new ArrayBufferWriter<byte>();
        try { formatter.Serialize(writer, unserializable); } catch (Exception) { /* expected */ }

        var entries = await EntriesAsync();
        entries.Should().ContainSingle();
        entries[0].Kind.Should().Be(ConversationEntryKind.NeverSent);
        entries[0].Method.Should().Be("textDocument/hover",
            "the method is what a reader scans for, and it survives even when the bytes do not");
        entries[0].Detail.Should().Contain("could not be serialized");
    }

    /// <summary>A type System.Text.Json refuses, standing in for one missing from the context.</summary>
    private sealed class Unserializable
    {
        public Unserializable Self => this;   // a cycle: serialization throws rather than recursing forever
    }

    // ── Standard error and the exit code ─────────────────────────────────────

    // Whether the stdio transport genuinely observes a real process — its stderr lines and its exit code —
    // is proved against one, in StdioProcessNoticeTests. What is proved here is the other half: that what
    // the transport reports lands on the record, as the right kind, attributed to the right connection.

    [Fact]
    public async Task AStandardErrorLineLandsOnTheRecordAsSomethingThatArrived()
    {
        // The spec's scenario is "A server crashes": its dying words are on standard error, and the record
        // is where somebody looks for them. Direction matters and is not a detail — ConversationDirection
        // spells out that Received is "a reply, a notification, a line of standard error", and a stderr
        // line filed as Local would sit in the column for things HexIDE did.
        await using var connection = await Connected("vb6");

        connection.Transport.Say(TransportNoticeKind.StandardError, "Unhandled exception. System.Exception");

        var entry = await connection.WaitForAsync(ConversationEntryKind.StandardError);
        entry.Detail.Should().Be("Unhandled exception. System.Exception", "verbatim, or it is not the server's words");
        entry.Direction.Should().Be(ConversationDirection.Received);
        entry.Method.Should().BeNull("standard error is not a protocol message and must not look like one");
    }

    [Fact]
    public async Task TheExitCodeLandsOnTheRecordAsSomethingObservedRatherThanReceived()
    {
        // The same scenario's other half. An exit code did not arrive over the channel — nothing sent it —
        // which is exactly what Local is for: "a process starting, a transport reporting what it cannot
        // observe".
        await using var connection = await Connected("vb6");

        connection.Transport.Say(TransportNoticeKind.Lifecycle, "process exited with code 1");

        var entry = await connection.WaitForAsync(ConversationEntryKind.Lifecycle, "process exited with code 1");
        entry.Direction.Should().Be(ConversationDirection.Local);
    }

    [Fact]
    public async Task WithSeveralServersAttachedEachOnesOutputStaysWithIt()
    {
        // The spec's second scenario, and the reason attribution is the client's job rather than the
        // transport's: a transport does not know which connection it is. Two servers failing at once is
        // when a person most needs the record, and a stderr line on the wrong timeline is worse than none.
        await using var vb6 = await Connected("vb6");
        await using var markdown = await Connected("markdown");

        vb6.Transport.Say(TransportNoticeKind.StandardError, "vb6 is unwell");
        markdown.Transport.Say(TransportNoticeKind.StandardError, "markdown is unwell");

        (await vb6.WaitForAsync(ConversationEntryKind.StandardError)).Detail.Should().Be("vb6 is unwell");
        (await markdown.WaitForAsync(ConversationEntryKind.StandardError)).Detail.Should().Be("markdown is unwell");

        vb6.Entries().Should().NotContain(e => e.Detail == "markdown is unwell");
        markdown.Entries().Should().NotContain(e => e.Detail == "vb6 is unwell");
    }

    [Fact]
    public async Task TheProcessOutputSharesOneTimelineWithTheMessages()
    {
        // A separate stderr pane is what every other tool offers and it is the thing that does not answer
        // the question. "What was the last thing it managed to send before it died" is only answerable if
        // both are on one ordered list — so the entry has to carry a sequence from the same counter the
        // messages do, not merely exist somewhere in the same window.
        await using var connection = await Connected("vb6");

        connection.Transport.Say(TransportNoticeKind.StandardError, "the last thing it said");
        var stderr = await connection.WaitForAsync(ConversationEntryKind.StandardError);

        var handshake = connection.Entries().Where(e => e.Kind != ConversationEntryKind.StandardError).ToList();
        handshake.Should().NotBeEmpty("the handshake happened, so the timeline is not just this one entry");
        stderr.Sequence.Should().BeGreaterThan(handshake.Max(e => e.Sequence),
            "it arrived after everything the connection had said, and the record has to show that");
    }

    // ── The fake the client half needs ───────────────────────────────────────

    private async Task<Connection> Connected(string connectionId)
    {
        var transport = new NoticingFakeTransport();
        var client = new VBLspClient(
            transport, Substitute.For<ILogger<VBLspClient>>(), connectionId,
            capture: _log, connectionId: connectionId);

        await client.StartAsync(TestContext.Current.CancellationToken);
        return new Connection(client, transport, _log, connectionId);
    }

    private sealed record Connection(
        VBLspClient Client, NoticingFakeTransport Transport, ConversationLog Log, string Id)
        : IAsyncDisposable
    {
        public IReadOnlyList<ConversationEnvelope> Entries() => Log.Snapshot(Id);

        /// <summary>
        /// Waits for one entry of a kind. The record is written through a queue that a drain empties, and
        /// the notice is raised from whichever thread the transport was on, so reading straight after
        /// saying something is a race the transport would lose about half the time.
        /// </summary>
        public async Task<ConversationEnvelope> WaitForAsync(ConversationEntryKind kind, string? detail = null)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                await Log.DrainAsync();
                var found = Entries().LastOrDefault(
                    e => e.Kind == kind && (detail is null || e.Detail == detail));
                if (found is not null) return found;
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            throw new InvalidOperationException(
                $"no {kind} entry{(detail is null ? "" : $" saying '{detail}'")} reached the record for '{Id}'");
        }

        public async ValueTask DisposeAsync() => await Client.DisposeAsync();
    }

    /// <summary>
    /// A transport that connects for real and lets the test be the process.
    /// </summary>
    /// <remarks>
    /// It connects rather than stubbing, because the point of these is that a notice lands on the SAME
    /// timeline as the handshake it interleaves with — which needs a handshake to have happened. What it
    /// does not do is own a process; that half is proved against a real one in StdioProcessNoticeTests.
    /// </remarks>
    public sealed class NoticingFakeTransport : ILspTransport
    {
        private readonly List<IDisposable> _rpcs = [];
        private Stream? _clientSide;

        public bool IsAlive => true;
        public string? LastFailure => null;
        public bool CanReconnect => false;
        public string? Unobservable => null;

        public event EventHandler? Closed { add { } remove { } }
        public event EventHandler<TransportNotice>? Notice;

        /// <summary>Says what a process would have said.</summary>
        public void Say(TransportNoticeKind kind, string text) =>
            Notice?.Invoke(this, new TransportNotice(kind, text));

        public Task<IJsonRpcMessageHandler?> ConnectAsync(
            IJsonRpcMessageFormatter formatter, CancellationToken cancellationToken = default)
        {
            var (clientSide, serverSide) = FullDuplexStream.CreatePair();
            _clientSide = clientSide;

            var serverRpc = new JsonRpc(
                new HeaderDelimitedMessageHandler(serverSide, serverSide, new SystemTextJsonFormatter()),
                new AnswersTheHandshake());
            _rpcs.Add(serverRpc);
            serverRpc.StartListening();

            return Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(clientSide, clientSide, formatter));
        }

        public ValueTask DisposeAsync()
        {
            foreach (var rpc in _rpcs) rpc.Dispose();
            _clientSide?.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>The least a server can do and still be connected to.</summary>
    private sealed class AnswersTheHandshake
    {
        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) =>
            JsonDocument.Parse("""{"capabilities":{}}""").RootElement.Clone();

        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }
    }
}
