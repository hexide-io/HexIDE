using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Serilog;

namespace HexIDE.Desktop.Server;

/// <summary>
/// Turns an exception a tool did not catch into a reply that says what happened.
/// </summary>
/// <remarks>
/// <para>
/// Without this, the SDK answers any such exception with "An error occurred invoking '&lt;tool&gt;'." and
/// nothing else, so a caller cannot tell a bad argument from a defect from a broken IDE. It had been met
/// three separate times, each time dug out of the log (#603).
/// </para>
/// <para>
/// A backstop, not a substitute: a tool should still refuse bad input itself, with a reason, before anything
/// throws. The reply says it is a defect so it cannot be mistaken for one of those refusals. The stack goes to
/// the IDE log under the same tool name, not to the caller. <see cref="McpException"/> and cancellation are
/// left to the SDK, which already reports them as intended.
/// </para>
/// </remarks>
internal static class ToolFailures
{
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (request, ct) =>
        {
            try
            {
                return await next(request, ct);
            }
            catch (Exception ex) when (ex is not McpException and not OperationCanceledException)
            {
                var tool = request.Params?.Name ?? "(unnamed)";
                Log.Error(ex, "MCP tool {Tool} failed with an unhandled {ExceptionType}", tool, ex.GetType().Name);
                return new CallToolResult
                {
                    IsError = true,
                    Content = [new TextContentBlock { Text = Describe(tool, ex) }],
                };
            }
        };

    internal static string Describe(string tool, Exception ex) =>
        $"'{tool}' failed with an unhandled {ex.GetType().FullName}: {ex.Message} "
        + "This is a defect in the tool, not a refusal of your arguments: please report it. "
        + $"The stack trace is in the IDE log (%LOCALAPPDATA%/HexIDE/logs/ide/), under 'MCP tool {tool} failed'.";
}
