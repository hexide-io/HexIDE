using System.Text.Json;
using System.Text.Json.Serialization;

namespace HexIDE.Lsp;

/// <summary>A legacy command line or an argv array, preserving which form was configured.</summary>
[JsonConverter(typeof(ServerArgumentsConverter))]
public sealed record ServerArguments(string? Text, string[]? List)
{
    public static implicit operator ServerArguments?(string? text) => text is null ? null : new(text, null);

    public override string ToString() => List is null ? Text?.Trim() ?? "" :
        JsonSerializer.Serialize(List, LanguageServerConfigJsonContext.Default.StringArray);
}

public sealed class ServerArgumentsConverter : JsonConverter<ServerArguments>
{
    public override ServerArguments Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String) return new(reader.GetString(), null);
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected a string or string array.");
        var arguments = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.String) throw new JsonException("Expected a string argument.");
            arguments.Add(reader.GetString()!);
        }
        return new(null, arguments.ToArray());
    }

    public override void Write(Utf8JsonWriter writer, ServerArguments value, JsonSerializerOptions options)
    {
        if (value.List is null) writer.WriteStringValue(value.Text);
        else
        {
            writer.WriteStartArray();
            foreach (var argument in value.List) writer.WriteStringValue(argument);
            writer.WriteEndArray();
        }
    }
}
