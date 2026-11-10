using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HexIDE.AIChat;

/// <summary>Native Messages API transport; the session keeps its provider-neutral message format.</summary>
internal sealed class AnthropicClient(HttpClient http)
{
    public async IAsyncEnumerable<StreamChunk> StreamAsync(
        string baseUrl, string apiKey, string modelId, IReadOnlyList<JsonObject> messages,
        JsonArray tools, ChatSettings settings, [EnumeratorCancellation] CancellationToken ct = default)
    {
        OpenAiClient.RequireSecureEndpoint(baseUrl);
        var body = BuildRequest(modelId, messages, tools, settings);
        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/messages")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Add("anthropic-version", "2026-10-21");
        if (!string.IsNullOrWhiteSpace(settings.WorkspaceId))
            request.Headers.Add("anthropic-workspace-id", settings.WorkspaceId);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            string? message = null;
            try { message = JsonNode.Parse(errorBody)?["error"]?["message"]?.GetValue<string>(); }
            catch (JsonException) { /* Non-JSON responses retain the HTTP status diagnostic. */ }
            if (!string.IsNullOrEmpty(message))
                throw new HttpRequestException(message, null, response.StatusCode);
            response.EnsureSuccessStatusCode();
        }
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        var blocks = new SortedDictionary<int, JsonObject>();
        var inputs = new Dictionary<int, StringBuilder>();
        var data = new StringBuilder();
        string? stopReason = null;
        string? stopExplanation = null;

        while (true)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) throw new EndOfStreamException();
            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0) data.Append('\n');
                data.Append(line[5..].TrimStart(' '));
                continue;
            }
            if (line.Length != 0 || data.Length == 0) continue;
            var frame = JsonNode.Parse(data.ToString())!.AsObject();
            data.Clear();
            switch (frame["type"]?.GetValue<string>())
            {
                case "error":
                    // Provider diagnostics are external text, just as HTTP failures are on the other transport.
                    throw new HttpRequestException(frame["error"]?.ToJsonString());
                case "content_block_start":
                {
                    var index = frame["index"]!.GetValue<int>();
                    var block = frame["content_block"]!.AsObject();
                    blocks.Add(index, (JsonObject)block.DeepClone());
                    if (block["type"]!.GetValue<string>() == "tool_use") inputs.Add(index, new StringBuilder());
                    if (block["type"]!.GetValue<string>() == "text" && block["text"]?.GetValue<string>() is { Length: > 0 } text)
                        yield return new StreamChunk { ContentDelta = text };
                    break;
                }
                case "content_block_delta":
                {
                    var index = frame["index"]!.GetValue<int>();
                    var delta = frame["delta"]!.AsObject();
                    var block = blocks[index];
                    switch (delta["type"]?.GetValue<string>())
                    {
                        case "text_delta":
                            var text = delta["text"]!.GetValue<string>();
                            block["text"] = (block["text"]?.GetValue<string>() ?? "") + text;
                            yield return new StreamChunk { ContentDelta = text };
                            break;
                        case "input_json_delta":
                            inputs[index].Append(delta["partial_json"]!.GetValue<string>());
                            break;
                        case "thinking_delta":
                            block["thinking"] = (block["thinking"]?.GetValue<string>() ?? "") + delta["thinking"]!.GetValue<string>();
                            break;
                        case "signature_delta":
                            block["signature"] = (block["signature"]?.GetValue<string>() ?? "") + delta["signature"]!.GetValue<string>();
                            break;
                    }
                    break;
                }
                case "content_block_stop":
                {
                    var index = frame["index"]!.GetValue<int>();
                    if (inputs.TryGetValue(index, out var input) && input.Length > 0)
                        blocks[index]["input"] = JsonNode.Parse(input.ToString());
                    break;
                }
                case "message_delta":
                    stopReason = frame["delta"]?["stop_reason"]?.GetValue<string>() ?? stopReason;
                    stopExplanation = frame["delta"]?["stop_details"]?["explanation"]?.GetValue<string>() ?? stopExplanation;
                    break;
                case "message_stop":
                    if (stopReason == "refusal") throw new ChatStopException("Str.AIChat.Refusal", stopExplanation);
                    if (stopReason == "max_tokens") throw new ChatStopException("Str.AIChat.TokenLimit");
                    // Do not execute a partially generated tool call after a token limit or stream failure.
                    if (stopReason == "tool_use")
                        yield return new StreamChunk
                        {
                            FinishedToolCalls = blocks.Values.Where(b => b["type"]!.GetValue<string>() == "tool_use")
                                .Select(b => new ToolCall(b["id"]!.GetValue<string>(), b["name"]!.GetValue<string>(),
                                    b["input"]!.ToJsonString())).ToList()
                        };
                    yield return new StreamChunk
                    {
                        IsDone = true,
                        NativeContent = new JsonArray(blocks.Values.Select(b => (JsonNode)b.DeepClone()).ToArray())
                    };
                    yield break;
            }
        }
    }

    private static JsonObject BuildRequest(string modelId, IReadOnlyList<JsonObject> messages,
        JsonArray tools, ChatSettings settings)
    {
        var turns = new JsonArray();
        var system = new JsonArray();
        foreach (var message in messages)
        {
            var role = message["role"]!.GetValue<string>();
            if (role == "system")
            {
                system.Add(new JsonObject { ["type"] = "text", ["text"] = message["content"]!.DeepClone() });
                continue;
            }
            var content = new JsonArray();
            if (role == "tool")
            {
                content.Add(new JsonObject
                {
                    ["type"] = "tool_result", ["tool_use_id"] = message["tool_call_id"]!.DeepClone(),
                    ["content"] = message["content"]!.DeepClone()
                });
                role = "user";
            }
            else if (message["anthropic_content"] is JsonArray native)
                content = (JsonArray)native.DeepClone();
            else
            {
                if (message["content"]?.GetValue<string>() is { Length: > 0 } text)
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = text });
                if (message["tool_calls"] is JsonArray calls)
                    foreach (var call in calls)
                        content.Add(new JsonObject
                        {
                            ["type"] = "tool_use", ["id"] = call!["id"]!.DeepClone(),
                            ["name"] = call["function"]!["name"]!.DeepClone(),
                            ["input"] = JsonNode.Parse(call["function"]!["arguments"]!.GetValue<string>() is { Length: > 0 } arguments ? arguments : "{}")
                        });
            }
            // Parallel tool results must share the user turn immediately following their assistant turn.
            if (turns.LastOrDefault() is JsonObject last && last["role"]!.GetValue<string>() == role)
                foreach (var block in content) last["content"]!.AsArray().Add(block!.DeepClone());
            else
                turns.Add(new JsonObject { ["role"] = role, ["content"] = content });
        }
        var body = new JsonObject
        {
            ["model"] = modelId, ["stream"] = true, ["max_tokens"] = settings.MaxTokens, ["messages"] = turns
        };
        if (system.Count > 0) body["system"] = system;
        if (tools.Count > 0)
            body["tools"] = new JsonArray(tools.Select(t => (JsonNode)new JsonObject
            {
                ["name"] = t!["function"]!["name"]!.DeepClone(),
                ["description"] = t["function"]!["description"]?.DeepClone(),
                ["input_schema"] = t["function"]!["parameters"]!.DeepClone()
            }).ToArray());
        if (settings.Thinking is not null) body["thinking"] = settings.Thinking.DeepClone();
        if (settings.Effort is not null) body["output_config"] = new JsonObject { ["effort"] = settings.Effort };
        return body;
    }
}
