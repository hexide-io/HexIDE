using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace HexIDE.Desktop.Server;

/// <summary>
/// The tool-call filters that decide whether a call runs at all, in the order they must run. Shared by
/// <c>IdeServer</c> and by the tests that build a server like it, so the two cannot disagree about the order.
/// </summary>
internal static class ToolFilters
{
    /// <summary>
    /// Text is checked before SDK request deserialization (#1337), then the session guard (#1306) and failure
    /// reporting (#603). Among tool-call filters the guard comes first, so a call from another
    /// agent session is told it reached the wrong IDE, not how to fix its arguments for a tool on an IDE that is not
    /// its own.
    /// </summary>
    public static IMcpServerBuilder Register(IMcpServerBuilder builder, string? session) =>
        SessionGuard.Register(builder, session)
            .WithMessageFilters(filters => filters.AddIncomingFilter(ToolFailures.TextFilter))
            .WithRequestFilters(filters => filters.AddCallToolFilter(ToolFailures.Filter));
}
