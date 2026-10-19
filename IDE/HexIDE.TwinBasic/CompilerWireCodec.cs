using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace HexIDE.TwinBasic;

/// <summary>Limits apply to one complete WebSocket message, after transport reassembly.</summary>
public sealed record CompilerWireLimits(int MaximumMessageBytes = 16 * 1024 * 1024,
    int MaximumJsonObjects = 4096, int MaximumJsonDepth = 64);

/// <summary>Control reply, or notification when RequestId is -1. Code and payload remain uninterpreted.</summary>
public sealed record CompilerControlMessage(int RequestId, int Code, ImmutableArray<byte> Payload)
{
    public bool IsNotification => RequestId == -1;
}

/// <summary>
/// The observed control and JSON codecs, not a compiler session or a stock LSP/DAP transport.
/// Callers must bound WebSocket reassembly separately. Future encodings require a separate codec.
/// </summary>
public sealed class CompilerWireCodec
{
    private const uint JsonBatchMarker = 0xffeaeaea;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly CompilerWireLimits limits;

    public CompilerWireCodec(CompilerWireLimits? limits = null)
    {
        this.limits = limits ?? new();
        if (this.limits.MaximumMessageBytes < 8 || this.limits.MaximumJsonObjects < 1 || this.limits.MaximumJsonDepth < 1)
            throw new ArgumentOutOfRangeException(nameof(limits), "Message, object and depth limits must be positive; a control header needs eight bytes.");
    }

    /// <summary>Encode a control request: byte command, int32 request id, then caller-supplied opaque payload.</summary>
    public byte[] EncodeControlRequest(byte command, int requestId, ReadOnlySpan<byte> payload)
    {
        if (requestId < 0) throw new ArgumentOutOfRangeException(nameof(requestId));
        if (payload.Length > limits.MaximumMessageBytes - 5) throw Invalid("Control request exceeds the byte limit");
        var result = new byte[5 + payload.Length];
        result[0] = command;
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(1), requestId);
        payload.CopyTo(result.AsSpan(5));
        return result;
    }

    /// <summary>Decode an inbound control message: int32 request id, int32 status/event code, opaque payload.</summary>
    public CompilerControlMessage DecodeControl(ReadOnlySpan<byte> message)
    {
        CheckSize(message.Length);
        if (message.Length < 8) throw Invalid("Truncated control header");
        var requestId = BinaryPrimitives.ReadInt32LittleEndian(message);
        if (requestId < -1) throw Invalid("Invalid control request id");
        return new(requestId, BinaryPrimitives.ReadInt32LittleEndian(message[4..]),
            ImmutableArray.CreateRange(message[8..].ToArray()));
    }

    /// <summary>
    /// Decode a UTF-8 JSON object, or a binary batch: marker followed by repeated int32 byte lengths and JSON objects.
    /// Returned elements own their storage and preserve the compiler's nonstandard fields.
    /// </summary>
    public ImmutableArray<JsonElement> DecodeJson(ReadOnlySpan<byte> message)
    {
        CheckSize(message.Length);
        if (message.Length < 4 || BinaryPrimitives.ReadUInt32LittleEndian(message) != JsonBatchMarker)
            return [ParseObject(message)];

        var objects = ImmutableArray.CreateBuilder<JsonElement>();
        var offset = 4;
        while (offset < message.Length)
        {
            if (objects.Count == limits.MaximumJsonObjects) throw Invalid("JSON batch exceeds the object limit");
            if (message.Length - offset < 4) throw Invalid("Truncated JSON batch length");
            var length = BinaryPrimitives.ReadInt32LittleEndian(message[offset..]);
            offset += 4;
            if (length <= 0 || length > message.Length - offset) throw Invalid("Invalid JSON batch byte length");
            objects.Add(ParseObject(message.Slice(offset, length)));
            offset += length;
        }
        if (objects.Count == 0) throw Invalid("Empty JSON batch");
        return objects.ToImmutable();
    }

    /// <summary>Encode an ordinary JSON object. Special document-change envelopes are outside this codec.</summary>
    public byte[] EncodeJson(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object) throw Invalid("Compiler JSON message must be an object");
        var text = message.GetRawText();
        CheckSize(StrictUtf8.GetByteCount(text));
        var bytes = StrictUtf8.GetBytes(text);
        _ = ParseObject(bytes); // Apply the same depth and UTF-8 contract in both directions.
        return bytes;
    }

    private JsonElement ParseObject(ReadOnlySpan<byte> bytes)
    {
        try
        {
            _ = StrictUtf8.GetCharCount(bytes);
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = limits.MaximumJsonDepth });
            using var document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.ValueKind != JsonValueKind.Object || reader.Read())
                throw Invalid("Compiler JSON message must contain exactly one object");
            return document.RootElement.Clone();
        }
        catch (JsonException ex) { throw new InvalidDataException("Malformed compiler JSON message.", ex); }
        catch (DecoderFallbackException ex) { throw new InvalidDataException("Compiler JSON is not valid UTF-8.", ex); }
    }

    private void CheckSize(int length)
    {
        if (length == 0 || length > limits.MaximumMessageBytes) throw Invalid("Compiler message is empty or exceeds the byte limit");
    }

    private static InvalidDataException Invalid(string message) => new(message + ".");
}
