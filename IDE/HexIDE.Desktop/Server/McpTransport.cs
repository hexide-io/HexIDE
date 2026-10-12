using ModelContextProtocol.AspNetCore;

namespace HexIDE.Desktop.Server;

/// <summary>
/// How the automation server speaks Streamable HTTP. Kept apart from <see cref="IdeServer"/> so a test can
/// build a server with exactly this configuration and restart it under a real client (McpRestartTests).
/// </summary>
internal static class McpTransport
{
    /// <summary>
    /// Stateless: no <c>Mcp-Session-Id</c>, so nothing a client holds can go stale when the IDE is relaunched.
    /// </summary>
    /// <remarks>
    /// <para>The SDK's default is stateful. A relaunched IDE then answers every attached client's next request with
    /// 404 "Session not found", and the SDK's own client does not recover from that. The documented rebuild loop
    /// relaunches the IDE on every iteration.</para>
    /// <para><b>What this gives up, and why that is free today:</b> a stateless server cannot send anything the
    /// client did not ask for. That means no <c>tools/list_changed</c> or other notification outside a call, and no
    /// sampling, elicitation or roots request. The tools use none of these. A change that adds one has to revisit
    /// this setting rather than find the feature silently missing.</para>
    /// </remarks>
    public static void Configure(HttpServerTransportOptions options)
    {
        options.Stateless = true;
    }
}
