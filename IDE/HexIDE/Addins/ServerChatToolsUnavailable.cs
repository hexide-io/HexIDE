using System.Text.Json;

namespace HexIDE.Addins;

/// <summary>
/// What an add-in that is not HexIDE's own sees of language servers' AI Chat tools: none (#4257).
/// </summary>
/// <remarks>
/// The caller supplies the approval callback, so a third-party add-in holding the real service could pass one that always
/// says yes and drive any server's tools with no question asked. Only a first-party add-in, or one a developer loaded
/// unsigned in a Debug build's developer mode, gets the real service; see <see cref="AddinOwnedHost"/>.
/// </remarks>
internal sealed class ServerChatToolsUnavailable : IServerChatTools
{
    public static ServerChatToolsUnavailable Instance { get; } = new();

    private ServerChatToolsUnavailable() { }

    public Task<IReadOnlyList<ServerChatTool>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ServerChatTool>>([]);

    public Task<ServerChatToolResult> CallAsync(ServerChatTool tool, JsonElement? arguments, ServerChatToolApproval approve,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ServerChatToolResult(true, "Language servers' AI Chat tools are available only to HexIDE's own add-ins.",
            ReasonKey: "Str.AIChat.ServerToolsUnavailable"));

    public Task<IReadOnlyList<ServerChatContext>> GetContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ServerChatContext>>([]);
}
