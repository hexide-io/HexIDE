using System.Buffers.Binary;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace HexIDE.TwinBasic;

public sealed record CompilerRecordingHeader(int FormatVersion, string Beta, string Edition, string Scenario);
public sealed record CompilerRecordedFrame(int Sequence, string Channel, string Direction, string Type, string Payload);

/// <summary>Explicit wire values; integers must use the encoding of the frame being recorded.</summary>
public sealed class CompilerRecordingValues
{
    private sealed record Binding(string Key, byte[] Value, byte[] Marker);
    private readonly Binding[] values;
    public CompilerRecordingValues(IReadOnlyDictionary<string, byte[]> values)
    {
        this.values = values.Select(x => new Binding(x.Key, x.Value.ToArray(), Encoding.UTF8.GetBytes(x.Key)))
            .OrderByDescending(x => x.Value.Length).ToArray();
        // {language} stands for the language data an initialization carries: an install's pack is not ours to keep.
        if (this.values.Any(x => !Regex.IsMatch(x.Key, @"^\{(stage|pid|instance|language|port:(root|fs|language|debugger|formImage|liveBackup))\}$") || x.Value.Length == 0))
            throw new ArgumentException("Named, nonempty recording values are required.", nameof(values));
    }

    internal string SubstituteText(string text, bool recording)
    {
        string WireText(Binding value) =>
            value.Key == "{pid}" || value.Key.StartsWith("{port:", StringComparison.Ordinal)
                ? BinaryPrimitives.ReadInt32LittleEndian(value.Value).ToString(CultureInfo.InvariantCulture)
                : new UTF8Encoding(false, true).GetString(value.Value);

        if (!recording)
        {
            foreach (var value in values)
            {
                // Values inside JSON strings need escaping; integer digits are identical in either context.
                var escaped = JsonSerializer.Serialize(WireText(value));
                text = text.Replace(value.Key, escaped[1..^1], StringComparison.Ordinal);
            }
            return text;
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        var reader = new Utf8JsonReader(bytes);
        using var output = new MemoryStream();
        var offset = 0;
        try
        {
            while (reader.Read())
            {
                string? replacement = null;
                if (reader.TokenType == JsonTokenType.String)
                {
                    var content = reader.GetString()!;
                    foreach (var value in values) content = content.Replace(WireText(value), value.Key, StringComparison.Ordinal);
                    replacement = JsonSerializer.Serialize(content);
                }
                else if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
                {
                    foreach (var value in values)
                    {
                        if ((value.Key == "{pid}" || value.Key.StartsWith("{port:", StringComparison.Ordinal)) &&
                            number == BinaryPrimitives.ReadInt32LittleEndian(value.Value))
                        { replacement = value.Key; break; }
                    }
                }
                if (replacement is null) continue;
                var start = checked((int)reader.TokenStartIndex);
                output.Write(bytes.AsSpan(offset, start - offset));
                output.Write(Encoding.UTF8.GetBytes(replacement));
                offset = checked((int)reader.BytesConsumed);
            }
            output.Write(bytes.AsSpan(offset));
            return Encoding.UTF8.GetString(output.ToArray());
        }
        catch (JsonException) { return Encoding.UTF8.GetString(Substitute(bytes, true)); }
    }

    internal byte[] Substitute(ReadOnlySpan<byte> bytes, bool recording)
    {
        using var output = new MemoryStream();
        for (var offset = 0; offset < bytes.Length;)
        {
            var matched = false;
            foreach (var value in values)
            {
                var marker = value.Marker;
                var from = recording ? value.Value : marker;
                if (!bytes[offset..].StartsWith(from)) continue;
                output.Write(recording ? marker : value.Value);
                offset += from.Length;
                matched = true;
                break;
            }
            if (!matched) output.WriteByte(bytes[offset++]);
        }
        return output.ToArray();
    }
}

/// <summary>One shared recorder orders all channels. The caller owns the writer.</summary>
public sealed class CompilerRecording
{
    private readonly TextWriter writer;
    private readonly CompilerRecordingValues values;
    private readonly object gate = new();
    private int sequence;

