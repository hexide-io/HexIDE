using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace HexIDE.Desktop.Server;

/// <summary>
/// Tells the status bar's automation section when a tool call starts and ends (#2451), so a person sees that the IDE is
/// being driven, and when it last was.
/// </summary>
/// <remarks>
/// Registered after the session guard, so a call from another session counts as refused and not as the last call this
/// IDE ran; and before failure reporting, so a call refused for its arguments still counts as a call.
/// </remarks>
internal static class ActivityFilter
{
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (request, ct) =>
        {
            var status = HexIDE.Automation.AutomationServerStatus.Current;
            status.CallStarted(request.Params?.Name ?? "(unnamed)");
            try
            {
                return await next(request, ct);
            }
            finally
            {
                status.CallEnded();
            }
        };
}
