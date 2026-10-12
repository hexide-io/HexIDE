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
    /// Stateful, a relaunched IDE answers an attached client's next request with 404 "Session not found". Neither
    /// of the official SDK clients recovers from that: C# 1.3.0 expires its session, and the TypeScript client has
    /// no handling for it. Claude Code also holds the GET stream a stateful server offers, and a shutdown within a
    /// few minutes of a connect made it close the transport and give up until <c>/mcp</c>. McpRestartTests has
    /// both.</para>
    /// <para><b>Why stateless, rather than stateful with the GET stream refused.</b> That alternative would silence
    /// the shutdown but keep the 404, which only a client with recovery of its own, such as Claude Code, survives.
    /// And the protocol has dropped sessions anyway: revision 2026-07-28 removes <c>Mcp-Session-Id</c> and the GET
    /// stream, and the SDK's 2.x line is stateless by default (#764). Stateful would be a stopgap to undo.</para>
    /// <para><b>What this gives up.</b> A stateless server cannot send anything the client did not ask for: no
    /// <c>tools/list_changed</c> or other notification outside a call, and no sampling, elicitation or roots
    /// request. Nor can a client cancel a call by notification: <c>notifications/cancelled</c> arrives as a POST of
    /// its own, which builds a fresh server with nothing in flight, so it cancels nothing. Aborting the call's own
    /// request still cancels it. The tools use none of these, and the one long wait a caller might cancel,
    /// <c>hover</c>'s dwell, is capped at ten seconds. A change that needs one of them has to revisit this setting
    /// rather than find the feature silently missing.</para>
    /// <para>One side effect: a tool added in a rebuild used to reach Claude Code when it re-initialised after the
    /// 404. With no 404 it now needs a <c>/mcp</c> reconnect.</para>
    /// </remarks>
    public static void Configure(HttpServerTransportOptions options)
    {
        options.Stateless = true;
    }
}
