using System.Net.Http;
using System.Text.Json.Nodes;

namespace HexIDE.AIChat;

/// <summary>Selects the wire protocol without changing the conversation or credential source.</summary>
internal sealed class ChatClient : IDisposable
{
    private readonly OpenAiClient _openAi;
    private readonly AnthropicClient _anthropic;

    public ChatClient() : this(new HttpClient()) { }

    internal ChatClient(HttpClient http)
    {
        _openAi = new OpenAiClient(http);
        _anthropic = new AnthropicClient(http);
    }

    public void Dispose() => _openAi.Dispose();

    public IAsyncEnumerable<StreamChunk> StreamAsync(
        string baseUrl, string apiKey, string modelId, IReadOnlyList<JsonObject> messages,
        JsonArray tools, CancellationToken ct = default, ChatSettings? settings = null) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
        && uri.Host.Equals("api.anthropic.com", StringComparison.OrdinalIgnoreCase)
            ? _anthropic.StreamAsync(baseUrl, apiKey, modelId, messages, tools, settings ?? new ChatSettings(), ct)
            : _openAi.StreamAsync(baseUrl, apiKey, modelId, messages, tools, ct);
}
