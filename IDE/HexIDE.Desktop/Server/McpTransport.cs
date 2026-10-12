using ModelContextProtocol.AspNetCore;

namespace HexIDE.Desktop.Server;

/// <summary>
/// How the automation server speaks Streamable HTTP. Kept apart from <see cref="IdeServer"/> so a test can
/// build a server with exactly this configuration and restart it under a real client (McpRestartTests).
/// </summary>
internal static class McpTransport
{
    /// <summary>
    /// Stateless: no <c>Mcp-Session-Id</c> and no GET stream, so a relaunch leaves an attached client nothing
    /// that has gone stale.
    /// </summary>
    /// <remarks>
    /// <para>The SDK's default is stateful, and the documented rebuild loop relaunches the IDE every iteration.
    /// Stateful, a relaunched IDE answers an attached client's next request with 404 "Session not found", which
    /// the SDK's own client does not recover from. Worse for Claude Code, it holds the GET stream a stateful server
    /// offers, and a shutdown within a few minutes of a connect made it close the transport and give up until
    /// <c>/mcp</c>. McpRestartTests has both.</para>
    /// <para><b>What this gives up.</b> A stateless server cannot send anything the client did not ask for: no
    /// <c>tools/list_changed</c> or other notification outside a call, and no sampling, elicitation or roots
    /// request. The tools use none of these. A change that adds one has to revisit this setting rather than find
    /// the feature silently missing. There is one side effect: a tool added in a rebuild used to reach Claude Code
    /// when it re-initialised after the 404, and with no 404 it now needs a <c>/mcp</c> reconnect.</para>
    /// </remarks>
    public static void Configure(HttpServerTransportOptions options)
    {
        options.Stateless = true;
    }
}
