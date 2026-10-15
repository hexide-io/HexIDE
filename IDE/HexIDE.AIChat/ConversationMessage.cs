using System.Text.Json.Nodes;

namespace HexIDE.AIChat;

public sealed class ConversationMessage
{
    public string Role { get; }
    public string Content { get; set; }
    public IReadOnlyList<ToolCall>? ToolCalls { get; set; }
    public string? ToolCallId { get; }
    public string? ToolName { get; }

    /// <summary>
    /// True for a note the IDE wrote to the assistant about something the developer did, such as applying a code
    /// block. It goes to the model as a <c>user</c> message, which is the only role that can carry it: a
    /// <c>tool</c> message needs a call to answer, and a <c>system</c> message mid-conversation is refused by some
    /// endpoints. The flag keeps it out of the developer's own bubbles when the transcript is rebuilt.
    /// </summary>
    public bool FromIde { get; }

    public ConversationMessage(string role, string content,
        IReadOnlyList<ToolCall>? toolCalls = null,
        string? toolCallId = null,
        string? toolName = null,
        bool fromIde = false)
    {
        Role = role;
        Content = content;
        ToolCalls = toolCalls;
        ToolCallId = toolCallId;
        ToolName = toolName;
        FromIde = fromIde;
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
