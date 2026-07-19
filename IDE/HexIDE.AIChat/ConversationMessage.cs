using System.Text.Json.Nodes;

namespace HexIDE.AIChat;

public sealed class ConversationMessage
{
    public string Role { get; }
    public string Content { get; set; }
    public IReadOnlyList<ToolCall>? ToolCalls { get; set; }
    public string? ToolCallId { get; }
    public string? ToolName { get; }

    public ConversationMessage(string role, string content,
        IReadOnlyList<ToolCall>? toolCalls = null,
        string? toolCallId = null,
        string? toolName = null)
    {
        Role = role;
        Content = content;
        ToolCalls = toolCalls;
        ToolCallId = toolCallId;
        ToolName = toolName;
    }

    public JsonObject ToApiJson()
    {
        if (Role == "tool")
        {
            return new JsonObject
            {
                ["role"]         = "tool",
                ["tool_call_id"] = ToolCallId,
                ["content"]      = Content
            };
        }

        var obj = new JsonObject { ["role"] = Role };
        if (!string.IsNullOrEmpty(Content)) obj["content"] = Content;

        if (ToolCalls is { Count: > 0 })
        {
            var arr = new JsonArray();
            foreach (var tc in ToolCalls)
            {
                arr.Add(new JsonObject
                {
                    ["id"]   = tc.Id,
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"]      = tc.Name,
                        ["arguments"] = tc.ArgumentsJson
                    }
                });
            }
            obj["tool_calls"] = arr;
        }
        return obj;
    }
}

public sealed class ToolCall(string id, string name, string argumentsJson)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string ArgumentsJson { get; } = argumentsJson;
}
