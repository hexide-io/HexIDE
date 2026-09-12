using System.Buffers;
using System.Text;
using HexIDE.Conversations;
using StreamJsonRpc;
using StreamJsonRpc.Protocol;
using StreamJsonRpc.Reflection;

namespace HexIDE.Lsp;

/// <summary>
/// A message formatter that copies what crosses it into a <see cref="ConversationLog"/>, and otherwise
/// does nothing at all.
/// </summary>
/// <remarks>
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
    ConversationLog log,
    string connectionId)
    : IJsonRpcMessageTextFormatter, IJsonRpcMessageFactory, IJsonRpcInstanceContainer, IJsonRpcFormatterState,
      IJsonRpcFormatterTracingCallbacks, IJsonRpcMessageBufferManager, IDisposable
{
    private readonly IJsonRpcMessageFactory _factory = (IJsonRpcMessageFactory)inner;
    private readonly IJsonRpcFormatterState _state = (IJsonRpcFormatterState)inner;

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
        var tee = new TeeWriter(bufferWriter, log.ShouldKeepBody(connectionId));

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
            try
            {
                log.Record(
                    connectionId, ConversationDirection.Local, ConversationEntryKind.NeverSent,
                    MethodOf(message), null, 0, null, $"could not be serialized: {ex.Message}");
            }
            catch (Exception) { /* never replace the caller's exception with a diagnostic's */ }

            throw;
        }

        // Everything below is best-effort and swallows. A throw here is survivable — measured, the one
        // call fails and the connection lives — but "survivable" is not a reason to spend the connection's
        // luck on a diagnostic.
        try { Record(message, ConversationDirection.Sent, tee.Length, tee.Captured); }
        catch (Exception) { /* a capture is never worth breaking anything for */ }
    }

    public JsonRpcMessage Deserialize(ReadOnlySequence<byte> contentBuffer) =>
        Capture(contentBuffer, () => inner.Deserialize(contentBuffer));

    public JsonRpcMessage Deserialize(ReadOnlySequence<byte> contentBuffer, Encoding encoding) =>
        Capture(contentBuffer, () => inner.Deserialize(contentBuffer, encoding));

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
    private JsonRpcMessage Capture(ReadOnlySequence<byte> contentBuffer, Func<JsonRpcMessage> deserialize)
    {
        var length = (int)contentBuffer.Length;
        byte[]? copy = null;
        try
        {
            if (log.ShouldKeepBody(connectionId)) copy = contentBuffer.ToArray();
        }
        catch (Exception) { copy = null; }

        JsonRpcMessage message;
        try
        {
            message = deserialize();
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
            try
            {
                log.Record(
                    connectionId, ConversationDirection.Received, ConversationEntryKind.Note,
                    null, null, length, copy, $"undecodable frame: {ex.Message}");
            }
            catch (Exception) { /* the throw below is the caller's answer; this must not replace it */ }

            throw;
        }

        try { Record(message, ConversationDirection.Received, length, copy); }
        catch (Exception) { /* as above, and more so */ }

        return message;
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
        log.Record(connectionId, direction, kind, method, id, size, body);
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
