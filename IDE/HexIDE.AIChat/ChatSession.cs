using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace HexIDE.AIChat;

/// <summary>
/// Holds the full persistent conversation log and builds the API message list
/// with sliding-window compaction.
/// </summary>
public sealed class ChatSession
{
    private readonly List<ConversationMessage> _log = [];
    private readonly List<ConversationMessage> _window = [];
    private string? _compactionSummary;

    public IReadOnlyList<ConversationMessage> Log => _log;

    public void Add(ConversationMessage message)
    {
        _log.Add(message);
        _window.Add(message);
    }

    public bool NeedsCompaction(int threshold) =>
        _window.Count(m => m.Role is "user" or "assistant") >= threshold;

    /// <summary>
    /// Removes the oldest messages from the API window, returning them for summarisation.
    /// The split point is advanced forward past any leading <c>tool</c> messages so the
    /// kept tail never begins with a tool result orphaned from its assistant/tool_calls
    /// parent — which the OpenAI API rejects.
    /// </summary>
    public IReadOnlyList<ConversationMessage> DrainForCompaction(int keepTailCount)
    {
        var boundary = Math.Max(0, _window.Count - keepTailCount);
        while (boundary < _window.Count && _window[boundary].Role == "tool")
            boundary++;

        var toSummarise = _window.Take(boundary).ToList();
        _window.RemoveRange(0, boundary);
        return toSummarise;
    }

    public void SetCompactionSummary(string summary) =>
        _compactionSummary = _compactionSummary is null
            ? summary
            : $"{_compactionSummary}\n\n{summary}";

    public IReadOnlyList<JsonObject> BuildApiMessages(string systemPrompt)
    {
        // Fold any compaction summary into the single system message rather than
        // injecting a synthetic user/assistant turn pair — that keeps the role
        // sequence clean regardless of what role the window starts with.
        var system = _compactionSummary is null
            ? systemPrompt
            : $"{systemPrompt}\n\n[Summary of earlier conversation]\n{_compactionSummary}";

        var messages = new List<JsonObject>
        {
            new() { ["role"] = "system", ["content"] = system }
        };

        messages.AddRange(_window.Select(m => m.ToApiJson()));
        return messages;
    }

    // ── persistence ──────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public void SaveTo(string path)
    {
        try
        {
            var payload = new PersistedSession
            {
                CompactionSummary = _compactionSummary,
                Log = _log.Select(m => new PersistedMessage
                {
                    Role = m.Role,
                    Content = m.Content,
                    ToolCallId = m.ToolCallId,
                    ToolName = m.ToolName,
                    ToolCalls = m.ToolCalls?.Select(tc => new PersistedToolCall
                    {
                        Id = tc.Id,
                        Name = tc.Name,
                        ArgumentsJson = tc.ArgumentsJson,
                    }).ToList(),
                }).ToList(),
            };
            File.WriteAllText(path, JsonSerializer.Serialize(payload, s_json));
        }
        catch { /* best-effort */ }
    }

    public static ChatSession LoadFrom(string path)
    {
        var session = new ChatSession();
        if (!File.Exists(path)) return session;
        try
        {
            var data = JsonSerializer.Deserialize<PersistedSession>(File.ReadAllText(path), s_json);
            if (data is null) return session;

            session._compactionSummary = data.CompactionSummary;

            foreach (var entry in data.Log)
            {
                IReadOnlyList<ToolCall>? toolCalls = entry.ToolCalls?
                    .Select(t => new ToolCall(t.Id, t.Name, t.ArgumentsJson))
                    .ToList();

                var msg = new ConversationMessage(
                    entry.Role, entry.Content, toolCalls, entry.ToolCallId, entry.ToolName);
                session._log.Add(msg);
                session._window.Add(msg);
            }
        }
        catch { /* corrupt history — start fresh */ }
        return session;
    }

    // ── persistence DTOs (symmetric round-trip) ────────────────────────────────

    private sealed class PersistedSession
    {
        [JsonPropertyName("compactionSummary")] public string? CompactionSummary { get; set; }
        [JsonPropertyName("log")] public List<PersistedMessage> Log { get; set; } = [];
    }

    private sealed class PersistedMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "user";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
        [JsonPropertyName("toolCallId")] public string? ToolCallId { get; set; }
        [JsonPropertyName("toolName")] public string? ToolName { get; set; }
        [JsonPropertyName("toolCalls")] public List<PersistedToolCall>? ToolCalls { get; set; }
    }

    private sealed class PersistedToolCall
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("argumentsJson")] public string ArgumentsJson { get; set; } = "";
    }
}
