using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace HexIDE.TwinBasic;

public sealed partial class CompilerWireCodec
{
    // Ongoing channels are intentionally opaque until their dispatchers exist (#1217).
    private sealed record OpaqueChannelData(WebSocketMessageType Type, ReadOnlyMemory<byte> Bytes) : CompilerChannelData;
    public CompilerChannelData DecodeChannel(WebSocketMessageType type, ReadOnlyMemory<byte> bytes) => new OpaqueChannelData(type, bytes);
    public CompilerEncodedMessage EncodeChannel(CompilerChannelData message)
    {
        var opaque = (OpaqueChannelData)message;
        if (opaque.Type is not (WebSocketMessageType.Binary or WebSocketMessageType.Text)) throw new ArgumentOutOfRangeException("type");
        return new(opaque.Type, opaque.Bytes);
    }

    private sealed record InitializationPayload(byte[] Bytes, int MaximumMessageBytes) : CompilerInitializationRequest;
    private sealed record JsonLanguageData(JsonElement Value) : CompilerLanguageData;

    public static CompilerLanguageData ReadLanguageData(JsonElement value) => new JsonLanguageData(value.Clone());

    public CompilerInitializationRequest PrepareInitialization(CompilerRootInitialization initialization, int maximumMessageBytes)
    {
        ArgumentNullException.ThrowIfNull(initialization);
        ArgumentNullException.ThrowIfNull(initialization.PackagePaths);
        if (initialization.LanguageData is not JsonLanguageData languageData ||
            languageData.Value.ValueKind != JsonValueKind.Object || !languageData.Value.EnumerateObject().Any())
            throw new ArgumentException("Compiler initialization requires a nonempty language-data object.", nameof(initialization));

        // Size before allocation; '?' separates entries in the observed package-list representation.
        long packageBytes = 0;
        var paths = new List<string>();
        foreach (var path in initialization.PackagePaths)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Contains('?') || path.Contains('\0'))
                throw new ArgumentException("Package entries must be nonempty and contain no delimiter or NUL.", nameof(initialization));
            packageBytes += StrictUtf8.GetByteCount(path) + 1L;
            if (packageBytes > maximumMessageBytes) throw new InvalidDataException("Compiler package list exceeds the byte limit.");
            paths.Add(path);
        }
        var language = languageData.Value.GetRawText();
        var total = 12L + packageBytes + StrictUtf8.GetByteCount(language);
        if (total > maximumMessageBytes - 5) throw new InvalidDataException("Compiler initialization exceeds the byte limit.");
        var payload = new byte[(int)total];
        // flags = 0, reserved = 0, byte length of first string, package list, language JSON.
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8), (int)packageBytes);
        var offset = 12;
        foreach (var path in paths)
        {
            offset += StrictUtf8.GetBytes(path, payload.AsSpan(offset));
            payload[offset++] = (byte)'?';
        }
        StrictUtf8.GetBytes(language, payload.AsSpan(offset));
        return new InitializationPayload(payload, maximumMessageBytes);
    }

    public byte[] EncodePreinitialize() => EncodeControlRequest(22, 0, [96, 0, 0, 0, .. "0,1,10000,0,"u8]);
    public byte[] EncodeInitialize(CompilerInitializationRequest initialization)
    {
        var payload = (InitializationPayload)initialization;
        return new CompilerWireCodec(new(MaximumMessageBytes: payload.MaximumMessageBytes))
            .EncodeControlRequest(0, 1, payload.Bytes);
    }

    public CompilerControlMessage DecodeControl(ReadOnlySpan<byte> message, int maximumMessageBytes) =>
        new CompilerWireCodec(new(MaximumMessageBytes: maximumMessageBytes)).DecodeControl(message);

    public CompilerProcessInfo ReadProcessInfo(CompilerControlMessage message)
    {
        if (message.Payload.Length != 4) throw new InvalidDataException("Invalid compiler process-id reply.");
        var processId = BinaryPrimitives.ReadInt32LittleEndian(message.Payload.AsSpan());
        if (processId <= 0) throw new InvalidDataException("Invalid compiler process id.");

        return new(processId);
    }

    public CompilerChannelMetadata ReadChannelMetadata(CompilerControlMessage message)
    {
        if (message.Payload.Length <= 20) throw new InvalidDataException("Incomplete compiler channel metadata.");
        var ports = new int[5];
        for (var index = 0; index < ports.Length; index++)
        {
            ports[index] = BinaryPrimitives.ReadInt32LittleEndian(message.Payload.AsSpan(index * 4, 4));
            if (ports[index] is < 1 or > 65535) throw new InvalidDataException("Invalid compiler channel port.");
        }
        string serverId;
        try { serverId = StrictUtf8.GetString(message.Payload.AsSpan()[20..]); }
        catch (DecoderFallbackException) { throw new InvalidDataException("Invalid UTF-8 compiler server identity."); }
        if (string.IsNullOrWhiteSpace(serverId) || serverId.Contains('\0')) throw new InvalidDataException("Invalid compiler server identity.");
        return new(ports[0], ports[1], ports[2], ports[3], ports[4], serverId);
    }

    public CompilerSettingsRequest ReadSettingsRequest(CompilerControlMessage reply)
    {
        if (reply.Payload.Length < 6) throw new InvalidDataException("Truncated compiler settings request.");
        try
        {
            using var settings = JsonDocument.Parse(reply.Payload.AsMemory()[4..], new() { MaxDepth = 64 });
            if (settings.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Compiler settings must be a JSON object.");
        }
        catch (JsonException) { throw new InvalidDataException("Invalid compiler settings JSON."); }
        return new();
    }

    public CompilerPackageRequest ReadPackageRequest(CompilerControlMessage reply)
    {
        if (reply.Payload.Length is < 1 or > 4096) throw new InvalidDataException("Invalid compiler package identifier length.");
        string id;
        try { id = StrictUtf8.GetString(reply.Payload.AsSpan()); }
        catch (DecoderFallbackException) { throw new InvalidDataException("Invalid UTF-8 compiler package identifier."); }
        if (string.IsNullOrWhiteSpace(id) || id.Contains('\0')) throw new InvalidDataException("Invalid compiler package identifier.");
        return new(id);
    }

    public long MaximumPackageContentBytes(CompilerPackageRequest package, int maximumMessageBytes) =>
        (maximumMessageBytes - 9L - StrictUtf8.GetByteCount(package.Identifier)) / 4 * 3;

    public byte[] EncodeProjectRequest(int requestId, CompilerProjectRequest request, int maximumMessageBytes)
    {
        byte command;
        byte[] payload;
        switch (request)
        {
            case CompilerLoadProject load:
                command = 1;
                payload = StrictUtf8.GetBytes(load.Path);
                break;
            case CompilerProjectsLoaded:
                command = 3;
                payload = [];
                break;
            case CompilerKeepSettings:
                command = 24;
                payload = new byte[4];
                break;
            case CompilerProvidePackage provide:
                command = 4;
                var id = StrictUtf8.GetBytes(provide.Package.Identifier);
                var encoded = Convert.ToBase64String(provide.Content.Span);
                payload = new byte[4 + id.Length + encoded.Length];
                BinaryPrimitives.WriteInt32LittleEndian(payload, id.Length);
                id.CopyTo(payload, 4);
                Encoding.ASCII.GetBytes(encoded, payload.AsSpan(4 + id.Length));
                break;
            default: throw new ArgumentOutOfRangeException(nameof(request));
        }
        return new CompilerWireCodec(new(MaximumMessageBytes: maximumMessageBytes))
            .EncodeControlRequest(command, requestId, payload);
    }
}
