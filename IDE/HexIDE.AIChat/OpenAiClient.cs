using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HexIDE.AIChat;

public sealed class StreamChunk
{
    public string? ContentDelta { get; init; }
    public IReadOnlyList<ToolCall>? FinishedToolCalls { get; init; }
    public bool IsDone { get; init; }
}

/// <summary>Thin OpenAI-compatible streaming client. No SDK dependencies.</summary>
public sealed class OpenAiClient : IDisposable
{
    private readonly HttpClient _http = new();

    public void Dispose() => _http.Dispose();

    public async IAsyncEnumerable<StreamChunk> StreamAsync(
        string baseUrl,
        string apiKey,
        string modelId,
        IReadOnlyList<JsonObject> messages,
        JsonArray tools,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        RequireSecureEndpoint(baseUrl);

        var url = baseUrl.TrimEnd('/') + "/chat/completions";

        var body = new JsonObject
        {
            ["model"]    = modelId,
            ["stream"]   = true,
            ["messages"] = JsonNode.Parse(JsonSerializer.Serialize(messages)),
        };
        if (tools.Count > 0)
            body["tools"] = JsonNode.Parse(tools.ToJsonString());

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new System.IO.StreamReader(stream);

        // Accumulate tool call chunks by index
        var toolAccumulators = new Dictionary<int, ToolCallAccumulator>();

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

            var data = line[6..].Trim();
            if (data == "[DONE]")
            {
                if (toolAccumulators.Count > 0)
                {
                    var calls = toolAccumulators.OrderBy(kv => kv.Key)
                        .Select(kv => kv.Value.Build())
                        .ToList();
                    yield return new StreamChunk { FinishedToolCalls = calls };
                }
                yield return new StreamChunk { IsDone = true };
                yield break;
            }

            JsonElement chunk;
            try { chunk = JsonDocument.Parse(data).RootElement; }
            catch { continue; }

            if (!chunk.TryGetProperty("choices", out var choices) ||
                choices.GetArrayLength() == 0) continue;

            var delta = choices[0].GetProperty("delta");

            if (delta.TryGetProperty("content", out var contentProp) &&
                contentProp.ValueKind == JsonValueKind.String)
            {
                var text = contentProp.GetString();
                if (!string.IsNullOrEmpty(text))
                    yield return new StreamChunk { ContentDelta = text };
            }

            if (delta.TryGetProperty("tool_calls", out var tcArr))
            {
                foreach (var tc in tcArr.EnumerateArray())
                {
                    var idx = tc.GetProperty("index").GetInt32();
                    if (!toolAccumulators.TryGetValue(idx, out var acc))
                    {
                        acc = new ToolCallAccumulator();
                        toolAccumulators[idx] = acc;
                    }
                    if (tc.TryGetProperty("id", out var idProp))
                        acc.Id = idProp.GetString() ?? acc.Id;
                    if (tc.TryGetProperty("function", out var fnProp))
                    {
                        if (fnProp.TryGetProperty("name", out var nameProp))
                            acc.Name = nameProp.GetString() ?? acc.Name;
                        if (fnProp.TryGetProperty("arguments", out var argsProp))
                            acc.Arguments += argsProp.GetString();
                    }
                }
            }
        }
    }

    /// <summary>
    /// Refuses to transmit the API key and source code over a cleartext connection. Plain <c>http</c> is
    /// permitted only to a loopback host (a local model server such as Ollama or LM Studio); every remote
    /// endpoint must be <c>https</c>.
    /// </summary>
    private static void RequireSecureEndpoint(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"AI Chat base URL is not a valid absolute URL: '{baseUrl}'.");

        var secure = uri.Scheme == Uri.UriSchemeHttps
                     || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
        if (!secure)
            throw new InvalidOperationException(
                "AI Chat won't send your API key and code over an insecure connection. " +
                "Use an https:// endpoint (or http://localhost for a local model server).");
    }

    private sealed class ToolCallAccumulator
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public ToolCall Build() => new(Id, Name, Arguments);
    }
}
