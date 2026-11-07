using System.Text.Json;
using System.Text.Json.Serialization;

namespace HexIDE.Lsp;

/// <summary>A server command and its JSON argument template, independent of any particular backend.</summary>
public sealed class ProjectImportEntry
{
    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("arguments")]
    public JsonElement? Arguments { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unrecognized { get; set; }
}