    public CompilerRecording(TextWriter writer, CompilerRecordingHeader header, CompilerRecordingValues values)
    {
        if (header.FormatVersion != 1 || string.IsNullOrWhiteSpace(header.Beta) ||
            string.IsNullOrWhiteSpace(header.Edition) || string.IsNullOrWhiteSpace(header.Scenario))
            throw new ArgumentException("A version 1 recording header is required.", nameof(header));
        this.writer = writer;
        this.values = values;
        Write(header);
    }

    public void Record(string channel, string direction, CompilerEncodedMessage message)
    {
        if (channel is not ("root" or "fs" or "language" or "debugger") || direction is not ("send" or "receive") ||
            message.Type is not (WebSocketMessageType.Text or WebSocketMessageType.Binary))
            throw new ArgumentException("Invalid recording frame metadata.");
        lock (gate)
        {
            var payload = message.Type == WebSocketMessageType.Text
                ? values.SubstituteText(new UTF8Encoding(false, true).GetString(message.Bytes.Span), true)
                : Convert.ToBase64String(values.Substitute(message.Bytes.Span, true));
            Write(new CompilerRecordedFrame(++sequence, channel, direction, message.Type.ToString(), payload));
        }
    }

    private void Write<T>(T line)
    {
        var text = JsonSerializer.Serialize(line);
        CompilerRecordingReplay.CheckRedaction(text);
        writer.WriteLine(text);
        writer.Flush();
    }
}

/// <summary>Plays the compiler side over caller-owned loopback transports, in recorded order.</summary>
public static class CompilerRecordingReplay
{
    public static async Task PlayAsync(TextReader reader, IReadOnlyDictionary<string, CompilerWebSocketTransport> peers,
        CompilerRecordingValues values, CancellationToken cancellationToken = default)
    {
        ReadHeader(reader);
        var sequence = 0;
        while (reader.ReadLine() is { } line)
        {
            CheckRedaction(line);
            var frame = JsonSerializer.Deserialize<CompilerRecordedFrame>(line) ?? throw new InvalidDataException("Missing frame.");
            if (frame.Sequence != ++sequence || frame.Direction is not ("send" or "receive") ||
                frame.Type is not ("Text" or "Binary")) throw new InvalidDataException("Invalid recording frame.");
            var type = Enum.Parse<WebSocketMessageType>(frame.Type);
            var bytes = type == WebSocketMessageType.Text
                ? Encoding.UTF8.GetBytes(values.SubstituteText(frame.Payload, false))
                : values.Substitute(Convert.FromBase64String(frame.Payload), false);
            if (Regex.IsMatch(Encoding.UTF8.GetString(bytes), @"\{(?:stage|pid|instance|language|port:[^}]+)\}"))
                throw new InvalidDataException($"Unsubstituted placeholder at frame {sequence}.");
            var peer = peers[frame.Channel];
            if (frame.Direction == "receive") await peer.SendAsync(bytes, type, cancellationToken).ConfigureAwait(false);
            else
            {
                var actual = await peer.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                var expectedText = type == WebSocketMessageType.Text ? Encoding.UTF8.GetString(bytes) : Convert.ToBase64String(bytes);
                var actualText = type == WebSocketMessageType.Text ? Encoding.UTF8.GetString(actual.Bytes) : Convert.ToBase64String(actual.Bytes);
                var equal = bytes.AsSpan().SequenceEqual(actual.Bytes);
                if (type == WebSocketMessageType.Text)
                {
                    try { equal = JsonNode.DeepEquals(JsonNode.Parse(expectedText), JsonNode.Parse(actualText)); }
                    catch (JsonException) { equal = false; }
                }
                if (actual.Type != type || !equal)
                    throw new InvalidDataException($"Frame {sequence} differs. Expected {expectedText}; actual {actualText}.");
            }
        }
    }

    private static CompilerRecordingHeader ReadHeader(TextReader reader)
    {
        var headerLine = reader.ReadLine() ?? "";
        CheckRedaction(headerLine);
        var header = JsonSerializer.Deserialize<CompilerRecordingHeader>(headerLine)
            ?? throw new InvalidDataException("Missing recording header.");
        return header.FormatVersion == 1 ? header : throw new InvalidDataException("Unsupported recording format.");
    }

