using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HexIDE.Lsp.Messages;

public record DiagnosticRelatedInformation(
    [property: JsonPropertyName("location")] Location Location,
    [property: JsonPropertyName("message")] string Message);

public sealed class DiagnosticTagsConverter : JsonConverter<int[]>
{
    public override int[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return ReadItems(document.RootElement);
    }

    public static int[] ReadItems(JsonElement element)
    {
        var tags = new List<int>();
        if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var tag) && tag is 1 or 2)
                    tags.Add(tag);
        return [.. tags];
    }

    public override void Write(Utf8JsonWriter writer, int[] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var tag in value) writer.WriteNumberValue(tag);
        writer.WriteEndArray();
    }
}

public sealed class DiagnosticRelatedInformationConverter : JsonConverter<DiagnosticRelatedInformation[]>
{
    public override DiagnosticRelatedInformation[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return ReadItems(document.RootElement);
    }

    public static DiagnosticRelatedInformation[] ReadItems(JsonElement element)
    {
        var entries = new List<DiagnosticRelatedInformation>();
        if (element.ValueKind != JsonValueKind.Array) return [];
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("location", out var location) || location.ValueKind != JsonValueKind.Object
                || !location.TryGetProperty("uri", out var uri) || uri.ValueKind != JsonValueKind.String
                || !location.TryGetProperty("range", out var range) || range.ValueKind != JsonValueKind.Object
                || !range.TryGetProperty("start", out var start) || ReadPosition(start) is not { } from
                || !range.TryGetProperty("end", out var end) || ReadPosition(end) is not { } to
                || !item.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.String)
                continue;
            entries.Add(new(new Location(uri.GetString()!, new Range(from, to)), message.GetString()!));
        }
        return [.. entries];
    }

    private static Position? ReadPosition(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty("line", out var line) && line.ValueKind == JsonValueKind.Number
        && line.TryGetInt32(out var l) && l >= 0
        && element.TryGetProperty("character", out var character) && character.ValueKind == JsonValueKind.Number
        && character.TryGetInt32(out var c) && c >= 0 ? new Position(l, c) : null;

    public override void Write(Utf8JsonWriter writer, DiagnosticRelatedInformation[] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var entry in value)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("location");
            writer.WriteStartObject();
            writer.WriteString("uri", entry.Location.Uri);
            writer.WritePropertyName("range");
            writer.WriteStartObject();
            WritePosition(writer, "start", entry.Location.Range.Start);
            WritePosition(writer, "end", entry.Location.Range.End);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteString("message", entry.Message);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WritePosition(Utf8JsonWriter writer, string name, Position position)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        writer.WriteNumber("line", position.Line);
        writer.WriteNumber("character", position.Character);
        writer.WriteEndObject();
    }
}

public sealed class DiagnosticDataConverter : JsonConverter<JsonElement?>
{
    public override bool HandleNull => true;

    public override JsonElement? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.Clone();
    }

    public override void Write(Utf8JsonWriter writer, JsonElement? value, JsonSerializerOptions options)
    {
        // Preserve the server's token, including number spelling, escapes and internal whitespace.
        if (value is { } data) writer.WriteRawValue(data.GetRawText());
        else writer.WriteNullValue();
    }
}
