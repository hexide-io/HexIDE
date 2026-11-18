using System.Text;
using System.Text.Json;
using HexIDE.Addins;
using Microsoft.Extensions.Logging;

namespace HexIDE.Lsp;

/// <summary>A running server AI Chat may take tools from, with the methods its own capability names (#4257).</summary>
/// <param name="Request">Sends one of those methods to this server, and only to it.</param>
internal sealed record ChatToolsServer(
    string Id,
    string DisplayName,
    ServerChatToolsPolicy Policy,
    ServerChatToolsCapability Capability,
    Func<string, JsonElement?, TimeSpan?, CancellationToken, Task<JsonElement?>> Request);

/// <summary>
/// AI Chat's tools and context from language servers, over the connections the registry holds (#4257).
/// </summary>
/// <remarks>
/// <para>
/// <b>The wire shape follows MCP's,</b> as RDCore's catalogue does. The list method takes <c>{}</c> and answers
/// <c>{"tools": [{"name", "description", "inputSchema", "annotations": {"readOnlyHint", "destructiveHint"}}]}</c>. A
/// tool that runs code says so with <c>"hexide.runsCode": true</c> among its annotations. The call method takes
/// <c>{"name", "arguments"}</c> and answers <c>{"content": [{"type": "text", "text"}], "isError"}</c>. The context method
/// takes <c>{}</c> and answers a string, or <c>{"text"}</c>.
/// </para>
/// <para>
/// <b>Every request goes only to the server whose capability named the method,</b> found again by its id on each call,
/// so a tool from a server that has since stopped or stopped declaring the capability is not called.
/// </para>
/// <para>
/// <b>Approval is decided here.</b> A call lists that server's tools again and reads the tool's hints and the server's
/// policy from that answer, not from anything the caller passes, and refuses a name the server no longer lists. Add-ins are
/// consented code in HexIDE's own process, so this is a contract that keeps the decision in one place rather than a
/// sandbox: an add-in that wanted to skip it could reach the server by other means.
/// </para>
/// </remarks>
internal sealed class ServerChatToolsService(Func<IReadOnlyList<ChatToolsServer>> servers, ILogger logger) : IServerChatTools
{
    internal const string RunsCodeAnnotation = "hexide.runsCode";

    internal static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan ContextTimeout = TimeSpan.FromSeconds(10);

    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();
    private static readonly JsonElement EmptySchema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement.Clone();

