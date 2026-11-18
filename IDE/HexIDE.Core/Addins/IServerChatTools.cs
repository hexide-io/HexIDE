using System.Text.Json;

namespace HexIDE.Addins;

/// <summary>
/// The tools and context fragments running language servers give AI Chat, over the connection HexIDE already holds for
/// each (#4257, decided on the Team Desk, 2026-11-16).
/// </summary>
/// <remarks>
/// <para>
/// <b>Where they come from:</b> only a server the person configured themselves, HexIDE's own, or one a consented add-in
/// contributed, and only while it runs. A server's <c>lsp-servers.json</c> entry can turn its tools off, or make every
/// call ask (<c>"chatTools": "off"</c> or <c>"ask"</c>).
/// </para>
/// <para>
/// <b>What the host decides, not the caller:</b> which servers count, which names a tool may have and how long its text
/// may be, whether a call asks first, and that a call goes only to the server that offered the tool. On each call the host
/// lists that server's tools again and asks through the caller's <see cref="ServerChatToolApproval"/> when its own reading
/// says to; the caller only shows the question.
/// </para>
/// </remarks>
public interface IServerChatTools
{
    /// <summary>Every tool the running servers offer now. A server that fails to answer is left out, and logged.</summary>
    Task<IReadOnlyList<ServerChatTool>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls the tool named <paramref name="tool"/>'s <see cref="ServerChatTool.Name"/> on the server
    /// <see cref="ServerChatTool.ServerId"/> names, with <paramref name="arguments"/> as its arguments. Nothing else of the
    /// record is read: the host lists that server's tools again, refuses a name it no longer offers, and calls
    /// <paramref name="approve"/> first when its own reading says the call asks.
    /// </summary>
    /// <returns>
    /// The server's answer as text, or why there is none, as an error result; <see cref="ServerChatToolResult.Declined"/>
    /// when the person said no. It never throws for the server.
    /// </returns>
    Task<ServerChatToolResult> CallAsync(ServerChatTool tool, JsonElement? arguments, ServerChatToolApproval approve,
        CancellationToken cancellationToken = default);

    /// <summary>The context fragments the running servers that offer one give now: one per server, in server order.</summary>
    Task<IReadOnlyList<ServerChatContext>> GetContextAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Asks the person whether <paramref name="tool"/> may run, as the host read it from its server just now. True for yes.
/// </summary>
public delegate Task<bool> ServerChatToolApproval(ServerChatTool tool, CancellationToken cancellationToken);

/// <summary>A tool a language server offers AI Chat.</summary>
/// <param name="ServerId">The server's id, as the Language Servers window shows it.</param>
/// <param name="ServerName">The server's display name, for the person and the assistant.</param>
/// <param name="Name">
/// The tool's name as the server gave it, which is what a call sends back: letters, digits, <c>_</c>, <c>-</c>, <c>.</c> and
/// <c>/</c> only, at most <see cref="ServerChatToolLimits.MaxName"/>.
/// </param>
/// <param name="Description">The server's description of it, cut to <see cref="ServerChatToolLimits.MaxDescription"/>.</param>
/// <param name="InputSchema">Its arguments' JSON schema, an object.</param>
/// <param name="Asks">True when a call asks the person first, as the host read it when it listed the tool.</param>
public sealed record ServerChatTool(
    string ServerId,
    string ServerName,
    string Name,
    string Description,
    JsonElement InputSchema,
    bool Asks);

/// <summary>What a server's tool answered.</summary>
/// <param name="IsError">True when the server said the call failed, or there was no answer.</param>
/// <param name="Text">The answer as text, cut to <see cref="ServerChatToolLimits.MaxResult"/>.</param>
/// <param name="Truncated">True when the text was cut.</param>
/// <param name="Declined">True when the call asked and the person said no, so nothing was sent to the server.</param>
public sealed record ServerChatToolResult(bool IsError, string Text, bool Truncated = false, bool Declined = false)
{
    // Init-only, not positional: a new positional parameter would replace the record's constructor and Deconstruct,
    // which add-ins compiled against the SDK call (#4566, #4567).

    /// <summary>A localization key for a refusal HexIDE wrote, or null. <see cref="Text"/> stays English for the AI service.</summary>
    public string? ReasonKey { get; init; }

    /// <summary>The facts to insert in the localized refusal: server and tool names, an exception's message.</summary>
    public object[]? ReasonArguments { get; init; }
}

/// <summary>A context fragment a server gave.</summary>
public sealed record ServerChatContext(string ServerId, string ServerName, string Text, bool Truncated = false);

/// <summary>How much of a server's text AI Chat takes, so no server can fill the assistant's context (#4257).</summary>
public static class ServerChatToolLimits
{
    /// <summary>Tools taken from one server; the rest are left out and logged.</summary>
    public const int MaxToolsPerServer = 32;

    /// <summary>A tool name, as MCP and the AI services allow it.</summary>
    public const int MaxName = 64;

    /// <summary>A tool's description, in characters.</summary>
    public const int MaxDescription = 1024;

    /// <summary>A tool's input schema, as UTF-8 JSON; a tool with a larger one is left out.</summary>
    public const int MaxSchemaBytes = 8 * 1024;

    /// <summary>A call's answer, in characters.</summary>
    public const int MaxResult = 16 * 1024;

    /// <summary>A server's context fragment, in characters.</summary>
    public const int MaxContext = 8 * 1024;
}
