using System.Buffers;
using System.Text.Json;

namespace HexIDE.Lsp;

/// <summary>A validated import command. Substitution visits values, preserving JSON types and property names.</summary>
public sealed record ProjectImportSpec(string Command, JsonElement Arguments)
{
    public JsonElement[] ExpandArguments(string uri, string text)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) Write(writer, Arguments, uri, text);
        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.EnumerateArray().Select(value => value.Clone()).ToArray();
    }

    private static void Write(Utf8JsonWriter writer, JsonElement value, string uri, string text)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value, uri, text);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) Write(writer, item, uri, text);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                // Exact tokens only: substituted project text is never interpreted as another template.
                writer.WriteStringValue(value.GetString() switch
                {
                    "{uri}" => uri,
                    "{text}" => text,
                    var literal => literal,
                });
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}