    public async Task<IReadOnlyList<ServerChatTool>> ListAsync(CancellationToken cancellationToken = default)
    {
        var tools = new List<ServerChatTool>();
        foreach (var server in servers())
        {
            JsonElement? answer;
            try
            {
                answer = await server.Request(server.Capability.List, EmptyObject, ListTimeout, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("The language server '{Id}' did not list its AI Chat tools: {Reason}", server.Id, ex.Message);
                continue;
            }
            tools.AddRange(ReadTools(server, answer, log: true));
        }
        return tools;
    }

    private IEnumerable<ServerChatTool> ReadTools(ChatToolsServer server, JsonElement? answer, bool log)
    {
        var listed = answer switch
        {
            { ValueKind: JsonValueKind.Array } array => array,
            { ValueKind: JsonValueKind.Object } o when o.TryGetProperty("tools", out var t) && t.ValueKind == JsonValueKind.Array => t,
            _ => (JsonElement?)null,
        };
        if (listed is not { } items)
        {
            if (log) logger.LogWarning("The language server '{Id}' answered its AI Chat tool list with no tools array.", server.Id);
            yield break;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var skipped = 0;
        foreach (var item in items.EnumerateArray())
        {
            if (ReadTool(server, item) is not { } tool || !names.Add(tool.Name)) { skipped++; continue; }
            if (names.Count > ServerChatToolLimits.MaxToolsPerServer)
            {
                if (log) logger.LogWarning("The language server '{Id}' offers more than {Max} AI Chat tools; the rest are left out.",
                    server.Id, ServerChatToolLimits.MaxToolsPerServer);
                yield break;
            }
            yield return tool;
        }
        if (skipped > 0 && log)
            logger.LogWarning("The language server '{Id}' offered {Count} AI Chat tool(s) HexIDE could not take: a name missing, "
                + "repeated, longer than {MaxName} characters or with a character other than a letter, a digit, _, -, . or /, "
                + "or a schema that isn't an object or is over {MaxSchema} bytes.",
                server.Id, skipped, ServerChatToolLimits.MaxName, ServerChatToolLimits.MaxSchemaBytes);
    }

    private static ServerChatTool? ReadTool(ChatToolsServer server, JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("name", out var nameValue) || nameValue.ValueKind != JsonValueKind.String
            || nameValue.GetString() is not { Length: > 0 and <= ServerChatToolLimits.MaxName } name
            || !name.All(IsNameCharacter))
            return null;

        var schema = EmptySchema;
        if (item.TryGetProperty("inputSchema", out var declared))
        {
            if (declared.ValueKind != JsonValueKind.Object) return null;
            if (Encoding.UTF8.GetByteCount(declared.GetRawText()) > ServerChatToolLimits.MaxSchemaBytes) return null;
            schema = declared.Clone();
        }

        var description = item.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String
            ? Cut(Clean(d.GetString()!), ServerChatToolLimits.MaxDescription, out _)
            : "";

        bool? readOnly = null, destructive = null;
        var runsCode = false;
        if (item.TryGetProperty("annotations", out var annotations) && annotations.ValueKind == JsonValueKind.Object)
        {
            readOnly = Flag(annotations, "readOnlyHint");
            destructive = Flag(annotations, "destructiveHint");
            // Present and anything but false counts: a hint that fails to read errs towards asking, as the other two do.
            runsCode = annotations.TryGetProperty(RunsCodeAnnotation, out var runs) && runs.ValueKind != JsonValueKind.False;
        }

        return new ServerChatTool(server.Id, server.DisplayName, name, description, schema,
            ServerChatToolsApproval.Asks(server.Policy, readOnly, destructive, runsCode));
    }

    // A name is shown in the question that asks the person, so it is held to characters that can't reorder, hide or format
    // the text around them.
    private static bool IsNameCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or '/';

    private static bool? Flag(JsonElement annotations, string name) =>
        annotations.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    public async Task<ServerChatToolResult> CallAsync(ServerChatTool tool, JsonElement? arguments, ServerChatToolApproval approve,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(approve);
        // Found again, by id: the call goes only to the server that offered the tool, with the method it names now.
        if (servers().FirstOrDefault(s => string.Equals(s.Id, tool.ServerId, StringComparison.Ordinal)) is not { } server)
            return new ServerChatToolResult(true, $"The language server '{tool.ServerName}' isn't running, or no longer offers AI Chat tools.") { ReasonKey = "Str.AIChat.ServerToolNotRunning", ReasonArguments = [tool.ServerName] };

        // Listed again, so whether it asks is this host's reading of what the server says now, not the caller's.
        JsonElement? listing;
        try
        {
            listing = await server.Request(server.Capability.List, EmptyObject, ListTimeout, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("The language server '{Id}' did not list its AI Chat tools before a call: {Reason}", server.Id, ex.Message);
            return new ServerChatToolResult(true, $"The language server '{server.DisplayName}' didn't list its tools, so nothing was called.") { ReasonKey = "Str.AIChat.ServerToolListFailed", ReasonArguments = [server.DisplayName] };
        }
        if (ReadTools(server, listing, log: false).FirstOrDefault(t => string.Equals(t.Name, tool.Name, StringComparison.Ordinal))
            is not { } current)
            return new ServerChatToolResult(true, $"The language server '{server.DisplayName}' doesn't offer a tool called '{tool.Name}'.") { ReasonKey = "Str.AIChat.ServerToolNotOffered", ReasonArguments = [server.DisplayName, tool.Name] };
        if (current.Asks && !await approve(current, cancellationToken))
            return new ServerChatToolResult(true, "The user declined this action.", Declined: true);

        var parameters = JsonSerializer.SerializeToElement(new CallParameters(current.Name,
            arguments is { ValueKind: JsonValueKind.Object } given ? given : EmptyObject), CallParametersContext.Default.CallParameters);
        JsonElement? answer;
        try
        {
            answer = await server.Request(server.Capability.Call, parameters, CallTimeout, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("The language server '{Id}' failed AI Chat's call to its tool '{Tool}': {Reason}", server.Id, tool.Name, ex.Message);
            return new ServerChatToolResult(true, Cut($"The language server '{server.DisplayName}' didn't complete the call: {ex.Message}",
                ServerChatToolLimits.MaxResult, out _))
                { ReasonKey = "Str.AIChat.ServerToolCallFailed", ReasonArguments = [server.DisplayName, ex.Message] };
        }

        var (isError, text) = ReadResult(answer);
        return new ServerChatToolResult(isError, Cut(text, ServerChatToolLimits.MaxResult, out var cut), cut);
    }

    private static (bool IsError, string Text) ReadResult(JsonElement? answer)
    {
        if (answer is not { } result || result.ValueKind == JsonValueKind.Null) return (false, "");
        if (result.ValueKind == JsonValueKind.String) return (false, Clean(result.GetString()!));
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array)
            return (false, Clean(result.GetRawText()));

        var isError = result.TryGetProperty("isError", out var e) && e.ValueKind == JsonValueKind.True;
        var parts = new List<string>();
        foreach (var part in content.EnumerateArray())
        {
            var type = part.ValueKind == JsonValueKind.Object && part.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString()
                : null;
            if (type == "text" && part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                parts.Add(Clean(text.GetString()!));
            else
                parts.Add($"[{Clean(type ?? "unknown")} content omitted]");
        }
        return (isError, string.Join("\n", parts));
    }

    public async Task<IReadOnlyList<ServerChatContext>> GetContextAsync(CancellationToken cancellationToken = default)
    {
        var contexts = new List<ServerChatContext>();
        foreach (var server in servers())
        {
            // A server set to ask for everything gives no context: context goes on every send, with nothing to ask.
            if (server.Policy == ServerChatToolsPolicy.AskEveryCall) continue;
            if (server.Capability.Context is not { } method) continue;
            JsonElement? answer;
            try
            {
                answer = await server.Request(method, EmptyObject, ContextTimeout, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("The language server '{Id}' did not give AI Chat its context: {Reason}", server.Id, ex.Message);
                continue;
            }

            var text = answer switch
            {
                { ValueKind: JsonValueKind.String } s => s.GetString(),
                { ValueKind: JsonValueKind.Object } o when o.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String => t.GetString(),
                _ => null,
            };
            if (string.IsNullOrWhiteSpace(text)) continue;
            contexts.Add(new ServerChatContext(server.Id, server.DisplayName,
                Cut(Clean(text), ServerChatToolLimits.MaxContext, out var cut), cut));
        }
        return contexts;
    }

    /// <summary>
    /// A server's text without control characters other than tab and line breaks, and without format characters (the
    /// bidirectional overrides among them) other than the zero-width joiner emoji need.
    /// </summary>
    private static string Clean(string text) => text.Any(Dropped) ? new string(text.Where(c => !Dropped(c)).ToArray()) : text;

    private static bool Dropped(char c) =>
        (char.IsControl(c) && c is not ('\n' or '\r' or '\t'))
        || (char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format && c != '\u200D');

    private static string Cut(string text, int max, out bool cut)
    {
        cut = text.Length > max;
        if (!cut) return text;
        // Never between the halves of a surrogate pair.
        var end = char.IsHighSurrogate(text[max - 1]) ? max - 1 : max;
        return text[..end];
    }
}

/// <summary>The parameters of a call: the tool's name and its arguments, as MCP's <c>tools/call</c> takes them.</summary>
internal sealed record CallParameters(
    [property: System.Text.Json.Serialization.JsonPropertyName("name")] string Name,
    [property: System.Text.Json.Serialization.JsonPropertyName("arguments")] JsonElement Arguments);

[System.Text.Json.Serialization.JsonSerializable(typeof(CallParameters))]
internal sealed partial class CallParametersContext : System.Text.Json.Serialization.JsonSerializerContext;
