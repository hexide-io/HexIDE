using System.Buffers;
using System.Text;
using System.Text.Json;
using HexIDE.Conversations;
using StreamJsonRpc;
using StreamJsonRpc.Protocol;
using StreamJsonRpc.Reflection;

namespace HexIDE.Lsp;

/// <summary>
/// A message formatter that copies what crosses it into a <see cref="ConversationLog"/> when there is one,
/// repairs the one frame shape that would otherwise cost a connection, and otherwise does nothing at all.
/// </summary>
/// <remarks>
/// <b>Always installed, even with no log to write to.</b> It was conditional, and that was a defect rather
/// than an economy: the <c>params: null</c> repair below lives here, so a connection survived a malformed
/// frame only while somebody happened to be recording it. A diagnostic facility must never be what keeps
/// the thing it observes alive — and the inverse is just as bad, since it makes the recorded connection
/// behave differently from the one a user has. With no log the wrapper records nothing and costs a null
/// check per frame. See <see cref="RepairNullParams"/>.
///
/// <para>
/// <b>Wrapping rather than subclassing, and that is not a style preference.</b> Every serialization member
/// on the underlying formatter is <c>virtual final newslot</c> — the runtime's encoding of an ordinary
/// non-virtual interface implementation — so an <c>override</c> does not compile, and the <c>new</c> that
/// does compile is never called: a subclass built that way runs perfectly, captures zero frames, and gives
/// no indication of it. Measured on the pinned assembly.
///
/// <para>
/// <b>Six interfaces, and forwarding all of them is a correctness requirement.</b> The one that costs most
/// to miss is <see cref="IJsonRpcInstanceContainer"/>: omit it and requests and responses keep working
/// while inbound <em>notifications</em> stop being delivered, with no exception, no log and a healthy
/// connection. That is <c>publishDiagnostics</c> vanishing — the same silent-blackout shape this client has
/// already been bitten by once. Any test here must assert a notification was <b>delivered</b>, never merely
/// that a call did not throw.
/// </para>
///
/// <para>
/// <b>Both deserialize overloads, because HexIDE has three transports and they disagree.</b> Standard
/// input and named pipes call the two-argument overload exclusively; a web socket calls the one-argument
/// overload exclusively and does not require the text interface at all. Implementing one of them leaves the
/// tap blind on a transport rather than broken, which is the harder kind of gap to notice.
/// </para>
///
/// <para>
/// <b>One of these per connection.</b> The underlying formatter refuses to be associated with a second
/// <see cref="JsonRpc"/> instance, so a shared wrapper would fail on the first reconnect. The log behind it
/// is shared, which is what lets a connection's record survive its server being restarted.
/// </para>
/// </remarks>
internal sealed class CapturingFormatter(
    IJsonRpcMessageTextFormatter inner,
    ConversationLog? log,
    string? connectionId)
    : IJsonRpcMessageTextFormatter, IJsonRpcMessageFactory, IJsonRpcInstanceContainer, IJsonRpcFormatterState,
      IJsonRpcFormatterTracingCallbacks, IJsonRpcMessageBufferManager, IDisposable
{
    private readonly IJsonRpcMessageFactory _factory = (IJsonRpcMessageFactory)inner;
    private readonly IJsonRpcFormatterState _state = (IJsonRpcFormatterState)inner;

    /// <summary>Whether anybody wants the bytes. False whenever there is nothing to write them to.</summary>
    private bool ShouldKeepBody() =>
        log is not null && connectionId is not null && log.ShouldKeepBody(connectionId);

    /// <summary>
    /// Writes one entry, if there is a log. Every caller is on a path where a throw is unwelcome and some
    /// are on one where it is fatal, so this swallows unconditionally.
    /// </summary>
    private void Note(
        ConversationDirection direction, ConversationEntryKind kind,
        string? method, string? id, int size, byte[]? body, string? detail = null)
    {
        if (log is null || connectionId is null) return;
        try { log.Record(connectionId, direction, kind, method, id, size, body, detail); }
        catch (Exception) { /* a capture is never worth breaking anything for */ }
    }

    public Encoding Encoding
    {
        get => inner.Encoding;
        set => inner.Encoding = value;
    }

    // ── The tap ──────────────────────────────────────────────────────────────

    public void Serialize(IBufferWriter<byte> bufferWriter, JsonRpcMessage message)
    {
        // Tee rather than serialize twice. The bytes the inner formatter writes are exactly the bytes the
        // handler will frame, so watching them go past costs one copy when armed and one addition when not.
        var tee = new TeeWriter(bufferWriter, ShouldKeepBody());

        try
        {
            inner.Serialize(tee, message);
        }
        catch (Exception ex)
        {
            // A tap INSIDE the client's own serialization stack can be blinded by a client-side defect that
            // a byte proxy sitting outside the process was immune to. The specific one is documented and
            // has cost this project time twice: a type missing from LspJsonContext throws here, the throw
            // lands in a debug-level catch upstream, and the result is a server that connects, initializes
            // and then answers nothing — indistinguishable from a broken server.
            //
            // So the attempt goes on the record. Nothing crossed the wire, which is exactly the finding.
            Note(
                ConversationDirection.Local, ConversationEntryKind.NeverSent,
                MethodOf(message), null, 0, null, $"could not be serialized: {ex.Message}");

            throw;
        }

        // Everything below is best-effort and swallows. A throw here is survivable — measured, the one
        // call fails and the connection lives — but "survivable" is not a reason to spend the connection's
        // luck on a diagnostic.
        try { Record(message, ConversationDirection.Sent, tee.Length, tee.Captured); }
        catch (Exception) { /* a capture is never worth breaking anything for */ }
    }

    public JsonRpcMessage Deserialize(ReadOnlySequence<byte> contentBuffer) =>
        Capture(contentBuffer, buffer => inner.Deserialize(buffer));

    public JsonRpcMessage Deserialize(ReadOnlySequence<byte> contentBuffer, Encoding encoding) =>
        Capture(contentBuffer, buffer => inner.Deserialize(buffer, encoding));

    /// <summary>
    /// Copies the frame if anyone asked for it, then hands the buffer on unchanged.
    /// </summary>
    /// <remarks>
    /// <b>The copy happens here or never.</b> The buffer is invalid the instant this method returns —
    /// measured across forty-two sequences held past the return, every one of which threw on re-read — and
    /// the signature is synchronous, so there is no await to defer behind.
    ///
    /// <para>
    /// <b>And nothing in the capture may throw on this path.</b> A throw here is not survivable the way an
    /// outbound one is: it surfaces as a lost connection and the connection stays lost. So the swallow is
    /// unconditional and the recording happens after the real work, never before it.
    /// </para>
    /// </remarks>
    private JsonRpcMessage Capture(
        ReadOnlySequence<byte> contentBuffer, Func<ReadOnlySequence<byte>, JsonRpcMessage> deserialize)
    {
        var length = (int)contentBuffer.Length;
        byte[]? copy = null;
        try
        {
            if (ShouldKeepBody()) copy = contentBuffer.ToArray();
        }
        catch (Exception) { copy = null; }

        JsonRpcMessage message;
        try
        {
            message = deserialize(contentBuffer);
        }
        catch (Exception ex)
        {
            // THE ONE FRAME MOST WORTH RECORDING, and until now the only one that was not.
            //
            // Record ran after deserialize, so a body this client could not decode threw straight past it
            // and the bytes — already copied, one line above — were dropped on the floor. A server emitting
            // malformed JSON is a real defect and this window exists to show it, so the record was blind in
            // exactly the case somebody opens it for. The window even renders the "not valid JSON" marker,
            // which no wire path could reach.
            //
            // Recorded as a note rather than as a message, because it is not one: it has no method, no id
            // and no direction the protocol would recognise. The bytes travel with it.
            Note(
                ConversationDirection.Received, ConversationEntryKind.Note,
                null, null, length, copy, $"undecodable frame: {ex.Message}");

            // One repair, tried once, before spending the connection on it. See RepairNullParams.
            if (RepairNullParams(contentBuffer) is { } repaired)
            {
                try
                {
                    message = deserialize(new ReadOnlySequence<byte>(repaired));
                    Note(
                        ConversationDirection.Received, ConversationEntryKind.Note,
                        null, null, length, copy,
                        "recovered: the frame above carried `\"params\": null`, which JSON-RPC 2.0 does "
                      + "not permit. Read as though the member were absent. The connection was kept.");

                    return message;
                }
                catch (Exception) { /* the repair did not help; fall through to the original failure */ }
            }

            throw;
        }

        try { Record(message, ConversationDirection.Received, length, copy); }
        catch (Exception) { /* as above, and more so */ }

        return message;
    }

    /// <summary>
    /// The same frame with a <c>"params": null</c> member removed, or null when that is not what is wrong.
    /// </summary>
    /// <remarks>
    /// <b>This exists because one malformed message used to cost the whole connection, not just the message.</b>
    /// A frame that cannot be decoded surfaces as a stream error, and a stream error tears the connection
    /// down — so a single notification a server should not have sent takes every language feature with it,
    /// permanently, on a transport that cannot re-dial. That asymmetry is the defect; the server's mistake is
    /// only the trigger.
    ///
    /// <para>
    /// <b>Found by declaring one capability.</b> Once this client says it can ask for diagnostics, rumdl
    /// sends <c>{"jsonrpc":"2.0","method":"workspace/diagnostic/refresh","params":null,"id":1}</c>.
    /// JSON-RPC 2.0 allows <c>params</c> to be an object or an array and nothing else, so <c>null</c> is
    /// invalid and the reader is right to reject it — and the specification's own reading of
    /// <c>workspace/diagnostic/refresh</c> is that it takes no parameters at all, which is what "absent"
    /// means. So dropping the member yields exactly the message the server meant to send.
    /// </para>
    ///
    /// <para>
    /// <b>Deliberately this one repair and no other.</b> It is not a licence to correct servers generally:
    /// guessing at a frame's intent is how a client starts accepting things that mean something else. This
    /// case is safe precisely because <c>null</c> has no valid reading to be confused with — it is the one
    /// value the grammar forbids outright. Anything else still fails, loudly, as before.
    /// </para>
    /// </remarks>
    /// <summary>
    /// <see cref="RepairNullParams"/>, reachable from the tests.
    /// </summary>
    /// <remarks>
    /// Exposed rather than exercised through a live connection on purpose. The failure being guarded lives
    /// inside the reader, and reproducing it end to end would make a passing test depend on a third party
    /// continuing to emit a malformed frame — so the day rumdl fixed its own bug, the guard would go quietly
    /// vacuous while still reporting green.
    /// </remarks>
    internal static byte[]? RepairNullParamsForTests(ReadOnlySequence<byte> contentBuffer) =>
        RepairNullParams(contentBuffer);

    private static byte[]? RepairNullParams(ReadOnlySequence<byte> contentBuffer)
    {
        try
        {
            var reader = new Utf8JsonReader(contentBuffer);
            using var document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!document.RootElement.TryGetProperty("params", out var parameters)) return null;
            if (parameters.ValueKind != JsonValueKind.Null) return null;

            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                foreach (var member in document.RootElement.EnumerateObject())
                {
                    if (member.NameEquals("params")) continue;
                    member.WriteTo(writer);
                }
                writer.WriteEndObject();
            }

            return buffer.WrittenSpan.ToArray();
        }
        catch (Exception)
        {
            // A repair that cannot itself be performed is not a repair. The caller rethrows the real failure.
            return null;
        }
    }

    /// <summary>The method name, when the message has one to give.</summary>
    /// <remarks>
    /// Read off the message rather than the bytes, because on the outbound failure path there are no
    /// bytes — that is the whole point of the entry. A response carries no method of its own and comes
    /// back null, which the row renders as its detail instead.
    /// </remarks>
    private static string? MethodOf(JsonRpcMessage message) =>
        message is JsonRpcRequest { Method: { Length: > 0 } method } ? method : null;

    private void Record(JsonRpcMessage message, ConversationDirection direction, int size, byte[]? body)
    {
        var (kind, method, id) = Describe(message);
        Note(direction, kind, method, id, size, body);
    }

    /// <summary>What sort of message this is, what it is called, and which exchange it belongs to.</summary>
    private static (ConversationEntryKind Kind, string? Method, string? Id) Describe(JsonRpcMessage message) =>
        message switch
        {
            // A notification is a request with nobody waiting, and its id is empty rather than absent —
            // distinguishable without inventing one.
            JsonRpcRequest request => (
                request.IsResponseExpected ? ConversationEntryKind.Request : ConversationEntryKind.Notification,
                request.Method,
                Text(request.RequestId)),

            JsonRpcResult result => (ConversationEntryKind.Response, null, Text(result.RequestId)),
            JsonRpcError error => (ConversationEntryKind.ErrorResponse, null, Text(error.RequestId)),
            _ => (ConversationEntryKind.Note, null, null),
        };

    /// <summary>
    /// The id as text, because the protocol allows a number or a string and a capture must not turn one
    /// into the other. Two servers numbering their requests differently is a real thing to be able to see.
    /// </summary>
    private static string? Text(RequestId id) =>
        id.Number is { } number ? number.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : id.String;

    // ── Everything else, forwarded ───────────────────────────────────────────

    /// <summary>
    /// Forwarded, and obsolete on both sides of the forward.
    /// </summary>
    /// <remarks>
    /// The library deprecated this in favour of the tracing callbacks, and a formatter is entitled to throw
    /// from it. Forwarding rather than reimplementing keeps this wrapper transparent: whatever the inner
    /// formatter would have done, including refusing, is what a caller gets.
    /// </remarks>
    [Obsolete("Deprecated by StreamJsonRpc in favour of the tracing callbacks; forwarded for transparency.")]
    public object GetJsonText(JsonRpcMessage message) => inner.GetJsonText(message);

    public JsonRpcRequest CreateRequestMessage() => _factory.CreateRequestMessage();
    public JsonRpcError CreateErrorMessage() => _factory.CreateErrorMessage();
    public JsonRpcResult CreateResultMessage() => _factory.CreateResultMessage();

    public bool SerializingRequest => _state.SerializingRequest;
    public RequestId SerializingMessageWithId => _state.SerializingMessageWithId;
    public RequestId DeserializingMessageWithId => _state.DeserializingMessageWithId;

    /// <summary>
    /// The forward whose absence is worst.
    /// </summary>
    /// <remarks>
    /// Without it the inner formatter never learns which connection it belongs to, and inbound
    /// notifications are silently never dispatched. Requests and responses continue to work, so the
    /// connection looks entirely healthy while diagnostics stop arriving.
    /// </remarks>
    public JsonRpc Rpc
    {
        set => ((IJsonRpcInstanceContainer)inner).Rpc = value;
    }

    public void OnSerializationComplete(JsonRpcMessage message, ReadOnlySequence<byte> encodedMessage) =>
        (inner as IJsonRpcFormatterTracingCallbacks)?.OnSerializationComplete(message, encodedMessage);

    /// <summary>
    /// Tells the inner formatter it may release what it retained for a message.
    /// </summary>
    /// <remarks>
    /// Not on the measured list of six, and forwarded anyway. This formatter keeps a parsed document alive
    /// so a request's arguments can be deserialized lazily, and this is how it learns that moment has
    /// passed. Swallowing the call would not break a frame; it would leak one per message, which is the
    /// kind of fault that only shows up in a long session and never points at its cause.
    /// </remarks>
    public void DeserializationComplete(JsonRpcMessage message) =>
        (inner as IJsonRpcMessageBufferManager)?.DeserializationComplete(message);

    public void Dispose() => (inner as IDisposable)?.Dispose();

    /// <summary>
    /// Passes writes through to the real destination, and keeps a copy when anybody wants one.
    /// </summary>
    /// <remarks>
    /// Length is accumulated whether or not the connection is armed, because the size of a frame is
    /// metadata rather than content: it is what makes an unarmed record able to say that a reply was two
    /// hundred kilobytes without holding two hundred kilobytes.
    /// </remarks>
    private sealed class TeeWriter(IBufferWriter<byte> destination, bool capture) : IBufferWriter<byte>
    {
        private readonly ArrayBufferWriter<byte>? _copy = capture ? new ArrayBufferWriter<byte>() : null;
        private Memory<byte> _current;

        public int Length { get; private set; }

        public byte[]? Captured => _copy?.WrittenSpan.ToArray();

        public void Advance(int count)
        {
            Length += count;
            _copy?.Write(_current.Span[..count]);
            destination.Advance(count);
        }

        public Memory<byte> GetMemory(int sizeHint = 0) => _current = destination.GetMemory(sizeHint);

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            _current = destination.GetMemory(sizeHint);
            return _current.Span;
        }
    }
}
