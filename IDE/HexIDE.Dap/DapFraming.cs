using System.Globalization;
using System.Text;
using System.Text.Json;

namespace HexIDE.Dap;

/// <summary>DAP byte-stream framing. DAP envelopes are not JSON-RPC messages.</summary>
internal sealed class DapFraming(Stream input, DapConnectionOptions options)
{
    private readonly byte[] _buffer = new byte[4096];
    private int _position;
    private int _count;

    private async ValueTask<int> ReadByteAsync(CancellationToken token)
    {
        if (_position == _count)
        {
            _count = await input.ReadAsync(_buffer, token).ConfigureAwait(false);
            _position = 0;
            if (_count == 0) return -1;
        }
        return _buffer[_position++];
    }

    public async Task<JsonElement> ReadAsync(CancellationToken token)
    {
        var header = new List<byte>();
        while (true)
        {
            var value = await ReadByteAsync(token).ConfigureAwait(false);
            if (value < 0)
                throw new EndOfStreamException(header.Count == 0
                    ? "Debug adapter closed its output."
                    : "Debug adapter closed its output inside a DAP header.");
            if (value > 127) throw new InvalidDataException("DAP headers must be ASCII.");
            header.Add((byte)value);
            if (header.Count > options.MaximumHeaderBytes)
                throw new InvalidDataException("DAP header exceeds the configured limit.");
            if (header.Count >= 4 && header[^4] == 13 && header[^3] == 10 && header[^2] == 13 && header[^1] == 10)
                break;
        }

        int? length = null;
        foreach (var line in Encoding.ASCII.GetString(header.ToArray()).Split("\r\n", StringSplitOptions.None)[..^2])
        {
            var colon = line.IndexOf(':');
            if (colon <= 0 || line.Contains('\r') || line.Contains('\n'))
                throw new InvalidDataException("Malformed DAP header.");
            if (!line[..colon].Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
            if (length is not null || !int.TryParse(line[(colon + 1)..].Trim(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var parsed) || parsed <= 0 || parsed > options.MaximumMessageBytes)
                throw new InvalidDataException("DAP Content-Length is duplicate, invalid, or exceeds the configured limit.");
            length = parsed;
        }
        if (length is null) throw new InvalidDataException("DAP Content-Length is missing.");

        var bytes = new byte[length.Value];
        var copied = Math.Min(_count - _position, bytes.Length);
        _buffer.AsSpan(_position, copied).CopyTo(bytes);
        _position += copied;
        try
        {
            await input.ReadExactlyAsync(bytes.AsMemory(copied), token).ConfigureAwait(false);
            using var json = JsonDocument.Parse(bytes);
            if (json.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("A DAP message must be a JSON object.");
            return json.RootElement.Clone();
        }
        catch (JsonException ex) { throw new InvalidDataException("Invalid DAP JSON message.", ex); }
    }

    public static byte[] Serialize(Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) write(writer);
        return buffer.ToArray();
    }

    public static async Task WriteAsync(Stream output, byte[] body, CancellationToken token)
    {
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length.ToString(CultureInfo.InvariantCulture)}\r\n\r\n");
        await output.WriteAsync(header, token).ConfigureAwait(false);
        await output.WriteAsync(body, token).ConfigureAwait(false);
        await output.FlushAsync(token).ConfigureAwait(false);
    }
}
