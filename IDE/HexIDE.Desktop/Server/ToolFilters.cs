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
    /// The session guard (#1306) and then the failure reporting (#603). The guard comes first, so a call from another
    /// agent session is told it reached the wrong IDE, not how to fix its arguments for a tool on an IDE that is not
    /// its own.
    /// </summary>
    public static IMcpServerBuilder Register(IMcpServerBuilder builder, string? session) =>
        SessionGuard.Register(builder, session)
            .WithRequestFilters(filters => filters.AddCallToolFilter(ToolFailures.Filter));
}