    internal static (CompilerRecordingHeader Header, List<CompilerRecordedFrame> Frames) Read(TextReader reader)
    {
        var header = ReadHeader(reader);
        var frames = new List<CompilerRecordedFrame>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Trim().Length == 0) continue;
            CheckRedaction(line);
            frames.Add(JsonSerializer.Deserialize<CompilerRecordedFrame>(line) ?? throw new InvalidDataException("Missing frame."));
        }
        return (header, frames);
    }

    public static void CheckRedaction(string line)
    {
        using var document = JsonDocument.Parse(line);
        var text = line;
        if (document.RootElement.TryGetProperty("Payload", out var payload))
            text = document.RootElement.GetProperty("Type").GetString() == "Binary"
                ? Encoding.UTF8.GetString(Convert.FromBase64String(payload.GetString()!)) : payload.GetString()!;
        // Check decoded strings, including binary payloads, rather than just the JSON envelope.
        text = text.Replace("\\\\", "\\", StringComparison.Ordinal);
        if (Regex.IsMatch(text, @"(?i)([a-z]:[\\/](?:users|documents and settings)[\\/]|/(?:home|Users|root)/|~/|[a-z0-9]{5}(?:-[a-z0-9]{5}){4})"))
            throw new InvalidDataException("Recording contains a user path or licence-shaped value.");
    }
}

/// <summary>
/// Compares two recordings of one scenario frame by frame, ignoring which beta and edition their headers name, so a
/// protocol change between betas reads as a list of frames (#1219). Placeholders compare as written, since both sides
/// were redacted when they were recorded.
/// </summary>
public static class CompilerRecordingComparison
{
    private const int Context = 48;

    public static IReadOnlyList<string> Differences(TextReader previous, TextReader current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        var (before, beforeFrames) = CompilerRecordingReplay.Read(previous);
        var (after, afterFrames) = CompilerRecordingReplay.Read(current);
        if (before.Scenario != after.Scenario)
            return [$"The recordings are of different scenarios, {before.Scenario} and {after.Scenario}, so they do not compare."];

        var differences = new List<string>();
        for (var i = 0; i < Math.Max(beforeFrames.Count, afterFrames.Count); i++)
        {
            if (i >= beforeFrames.Count) { differences.Add($"{Describe(i, afterFrames[i])} is only in beta {after.Beta}."); continue; }
            if (i >= afterFrames.Count) { differences.Add($"{Describe(i, beforeFrames[i])} is only in beta {before.Beta}."); continue; }
            var (a, b) = (beforeFrames[i], afterFrames[i]);
            if (a.Channel != b.Channel || a.Direction != b.Direction || a.Type != b.Type)
            {
                differences.Add($"frame {i + 1} is {a.Channel} {a.Direction} {a.Type} in beta {before.Beta}, and {b.Channel} {b.Direction} {b.Type} in beta {after.Beta}.");
                continue;
            }
            var (x, y) = (Bytes(a), Bytes(b));
            var offset = x.AsSpan().CommonPrefixLength(y);
            if (offset == x.Length && offset == y.Length) continue;
            differences.Add($"{Describe(i, a)} differs at offset {offset}: {x.Length} bytes before, {y.Length} now."
                            + $"\n  before (beta {before.Beta}): {Window(x, offset)}\n  now (beta {after.Beta}): {Window(y, offset)}");
        }
        return differences;
    }

    private static string Describe(int index, CompilerRecordedFrame frame) => $"frame {index + 1} ({frame.Channel} {frame.Direction})";

    private static byte[] Bytes(CompilerRecordedFrame frame) =>
        frame.Type == "Binary" ? Convert.FromBase64String(frame.Payload) : Encoding.UTF8.GetBytes(frame.Payload);

    /// <summary>The bytes around an offset, with anything unprintable shown as a dot, as a hex dump's text column has it.</summary>
    private static string Window(byte[] bytes, int offset)
    {
        var start = Math.Max(0, offset - Context);
        var end = Math.Min(bytes.Length, offset + Context);
        var text = new StringBuilder(start > 0 ? "…" : "");
        foreach (var b in bytes.AsSpan(start, end - start)) text.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
        return text.Append(end < bytes.Length ? "…" : "").ToString();
    }
}
